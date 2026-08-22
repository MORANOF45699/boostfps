using System.Diagnostics;
using System.Management;

namespace BoostFPS.Core.Services;

public sealed record RestorePointResult(bool Created, long? Sequence, string Message);

/// <summary>
/// Creates a System Restore checkpoint before an apply run. System Protection must be on
/// for the system drive; when it is off we report that instead of silently continuing.
/// </summary>
public sealed class RestorePointService
{
    private const string SystemRestoreKey =
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    /// <summary>True when System Protection is enabled for the system drive.</summary>
    public bool IsProtectionEnabled()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\default", "SELECT * FROM SystemRestore");
            _ = searcher.Get().Count;

            using var key = RegistryPath.OpenRead(SystemRestoreKey);
            // RPSessionInterval > 0 means the restore engine is active on this machine.
            var disabled = key?.GetValue("DisableSR");
            return disabled is null || Convert.ToInt32(disabled) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enables System Protection for the system drive. Requires elevation.
    /// Uses PowerShell because Enable-ComputerRestore has no plain WMI equivalent.
    /// </summary>
    public bool TryEnableProtection()
    {
        var drive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        return RunPowerShell($"Enable-ComputerRestore -Drive '{drive}'");
    }

    /// <summary>
    /// Creates a checkpoint and returns its sequence number. Windows throttles checkpoints to
    /// one per 24h by default; SystemRestorePointCreationFrequency=0 removes that for our run.
    /// </summary>
    public RestorePointResult Create(string description)
    {
        try
        {
            using (var key = RegistryPath.OpenWrite(SystemRestoreKey))
            {
                key.SetValue("SystemRestorePointCreationFrequency", 0, Microsoft.Win32.RegistryValueKind.DWord);
            }

            using var cls = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            var args = cls.GetMethodParameters("CreateRestorePoint");
            args["Description"] = description;
            args["RestorePointType"] = 12;  // MODIFY_SETTINGS
            args["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE

            var result = cls.InvokeMethod("CreateRestorePoint", args, null);
            var rc = Convert.ToInt32(result?["ReturnValue"] ?? -1);

            if (rc != 0)
                return new RestorePointResult(false, null, $"CreateRestorePoint returned {rc}");

            return new RestorePointResult(true, LatestSequence(), "Restore point created");
        }
        catch (Exception ex)
        {
            return new RestorePointResult(false, null, ex.Message);
        }
    }

    private static long? LatestSequence()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\default", "SELECT SequenceNumber FROM SystemRestore");

            long? max = null;
            foreach (ManagementObject mo in searcher.Get())
            {
                var seq = Convert.ToInt64(mo["SequenceNumber"]);
                if (max is null || seq > max) max = seq;
            }
            return max;
        }
        catch { return null; }
    }

    private static bool RunPowerShell(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(120_000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
