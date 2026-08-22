namespace BoostFPS.Core.Models;

public sealed class NvidiaSetting
{
    public string SettingNameInfo { get; init; } = "";
    public uint SettingID { get; init; }
    public string SettingValue { get; init; } = "";
    public string ValueType { get; init; } = "Dword";
}

/// <summary>One profile parsed out of a .nip file (NVIDIA Profile Inspector export).</summary>
public sealed class NvidiaProfile
{
    public string ProfileName { get; init; } = "";
    public List<string> Executables { get; init; } = [];
    public List<NvidiaSetting> Settings { get; init; } = [];
}
