namespace BoostFPS.Core.Models;

/// <summary>One Windows service the app may switch to Disabled.</summary>
public sealed class ServiceDefinition
{
    public required string Name { get; init; }
    public string DisplayName { get; init; } = "";

    /// <summary>Plain-language consequence of disabling it, shown in the UI.</summary>
    public string Impact { get; init; } = "";

    public TweakTier Tier { get; init; } = TweakTier.Balanced;
    public ServiceStart TargetStart { get; init; } = ServiceStart.Disabled;

    /// <summary>Windows stock start type, fallback only when no snapshot exists.</summary>
    public ServiceStart? DefaultStart { get; init; }

    /// <summary>Named hardware/role probes that must all be false for this service to be offered.</summary>
    public string[] SkipWhen { get; init; } = [];

    /// <summary>Named probes that only produce a warning badge instead of hiding the entry.</summary>
    public string[] WarnWhen { get; init; } = [];
}
