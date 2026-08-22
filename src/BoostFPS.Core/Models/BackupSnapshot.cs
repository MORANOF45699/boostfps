namespace BoostFPS.Core.Models;

/// <summary>One captured registry value. Existed=false means revert deletes the value.</summary>
public sealed class RegistryValueSnapshot
{
    public required string RegPath { get; init; }
    public required string ValueName { get; init; }
    public bool Existed { get; init; }
    public string? Kind { get; init; }
    public object? Value { get; init; }
}

public sealed class ServiceSnapshot
{
    public required string Name { get; init; }
    public bool Existed { get; init; }
    public ServiceStart Start { get; init; }
}

/// <summary>Everything captured immediately before one Apply run, written to disk before any write happens.</summary>
public sealed class BackupSnapshot
{
    public required string Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public string MachineName { get; init; } = Environment.MachineName;
    public string Description { get; init; } = "";

    /// <summary>Null when System Protection was off or the user skipped it.</summary>
    public long? RestorePointSequence { get; init; }

    public List<RegistryValueSnapshot> RegistryValues { get; init; } = [];
    public List<ServiceSnapshot> Services { get; init; } = [];
    public List<string> ExportedRegFiles { get; init; } = [];

    /// <summary>Tweak ids applied in this run, so the UI can show what a snapshot corresponds to.</summary>
    public List<string> AppliedTweakIds { get; init; } = [];
}
