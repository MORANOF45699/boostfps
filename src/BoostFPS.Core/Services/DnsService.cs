using System.Net;
using System.Net.NetworkInformation;

namespace BoostFPS.Core.Services;

public sealed record DnsAdapter(string Name, string InterfaceAlias, string[] CurrentDns, bool IsPhysical);

public sealed record DnsPreset(string Name, string[] Servers, string Description);

/// <summary>
/// Reads DNS state via System.Net.NetworkInformation and writes it back through netsh — the
/// PowerShell Set-DnsClientServerAddress path is cleaner but adds a full PS startup per call.
/// </summary>
public sealed class DnsService
{
    public static readonly DnsPreset[] Presets =
    [
        new("DHCP (คืนค่าอัตโนมัติ)", [], "ปล่อยให้ router แจก DNS"),
        new("Cloudflare 1.1.1.1", ["1.1.1.1", "1.0.0.1"], "เร็ว privacy ดี"),
        new("Cloudflare + APNIC", ["1.1.1.1", "9.9.9.9"], "เผื่อ Cloudflare ล่ม fallback ไป Quad9"),
        new("Google 8.8.8.8", ["8.8.8.8", "8.8.4.4"], "เสถียร แต่ Google เก็บ log"),
        new("Quad9 9.9.9.9", ["9.9.9.9", "149.112.112.112"], "บล็อกโดเมนอันตราย"),
        new("AdGuard", ["94.140.14.14", "94.140.15.15"], "บล็อกโฆษณาและ tracker")
    ];

    public IReadOnlyList<DnsAdapter> ListAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                     && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                     && n.OperationalStatus == OperationalStatus.Up)
            .Select(n => new DnsAdapter(
                Name: n.Description,
                InterfaceAlias: n.Name,
                CurrentDns: n.GetIPProperties().DnsAddresses
                    .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToArray(),
                IsPhysical: n.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                                                  or NetworkInterfaceType.GigabitEthernet
                                                  or NetworkInterfaceType.Wireless80211))
            .ToList();

    public bool ApplyPreset(string interfaceAlias, DnsPreset preset)
    {
        if (preset.Servers.Length == 0) return ResetToDhcp(interfaceAlias);

        var primary = ProcessRunner.Run("netsh.exe",
            $"interface ip set dnsservers name=\"{interfaceAlias}\" static {preset.Servers[0]} primary");

        if (!primary.Success) return false;

        for (var i = 1; i < preset.Servers.Length; i++)
        {
            ProcessRunner.Run("netsh.exe",
                $"interface ip add dnsservers name=\"{interfaceAlias}\" {preset.Servers[i]} index={i + 1}");
        }

        FlushCache();
        return true;
    }

    public bool ResetToDhcp(string interfaceAlias)
    {
        var ok = ProcessRunner.Run("netsh.exe",
            $"interface ip set dnsservers name=\"{interfaceAlias}\" source=dhcp").Success;
        FlushCache();
        return ok;
    }

    public void FlushCache() => ProcessRunner.Run("ipconfig.exe", "/flushdns");

    /// <summary>
    /// Ranks presets by average ICMP RTT to their primary server. 3 pings per server, drop first.
    /// Returns records in ascending latency order; failed hosts sort to the bottom with -1.
    /// </summary>
    public async Task<IReadOnlyList<DnsPingResult>> RankPresetsAsync(CancellationToken ct = default)
    {
        var tasks = Presets
            .Where(p => p.Servers.Length > 0)
            .Select(async p =>
            {
                var latency = await AveragePingAsync(p.Servers[0], ct).ConfigureAwait(false);
                return new DnsPingResult(p, latency);
            });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return results
            .OrderBy(r => r.LatencyMs < 0 ? int.MaxValue : r.LatencyMs)
            .ToList();
    }

    private static async Task<int> AveragePingAsync(string host, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            long sum = 0;
            var count = 0;

            for (var i = 0; i < 4; i++)
            {
                if (ct.IsCancellationRequested) break;

                var reply = await ping.SendPingAsync(IPAddress.Parse(host), 1500).ConfigureAwait(false);
                if (reply.Status != IPStatus.Success) continue;

                if (i == 0) continue; // warmup
                sum += reply.RoundtripTime;
                count++;
            }

            return count == 0 ? -1 : (int)(sum / count);
        }
        catch { return -1; }
    }
}

public sealed record DnsPingResult(DnsPreset Preset, int LatencyMs)
{
    public string LatencyText => LatencyMs < 0 ? "-" : $"{LatencyMs} ms";
}
