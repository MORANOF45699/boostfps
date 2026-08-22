namespace BoostFPS.Core.Services;

public sealed class NetworkStepResult
{
    public required string Step { get; init; }
    public bool Success { get; init; }
    public string Detail { get; init; } = "";
}

/// <summary>
/// The NIC-side tuning ported from the proven portable script. Deliberately does NOT touch
/// Interrupt Moderation, RSC, Flow Control or IRQ affinity: those caused desync on 2.5G Realtek
/// adapters, so they stay out even though other guides recommend them.
/// </summary>
public sealed class NetworkService
{
    private static readonly string[] PowerSavingProperties =
    [
        "Energy-Efficient Ethernet", "Energy Efficient Ethernet", "EEE",
        "Advanced EEE", "Green Ethernet", "Power Saving Mode",
        "Gigabit Lite", "Ultra Low Power Mode", "Selective Suspend"
    ];

    /// <summary>Turns off NIC power saving on every physical adapter that is up, then restarts it.</summary>
    public NetworkStepResult DisableNicPowerSaving()
    {
        var props = string.Join(", ", PowerSavingProperties.Select(p => $"'{p}'"));

        var script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $props = @({{props}})
            $changed = @()
            Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' } | ForEach-Object {
                $name = $_.Name
                Disable-NetAdapterPowerManagement -Name $name -NoRestart
                foreach ($p in $props) {
                    try {
                        Set-NetAdapterAdvancedProperty -Name $name -DisplayName $p -DisplayValue 'Disabled' -NoRestart -ErrorAction Stop
                        $changed += "$name : $p"
                    } catch { }
                }
                Restart-NetAdapter -Name $name
            }
            $changed -join "`n"
            """;

        var result = ProcessRunner.PowerShell(script);
        return new NetworkStepResult
        {
            Step = "ปิด NIC power saving",
            Success = result.Success,
            Detail = string.IsNullOrWhiteSpace(result.StdOut) ? "ไม่มี property ที่รองรับบนการ์ดนี้" : result.StdOut.Trim()
        };
    }

    /// <summary>Global TCP settings. Autotuning stays normal on purpose - restricting it hurts throughput.</summary>
    public NetworkStepResult ApplyTcpGlobals()
    {
        var autotuning = ProcessRunner.Run("netsh.exe", "interface tcp set global autotuninglevel=normal");
        var rss = ProcessRunner.Run("netsh.exe", "interface tcp set global rss=enabled");

        return new NetworkStepResult
        {
            Step = "netsh TCP globals",
            Success = autotuning.Success && rss.Success,
            Detail = "autotuninglevel=normal, rss=enabled"
        };
    }

    public string ReadTcpGlobals() => ProcessRunner.Run("netsh.exe", "interface tcp show global").Output;
}
