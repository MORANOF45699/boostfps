namespace BoostFPS.Core.Models;

/// <summary>Snapshot of what this machine is, probed once at startup. Every gate reads from here.</summary>
public sealed class MachineProfile
{
    public CpuVendor CpuVendor { get; init; }
    public string CpuName { get; init; } = "";
    public int PhysicalCores { get; init; }
    public int LogicalCores { get; init; }

    public GpuVendor GpuVendor { get; init; }
    public string GpuName { get; init; } = "";

    /// <summary>Registry index of the active display adapter under Control\Class\{4d36e968-...}, e.g. 0000.</summary>
    public string? GpuClassKeyIndex { get; init; }

    public ulong TotalMemoryBytes { get; init; }
    public ChassisKind Chassis { get; init; }
    public bool SystemDriveIsSsd { get; init; }
    public bool DomainJoined { get; init; }
    public bool IsVirtualMachine { get; init; }
    public int WindowsBuild { get; init; }
    public string WindowsCaption { get; init; } = "";

    public bool HasPhysicalPrinter { get; init; }
    public bool HasBluetooth { get; init; }
    public bool HasTouchOrPen { get; init; }
    public bool HasFingerprintReader { get; init; }
    public bool HasSmartCardReader { get; init; }
    public bool HasWifi { get; init; }
    public bool HasSshConfig { get; init; }

    /// <summary>Interface GUIDs under Tcpip\Parameters\Interfaces that map to a physical, connected NIC.</summary>
    public IReadOnlyList<string> ActiveNetInterfaceGuids { get; init; } = [];

    /// <summary>Subkey names present under Services\Class\USB (e.g. 0000 .. 0032).</summary>
    public IReadOnlyList<string> UsbClassKeys { get; init; } = [];

    public bool IsLaptop => Chassis == ChassisKind.Laptop;
}
