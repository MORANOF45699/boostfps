using System.Diagnostics;
using Microsoft.Win32;

namespace BoostFPS.Core.Services;

public sealed record RunningGame(int Pid, string ProcessName, string WindowTitle, string ExePath)
{
    public string ExeName => Path.GetFileName(ExePath);
}

public enum WindowsGpuPreference
{
    SystemDefault = 0,
    PowerSaving = 1,
    HighPerformance = 2
}

/// <summary>An exe already registered in Settings > System > Display > Graphics.</summary>
public sealed record GraphicsPref(string ExePath, WindowsGpuPreference Preference)
{
    public string ExeName => Path.GetFileName(ExePath);
    public bool Exists => File.Exists(ExePath);
}

/// <summary>
/// Registers an executable as a "game" for Windows Game Mode / Game Bar, and pins its
/// live process at High priority with an affinity that avoids core 0 (where kernel DPCs
/// tend to land). Everything reversible: RemoveFromGameMode wipes the key, priority
/// resets when the process exits.
/// </summary>
public sealed class GameBoostService
{
    private const string GameConfigStore = @"HKCU\System\GameConfigStore";
    private const string UserGpuPreferences = @"HKCU\SOFTWARE\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>
    /// Enumerates processes that look like foreground games — visible main window, exe path
    /// readable, and not obviously a system process. Best-effort; some are just skipped.
    /// </summary>
    public IReadOnlyList<RunningGame> ListCandidates()
    {
        var results = new List<RunningGame>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero) continue;
                if (string.IsNullOrEmpty(p.MainWindowTitle)) continue;

                var path = SafeExePath(p);
                if (path is null) continue;

                var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (SystemProcesses.Contains(name)) continue;

                // Windows system exe paths.
                var lower = path.ToLowerInvariant();
                if (lower.Contains(@"\windows\system32\") || lower.Contains(@"\windows\syswow64\"))
                    continue;

                results.Add(new RunningGame(p.Id, p.ProcessName, p.MainWindowTitle, path));
            }
            catch { /* access denied on some system processes */ }
            finally { p.Dispose(); }
        }

        return results
            .OrderBy(g => g.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Registers the exe with Game Bar the same way "Remember this is a game" does:
    /// creates HKCU\System\GameConfigStore\Children\<guid> with MatchedExeFullPath,
    /// and per-exe overrides under HKCU\System\GameConfigStore\<exeName> forcing
    /// Game DVR off and Fullscreen Exclusive on.
    /// </summary>
    public void AddToGameMode(string exePath)
    {
        var id = Guid.NewGuid().ToString("B").ToUpperInvariant();

        using (var child = RegistryPath.OpenWrite($@"{GameConfigStore}\Children\{id}"))
        {
            child.SetValue("MatchedExeFullPath", exePath, RegistryValueKind.String);
            child.SetValue("MatchedTitleId", "0", RegistryValueKind.QWord);
        }

        var name = Path.GetFileName(exePath);
        using var perExe = RegistryPath.OpenWrite($@"{GameConfigStore}\{name}");
        perExe.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
        perExe.SetValue("GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
        perExe.SetValue("GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
        perExe.SetValue("GameDVR_DXGIHonorFSEWindowsCompatible", 1, RegistryValueKind.DWord);
    }

    public bool IsInGameMode(string exePath)
    {
        var target = Path.GetFullPath(exePath);

        foreach (var childName in RegistryPath.SubKeyNames($@"{GameConfigStore}\Children"))
        {
            using var child = RegistryPath.OpenRead($@"{GameConfigStore}\Children\{childName}");
            if (child?.GetValue("MatchedExeFullPath") is string p
                && string.Equals(p, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public void RemoveFromGameMode(string exePath)
    {
        var target = Path.GetFullPath(exePath);

        foreach (var childName in RegistryPath.SubKeyNames($@"{GameConfigStore}\Children"))
        {
            using var child = RegistryPath.OpenRead($@"{GameConfigStore}\Children\{childName}");
            if (child?.GetValue("MatchedExeFullPath") is string p
                && string.Equals(p, target, StringComparison.OrdinalIgnoreCase))
            {
                using var root = RegistryPath.OpenWrite($@"{GameConfigStore}\Children");
                root.DeleteSubKeyTree(childName, throwOnMissingSubKey: false);
            }
        }

        using var perExeRoot = RegistryPath.OpenWrite(GameConfigStore);
        perExeRoot.DeleteSubKeyTree(Path.GetFileName(exePath), throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Sets the live process to High priority and pins affinity to all logical cores
    /// except core 0. Only affects this run; when the process exits, defaults return.
    /// </summary>
    public bool BoostLiveProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.PriorityClass = ProcessPriorityClass.High;

            if (Environment.ProcessorCount > 2)
            {
                var mask = (nint)((1L << Environment.ProcessorCount) - 2); // clear bit 0
                p.ProcessorAffinity = mask;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the exe list from Settings > System > Display > Graphics ("Custom settings for
    /// applications"). Each value name is the full exe path, data is a semicolon list
    /// including <c>GpuPreference=N</c>.
    /// </summary>
    public IReadOnlyList<GraphicsPref> ListWindowsGraphicsPreferences()
    {
        using var key = RegistryPath.OpenRead(UserGpuPreferences);
        if (key is null) return [];

        var results = new List<GraphicsPref>();
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var pref = WindowsGpuPreference.SystemDefault;

            if (key.GetValue(name) is string data)
            {
                foreach (var part in data.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=');
                    if (eq < 0) continue;
                    if (part[..eq].Trim().Equals("GpuPreference", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(part[(eq + 1)..].Trim(), out var value))
                    {
                        pref = (WindowsGpuPreference)value;
                    }
                }
            }

            results.Add(new GraphicsPref(name, pref));
        }
        return results;
    }

    /// <summary>Writes an exe into the Windows Graphics preferences list (or updates it).</summary>
    public void SetWindowsGpuPreference(string exePath, WindowsGpuPreference preference)
    {
        using var key = RegistryPath.OpenWrite(UserGpuPreferences);
        key.SetValue(exePath, $"GpuPreference={(int)preference};",
            Microsoft.Win32.RegistryValueKind.String);
    }

    public void RemoveWindowsGpuPreference(string exePath)
    {
        using var key = RegistryPath.OpenWrite(UserGpuPreferences);
        key.DeleteValue(exePath, throwOnMissingValue: false);
    }

    private static string? SafeExePath(Process p)
    {
        try { return p.MainModule?.FileName; }
        catch { return null; }
    }

    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "csrss", "smss", "wininit", "services", "lsass",
        "svchost", "taskhostw", "sihost", "ctfmon", "conhost", "fontdrvhost",
        "searchhost", "startmenuexperiencehost", "shellexperiencehost",
        "runtimebroker", "textinputhost", "applicationframehost",
        "systemsettings", "widgets", "widgetservice", "lockapp",
        "boostfps"
    };
}
