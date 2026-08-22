namespace BoostFPS.Core.Models;

public enum RiskLevel
{
    Safe,
    Moderate,
    Aggressive
}

public enum TweakTier
{
    Safe,
    Balanced,
    Extreme
}

/// <summary>Windows service start type, as stored in the "Start" registry value.</summary>
public enum ServiceStart
{
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4
}

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel
}

public enum CpuVendor
{
    Unknown,
    Intel,
    Amd
}

public enum ChassisKind
{
    Unknown,
    Desktop,
    Laptop,
    VirtualMachine
}

/// <summary>Reason a tweak or service entry is not offered on the current machine.</summary>
public enum GateResult
{
    Allowed,
    NotPresent,
    HardwareMismatch,
    Blocked,
    Warned
}
