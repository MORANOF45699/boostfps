using System.Diagnostics;
using System.Xml.Linq;
using BoostFPS.Core.Models;

namespace BoostFPS.Core.Services;

public sealed record NvidiaApplyResult(bool Success, string Message, string? BackupFile);

/// <summary>
/// Reads .nip profile exports and applies them through nvidiaProfileInspector, which is the
/// only supported way to write NVIDIA driver profile settings without reimplementing NVAPI DRS.
/// Every import is preceded by a full export of the current driver profile database.
/// </summary>
public sealed class NvidiaProfileService(MachineProfile machine)
{
    private static readonly string[] KnownInspectorPaths =
    [
        @"G:\windows\Boost fps\nvidiaProfileInspector\nvidiaProfileInspector.exe",
        @"G:\windows\BOOST\3 GPU\nvidiaProfileInspector.exe"
    ];

    private readonly MachineProfile _machine = machine;

    public bool IsNvidia => _machine.GpuVendor == GpuVendor.Nvidia;

    /// <summary>Path to nvidiaProfileInspector.exe, or null when it cannot be found.</summary>
    public string? FindInspector()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "Tools", "nvidiaProfileInspector.exe");
        if (File.Exists(beside)) return beside;

        var configured = Json.Read<Dictionary<string, string>>(AppPaths.SettingsFile);
        if (configured is not null
            && configured.TryGetValue("InspectorPath", out var custom)
            && File.Exists(custom))
        {
            return custom;
        }

        return KnownInspectorPaths.FirstOrDefault(File.Exists);
    }

    public void SetInspectorPath(string path)
    {
        AppPaths.EnsureCreated();
        var settings = Json.Read<Dictionary<string, string>>(AppPaths.SettingsFile) ?? [];
        settings["InspectorPath"] = path;
        Json.Write(AppPaths.SettingsFile, settings);
    }

    /// <summary>
    /// Parses a .nip file. The files declare <c>encoding="utf-16"</c> but are often saved
    /// without a UTF-16 BOM (nvidiaProfileInspector writes them that way), which makes
    /// <c>XDocument.Load</c> throw "Cannot switch to Unicode". We sniff the bytes ourselves
    /// and decode to a string so the parser only sees text without an encoding declaration.
    /// </summary>
    public static IReadOnlyList<NvidiaProfile> Parse(string nipPath)
    {
        var text = ReadAnyEncoding(nipPath);
        var doc = XDocument.Parse(text);

        return doc.Root?.Elements("Profile").Select(p => new NvidiaProfile
        {
            ProfileName = (string?)p.Element("ProfileName") ?? "",
            Executables = p.Element("Executeables")?.Elements("string").Select(s => s.Value).ToList() ?? [],
            Settings = p.Element("Settings")?.Elements("ProfileSetting").Select(s => new NvidiaSetting
            {
                SettingNameInfo = (string?)s.Element("SettingNameInfo") ?? "",
                SettingID = uint.TryParse((string?)s.Element("SettingID"), out var id) ? id : 0,
                SettingValue = (string?)s.Element("SettingValue") ?? "",
                ValueType = (string?)s.Element("ValueType") ?? "Dword"
            }).ToList() ?? []
        }).ToList() ?? [];
    }

    /// <summary>Exports the current driver profile database so an import can be undone.</summary>
    public string? ExportCurrent(string outDirectory)
    {
        var inspector = FindInspector();
        if (inspector is null) return null;

        Directory.CreateDirectory(outDirectory);
        var file = Path.Combine(outDirectory, $"nvidia_profiles_{DateTime.Now:yyyyMMdd-HHmmss}.nip");

        return Run(inspector, $"-export \"{file}\"") && File.Exists(file) ? file : null;
    }

    /// <summary>Backs up the current profiles, then silently imports the given .nip.</summary>
    public NvidiaApplyResult Import(string nipPath, string backupDirectory)
    {
        if (!IsNvidia)
            return new NvidiaApplyResult(false, "เครื่องนี้ไม่ได้ใช้ GPU NVIDIA", null);

        var inspector = FindInspector();
        if (inspector is null)
            return new NvidiaApplyResult(false,
                "ไม่พบ nvidiaProfileInspector.exe — ตั้ง path เองในหน้า NVIDIA", null);

        if (!File.Exists(nipPath))
            return new NvidiaApplyResult(false, $"ไม่พบไฟล์ {nipPath}", null);

        var backup = ExportCurrent(backupDirectory);

        return Run(inspector, $"-silentImport \"{nipPath}\"")
            ? new NvidiaApplyResult(true, $"import {Path.GetFileName(nipPath)} สำเร็จ", backup)
            : new NvidiaApplyResult(false, "nvidiaProfileInspector คืนค่า exit code ที่ไม่ใช่ 0", backup);
    }

    /// <summary>
    /// Decodes the file with the right encoding regardless of BOM and returns text with the
    /// XML declaration's encoding attribute stripped, so <c>XDocument.Parse</c> won't complain
    /// about mismatched byte width.
    /// </summary>
    private static string ReadAnyEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);

        string text;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            text = System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            text = System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        else if (LooksLikeUtf16Le(bytes))
            text = System.Text.Encoding.Unicode.GetString(bytes);
        else
            text = System.Text.Encoding.UTF8.GetString(bytes);

        // Kill the encoding attribute so XDocument doesn't try to reinterpret bytes we've
        // already decoded. Keep the rest of the declaration intact.
        return System.Text.RegularExpressions.Regex.Replace(
            text, @"\s+encoding\s*=\s*[""'][^""']+[""']", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>Heuristic: an XML file with no BOM but every second byte is 0 is UTF-16 LE.</summary>
    private static bool LooksLikeUtf16Le(byte[] bytes)
    {
        if (bytes.Length < 16) return false;
        var zeros = 0;
        for (var i = 1; i < Math.Min(bytes.Length, 128); i += 2)
            if (bytes[i] == 0) zeros++;
        return zeros > 30;
    }

    private static bool Run(string exe, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p is null) return false;

            p.WaitForExit(120_000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
