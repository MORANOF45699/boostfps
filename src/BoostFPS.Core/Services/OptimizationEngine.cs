using BoostFPS.Core.Models;

namespace BoostFPS.Core.Services;

public sealed class ApplyRequest
{
    public required IReadOnlyList<TweakDefinition> Tweaks { get; init; }
    public IReadOnlyList<ServiceDefinition> Services { get; init; } = [];

    /// <summary>Create a System Restore checkpoint before touching anything.</summary>
    public bool CreateRestorePoint { get; init; } = true;

    /// <summary>Stop each disabled service immediately instead of waiting for a reboot.</summary>
    public bool StopServicesNow { get; init; } = true;

    public string Description { get; init; } = "Apply";
}

public sealed class ApplyResult
{
    public string? BackupDirectory { get; set; }
    public string? BackupId { get; set; }
    public int TweaksApplied { get; set; }
    public int ServicesChanged { get; set; }
    public bool RestorePointCreated { get; set; }
    public string? RestorePointMessage { get; set; }
    public bool RebootRequired { get; set; }
    public List<string> Failures { get; } = [];
    public bool Success => Failures.Count == 0;
}

/// <summary>
/// The single path through which changes reach the machine: capture, persist, then write.
/// Nothing else in the app is allowed to write tweaks or service start types directly.
/// </summary>
public sealed class OptimizationEngine(
    MachineProfile machine,
    RegistryTweakService tweaks,
    WindowsServiceService services,
    BackupService backups,
    RestorePointService restorePoints,
    ChangelogService changelog)
{
    private readonly MachineProfile _machine = machine;

    public MachineProfile Machine => _machine;
    public NvidiaProfileService Nvidia { get; } = new(machine);
    public NetworkService Network { get; } = new();
    public PowerService Power { get; } = new();
    public MemoryCleanerService Memory { get; } = new();
    public DnsService Dns { get; } = new();
    public GameBoostService Games { get; } = new();
    public AutoTuneService AutoTune { get; } = new(machine, services);
    public BaselineDiffService BaselineDiff { get; } = new(tweaks);
    public RegistryTweakService Tweaks => tweaks;
    public WindowsServiceService Services => services;
    public BackupService Backups => backups;
    public RestorePointService RestorePoints => restorePoints;
    public ChangelogService Changelog => changelog;

    public ApplyResult Apply(ApplyRequest request)
    {
        var result = new ApplyResult();

        var applicable = request.Tweaks.Where(tweaks.IsApplicable).ToList();
        var allowedServices = request.Services
            .Where(s => services.Evaluate(s).Result is GateResult.Allowed or GateResult.Warned)
            .ToList();

        if (applicable.Count == 0 && allowedServices.Count == 0)
        {
            result.Failures.Add("Nothing to apply: every selected item was gated out on this machine.");
            return result;
        }

        // 1. restore point first - it is the only recovery path for anything we fail to capture
        if (request.CreateRestorePoint)
        {
            var rp = restorePoints.Create($"BoostFPS - {request.Description}");
            result.RestorePointCreated = rp.Created;
            result.RestorePointMessage = rp.Message;
        }

        // 2. capture and persist the prior state BEFORE the first write
        var targets = applicable.SelectMany(tweaks.Resolve).ToList();
        var snapshot = backups.Capture(
            request.Description,
            targets,
            allowedServices.Select(s => s.Name),
            applicable.Select(t => t.Id));

        result.BackupDirectory = backups.Persist(snapshot);
        result.BackupId = snapshot.Id;

        // 3. write
        foreach (var tweak in applicable)
        {
            try
            {
                tweaks.Apply(tweak);
                result.TweaksApplied++;
                if (tweak.RequiresReboot) result.RebootRequired = true;
                changelog.Add("Applied", $"{tweak.Name} [{tweak.Id}]");
            }
            catch (Exception ex)
            {
                result.Failures.Add($"{tweak.Id}: {ex.Message}");
                changelog.Add("Failed", $"{tweak.Id}: {ex.Message}");
            }
        }

        foreach (var svc in allowedServices)
        {
            try
            {
                WindowsServiceService.WriteStart(svc.Name, svc.TargetStart);
                result.ServicesChanged++;

                if (request.StopServicesNow && svc.TargetStart == ServiceStart.Disabled)
                {
                    if (!WindowsServiceService.TryStop(svc.Name, TimeSpan.FromSeconds(10)))
                        result.RebootRequired = true;
                }

                changelog.Add("Applied", $"Service {svc.Name} -> {svc.TargetStart}");
            }
            catch (Exception ex)
            {
                result.Failures.Add($"service {svc.Name}: {ex.Message}");
                changelog.Add("Failed", $"service {svc.Name}: {ex.Message}");
            }
        }

        changelog.Add("Applied",
            $"Run '{request.Description}': {result.TweaksApplied} tweaks, {result.ServicesChanged} services, backup {snapshot.Id}");

        return result;
    }

    /// <summary>Rolls one snapshot back. Prefer this over RevertToDefault whenever a snapshot exists.</summary>
    public RestoreReport Revert(BackupSnapshot snapshot)
    {
        var report = backups.Restore(snapshot);

        changelog.Add(report.Success ? "Reverted" : "Reverted with errors",
            $"Snapshot {snapshot.Id}: {report.RegistryRestored} values, {report.ServicesRestored} services" +
            (report.Failures.Count > 0 ? $", {report.Failures.Count} failed" : ""));

        return report;
    }

    /// <summary>Tweaks from the catalog that this machine can actually use.</summary>
    public IReadOnlyList<TweakDefinition> AvailableTweaks() =>
        Catalog.Tweaks.Where(tweaks.IsApplicable).ToList();

    public IReadOnlyList<TweakDefinition> TweaksForTier(TweakTier tier) =>
        AvailableTweaks().Where(t => t.Tiers.Contains(tier)).ToList();

    public IReadOnlyList<ServiceEntry> AvailableServices() => services.Build(Catalog.Services);

    /// <summary>Builds the whole graph from a freshly probed machine profile.</summary>
    public static OptimizationEngine Create()
    {
        AppPaths.EnsureCreated();
        var profile = new MachineProfiler().Probe();

        return new OptimizationEngine(
            profile,
            new RegistryTweakService(profile),
            new WindowsServiceService(profile),
            new BackupService(),
            new RestorePointService(),
            new ChangelogService());
    }
}
