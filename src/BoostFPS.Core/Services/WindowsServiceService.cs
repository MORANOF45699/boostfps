using System.ServiceProcess;
using BoostFPS.Core.Models;
using Microsoft.Win32;

namespace BoostFPS.Core.Services;

public sealed record ServiceGate(GateResult Result, string Reason);

public sealed class ServiceEntry
{
    public required ServiceDefinition Definition { get; init; }
    public required ServiceGate Gate { get; init; }
    public ServiceStart? CurrentStart { get; init; }
    public string? RunningState { get; init; }

    /// <summary>Other installed, non-disabled services that list this one in DependOnService.</summary>
    public IReadOnlyList<string> Dependents { get; init; } = [];

    public bool IsDisabled => CurrentStart == ServiceStart.Disabled;
    public bool CanToggle => Gate.Result is GateResult.Allowed or GateResult.Warned;
}

/// <summary>
/// Reads and writes service start types through the registry (works for services that
/// ServiceController refuses to open), and gates every candidate against the real machine.
/// </summary>
public sealed class WindowsServiceService(MachineProfile machine)
{
    private const string ServicesRoot = @"HKLM\SYSTEM\CurrentControlSet\Services";

    /// <summary>
    /// Never offered in the UI at any tier. Disabling any of these breaks boot, login,
    /// networking, or security in ways users cannot recover from inside Windows.
    /// </summary>
    public static readonly IReadOnlySet<string> HardBlocklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "DcomLaunch", "RpcSs", "RpcEptMapper", "BrokerInfrastructure", "SystemEventsBroker",
        "CoreMessagingRegistrar", "LSM", "PlugPlay", "Power", "ProfSvc", "UserManager",
        "Schedule", "EventLog", "EventSystem", "Winmgmt", "gpsvc", "CryptSvc", "SamSs",
        "Dhcp", "Dnscache", "nsi", "NlaSvc", "netprofm", "LanmanWorkstation", "WlanSvc",
        "BFE", "mpssvc", "WinDefend", "WdNisSvc", "SecurityHealthService", "wscsvc", "Sense",
        "AudioSrv", "Audiosrv", "AudioEndpointBuilder", "TrustedInstaller", "msiserver",
        "DeviceInstall", "DsmSvc", "DispBrokerDesktopSvc", "StateRepository", "TokenBroker"
    };

    private readonly MachineProfile _machine = machine;

    public static ServiceStart? ReadStart(string name)
    {
        using var key = RegistryPath.OpenRead($@"{ServicesRoot}\{name}");
        var value = key?.GetValue("Start");
        return value is null ? null : (ServiceStart)Convert.ToInt32(value);
    }

    public static void WriteStart(string name, ServiceStart start)
    {
        using var key = RegistryPath.OpenWrite($@"{ServicesRoot}\{name}");
        key.SetValue("Start", (int)start, RegistryValueKind.DWord);
    }

    public static bool Exists(string name) => RegistryPath.KeyExists($@"{ServicesRoot}\{name}");

    /// <summary>Stops the service now so the change takes effect without a reboot. Best effort.</summary>
    public static bool TryStop(string name, TimeSpan timeout)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) return true;
            if (!sc.CanStop) return false;

            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Builds the UI list: definition + gate verdict + live state + dependents.</summary>
    public IReadOnlyList<ServiceEntry> Build(IEnumerable<ServiceDefinition> definitions)
    {
        var dependencyMap = BuildDependencyMap();
        var live = SafeGetServices();

        return definitions.Select(def =>
        {
            var gate = Evaluate(def);
            var start = ReadStart(def.Name);
            var state = live.TryGetValue(def.Name, out var s) ? s : null;

            return new ServiceEntry
            {
                Definition = def,
                Gate = gate,
                CurrentStart = start,
                RunningState = state,
                Dependents = dependencyMap.TryGetValue(def.Name, out var d) ? d : []
            };
        })
        .Where(e => e.Gate.Result != GateResult.NotPresent)
        .OrderBy(e => e.Definition.Tier)
        .ThenBy(e => e.Definition.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    /// <summary>Decides whether this machine may disable the service, and why not when it may not.</summary>
    public ServiceGate Evaluate(ServiceDefinition def)
    {
        if (HardBlocklist.Contains(def.Name))
            return new ServiceGate(GateResult.Blocked, "Critical service - never disabled by BoostFPS");

        if (!Exists(def.Name))
            return new ServiceGate(GateResult.NotPresent, "Not installed on this machine");

        foreach (var probe in def.SkipWhen)
        {
            if (ProbeIsTrue(probe, out var reason))
                return new ServiceGate(GateResult.HardwareMismatch, reason);
        }

        foreach (var probe in def.WarnWhen)
        {
            if (ProbeIsTrue(probe, out var reason))
                return new ServiceGate(GateResult.Warned, reason);
        }

        return new ServiceGate(GateResult.Allowed, "");
    }

    /// <summary>
    /// Named hardware/role probes referenced by services.json. Returning true means
    /// "this machine actually uses that service", so it must not be disabled.
    /// </summary>
    private bool ProbeIsTrue(string probe, out string reason)
    {
        (bool hit, string why) = probe switch
        {
            "HasPrinter" => (_machine.HasPhysicalPrinter, "A physical printer is installed"),
            "HasBluetooth" => (_machine.HasBluetooth, "This machine has Bluetooth"),
            "IsLaptop" => (_machine.IsLaptop, "Laptop - location and sensor services are in use"),
            "HasTouchOrPen" => (_machine.HasTouchOrPen, "Touchscreen or pen digitizer present"),
            "HasFingerprint" => (_machine.HasFingerprintReader, "Fingerprint reader present"),
            "HasSmartCard" => (_machine.HasSmartCardReader, "Smart card reader present"),
            "IsVirtualMachine" => (_machine.IsVirtualMachine, "Running inside a VM - guest services required"),
            "DomainJoined" => (_machine.DomainJoined, "Machine is domain joined"),
            "HasWifi" => (_machine.HasWifi, "Wi-Fi adapter present"),
            "SystemDriveIsHdd" => (!_machine.SystemDriveIsSsd, "System drive is a hard disk - SysMain still helps"),
            "HasSshConfig" => (_machine.HasSshConfig, "An .ssh folder exists for this user"),
            _ => (false, "")
        };

        reason = why;
        return hit;
    }

    /// <summary>service name -> installed, not-disabled services that depend on it.</summary>
    private static Dictionary<string, List<string>> BuildDependencyMap()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in RegistryPath.SubKeyNames(ServicesRoot))
        {
            using var key = RegistryPath.OpenRead($@"{ServicesRoot}\{name}");
            if (key is null) continue;

            if (key.GetValue("Start") is { } start && Convert.ToInt32(start) == (int)ServiceStart.Disabled)
                continue;

            if (key.GetValue("DependOnService") is not string[] deps) continue;

            foreach (var dep in deps.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                if (!map.TryGetValue(dep, out var list)) map[dep] = list = [];
                list.Add(name);
            }
        }

        return map;
    }

    private static Dictionary<string, string> SafeGetServices()
    {
        try
        {
            return ServiceController.GetServices()
                .GroupBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Status.ToString(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
