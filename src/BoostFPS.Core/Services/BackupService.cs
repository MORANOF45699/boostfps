using System.Diagnostics;
using BoostFPS.Core.Models;

namespace BoostFPS.Core.Services;

/// <summary>
/// Captures the exact prior state of everything an apply run will touch, and writes it to
/// disk BEFORE the first write. Revert always prefers this snapshot over any built-in
/// "Windows default" table, because defaults differ per build and per machine.
/// </summary>
public sealed class BackupService
{
    /// <summary>Builds a snapshot in memory. Nothing is written to the registry here.</summary>
    public BackupSnapshot Capture(
        string description,
        IEnumerable<ResolvedTarget> registryTargets,
        IEnumerable<string> serviceNames,
        IEnumerable<string> appliedTweakIds)
    {
        var snapshot = new BackupSnapshot
        {
            Id = DateTime.Now.ToString("yyyyMMdd-HHmmss"),
            Description = description,
            AppliedTweakIds = appliedTweakIds.ToList()
        };

        foreach (var target in registryTargets.DistinctBy(t => (t.RegPath, t.ValueName)))
        {
            using var key = RegistryPath.OpenRead(target.RegPath);
            var value = key?.GetValue(target.ValueName);

            snapshot.RegistryValues.Add(new RegistryValueSnapshot
            {
                RegPath = target.RegPath,
                ValueName = target.ValueName,
                Existed = value is not null,
                Kind = value is null ? null : key!.GetValueKind(target.ValueName).ToString(),
                Value = value is byte[] bytes ? Convert.ToHexString(bytes) : value
            });
        }

        foreach (var name in serviceNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var start = WindowsServiceService.ReadStart(name);
            snapshot.Services.Add(new ServiceSnapshot
            {
                Name = name,
                Existed = start is not null,
                Start = start ?? ServiceStart.Manual
            });
        }

        return snapshot;
    }

    /// <summary>
    /// Persists the snapshot plus a reg.exe export of every touched key, so the machine can be
    /// recovered by hand even if this app never runs again.
    /// </summary>
    public string Persist(BackupSnapshot snapshot)
    {
        AppPaths.EnsureCreated();
        var dir = Path.Combine(AppPaths.Backups, snapshot.Id);
        Directory.CreateDirectory(dir);

        var roots = snapshot.RegistryValues
            .Select(v => v.RegPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = 0;
        foreach (var root in roots)
        {
            var file = Path.Combine(dir, $"key_{index++:D3}.reg");
            if (ExportKey(root, file)) snapshot.ExportedRegFiles.Add(Path.GetFileName(file));
        }

        Json.Write(Path.Combine(dir, "snapshot.json"), snapshot);
        return dir;
    }

    public IReadOnlyList<BackupSnapshot> List()
    {
        AppPaths.EnsureCreated();

        return Directory.EnumerateDirectories(AppPaths.Backups)
            .Select(d => Path.Combine(d, "snapshot.json"))
            .Where(File.Exists)
            .Select(Json.Read<BackupSnapshot>)
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    /// <summary>Puts every captured registry value and service start type back exactly as it was.</summary>
    public RestoreReport Restore(BackupSnapshot snapshot)
    {
        var report = new RestoreReport();

        foreach (var value in snapshot.RegistryValues)
        {
            try
            {
                RegistryTweakService.Restore(Rehydrate(value));
                report.RegistryRestored++;
            }
            catch (Exception ex)
            {
                report.Failures.Add($"{value.RegPath}\\{value.ValueName}: {ex.Message}");
            }
        }

        foreach (var svc in snapshot.Services.Where(s => s.Existed))
        {
            try
            {
                WindowsServiceService.WriteStart(svc.Name, svc.Start);
                report.ServicesRestored++;
            }
            catch (Exception ex)
            {
                report.Failures.Add($"service {svc.Name}: {ex.Message}");
            }
        }

        return report;
    }

    /// <summary>Binary values are stored as hex strings in JSON; turn them back into bytes.</summary>
    private static RegistryValueSnapshot Rehydrate(RegistryValueSnapshot v)
    {
        if (v.Kind != "Binary" || v.Value is null) return v;

        var hex = v.Value.ToString() ?? "";
        return new RegistryValueSnapshot
        {
            RegPath = v.RegPath,
            ValueName = v.ValueName,
            Existed = v.Existed,
            Kind = v.Kind,
            Value = Convert.FromHexString(hex)
        };
    }

    private static bool ExportKey(string regPath, string outFile)
    {
        try
        {
            var psi = new ProcessStartInfo("reg.exe")
            {
                Arguments = $"export \"{RegistryPath.ToRegExeForm(regPath)}\" \"{outFile}\" /y",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(30_000);
            return p.ExitCode == 0 && File.Exists(outFile);
        }
        catch { return false; }
    }
}

public sealed class RestoreReport
{
    public int RegistryRestored { get; set; }
    public int ServicesRestored { get; set; }
    public List<string> Failures { get; } = [];
    public bool Success => Failures.Count == 0;
}
