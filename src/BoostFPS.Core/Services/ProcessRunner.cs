using System.Diagnostics;

namespace BoostFPS.Core.Services;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
    public string Output => string.IsNullOrWhiteSpace(StdErr) ? StdOut : $"{StdOut}\n{StdErr}";
}

public static class ProcessRunner
{
    public static ProcessResult Run(string exe, string arguments, int timeoutMs = 120_000)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var p = Process.Start(psi);
            if (p is null) return new ProcessResult(-1, "", $"cannot start {exe}");

            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(timeoutMs);

            return new ProcessResult(p.HasExited ? p.ExitCode : -1, stdout, stderr);
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", ex.Message);
        }
    }

    /// <summary>Runs a PowerShell snippet. Used for NIC and power cmdlets that have no clean WMI path.</summary>
    public static ProcessResult PowerShell(string script, int timeoutMs = 180_000)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        return Run("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", timeoutMs);
    }
}
