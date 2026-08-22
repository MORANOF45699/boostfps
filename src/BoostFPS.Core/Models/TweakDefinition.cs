using Microsoft.Win32;

namespace BoostFPS.Core.Models;

/// <summary>One registry value written by a tweak.</summary>
public sealed class TweakValue
{
    public required string ValueName { get; init; }
    public RegistryValueKind Kind { get; init; } = RegistryValueKind.DWord;

    /// <summary>Value written when the tweak is on. Numbers arrive from JSON as numbers, hex blobs as hex strings.</summary>
    public required object OnValue { get; init; }

    /// <summary>Windows stock value, used only when no per-machine snapshot exists. Null = delete on revert.</summary>
    public object? DefaultValue { get; init; }
}

/// <summary>
/// One toggleable tweak: a key plus every value it writes. Paths may contain expansion
/// tokens that <c>RegistryTweakService</c> resolves against the live machine:
///   {GPU_CLASS_KEY}   -> Control\Class\{4d36e968-...}\NNNN of the active display adapter
///   {NET_INTERFACES}  -> each Tcpip\Parameters\Interfaces\{GUID} that is up
///   {USB_CLASS_KEYS}  -> each Services\Class\USB\NNNN present on the machine
/// A path containing a multi-target token is applied to every match.
/// </summary>
public sealed class TweakDefinition
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";

    /// <summary>Full path incl. hive, e.g. HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl.</summary>
    public required string RegPath { get; init; }

    public required List<TweakValue> Values { get; init; }

    public RiskLevel Risk { get; init; } = RiskLevel.Moderate;
    public bool RequiresReboot { get; init; }

    /// <summary>Tiers this tweak belongs to.</summary>
    public TweakTier[] Tiers { get; init; } = [TweakTier.Balanced];

    public MachineRequirement Requires { get; init; } = new();
}

/// <summary>Hardware gate. Every populated field must match the current machine or the item is hidden.</summary>
public sealed class MachineRequirement
{
    public GpuVendor? Gpu { get; init; }
    public CpuVendor? Cpu { get; init; }

    /// <summary>false = desktop only (hidden on laptops), true = laptop only.</summary>
    public bool? Laptop { get; init; }

    /// <summary>true = requires the system drive to be solid state.</summary>
    public bool? SystemDriveIsSsd { get; init; }

    /// <summary>true = only inside a VM guest, false = only on bare metal.</summary>
    public bool? VirtualMachine { get; init; }

    /// <summary>true = only when the machine is domain joined, false = only when it is not.</summary>
    public bool? DomainJoined { get; init; }

    /// <summary>Minimum Windows build number.</summary>
    public int? MinBuild { get; init; }
}
