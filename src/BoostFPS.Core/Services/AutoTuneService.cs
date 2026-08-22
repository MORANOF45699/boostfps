using BoostFPS.Core.Models;

namespace BoostFPS.Core.Services;

public sealed record AutoTunePlan(
    TweakTier Tier,
    string Reason,
    IReadOnlyList<TweakDefinition> Tweaks,
    IReadOnlyList<ServiceDefinition> Services);

/// <summary>
/// One-click tuner. Reads the machine profile, decides how far to push, and returns the
/// filtered lists ready for OptimizationEngine.Apply. Never writes on its own.
/// </summary>
public sealed class AutoTuneService(MachineProfile machine, WindowsServiceService services)
{
    private readonly MachineProfile _machine = machine;
    private readonly WindowsServiceService _services = services;

    public AutoTunePlan Recommend()
    {
        var (tier, reason) = PickTier();

        var tweaks = Catalog.Tweaks
            .Where(t => t.Tiers.Contains(tier))
            .Where(AppliesTo)
            .ToList();

        // Services up to and including the chosen tier, gated by hardware.
        var serviceEntries = _services.Build(Catalog.Services)
            .Where(e => e.Definition.Tier <= tier
                     && e.Gate.Result is GateResult.Allowed or GateResult.Warned
                     && !e.IsDisabled)
            .Select(e => e.Definition)
            .ToList();

        return new AutoTunePlan(tier, reason, tweaks, serviceEntries);
    }

    /// <summary>Desktop + SSD + gaming GPU = go hard. Laptop = play it safe. Everything else = middle.</summary>
    private (TweakTier, string) PickTier()
    {
        if (_machine.IsVirtualMachine)
            return (TweakTier.Safe,
                "รันใน VM — Safe เท่านั้น เพราะปิด timer ในเกสต์ไม่มีผลหรือทำ host เพี้ยน");

        if (_machine.IsLaptop)
            return (TweakTier.Safe,
                "โน้ตบุ๊ก — Safe กัน tweak ที่กินแบตหรือทำเครื่องร้อน");

        if (_machine.DomainJoined)
            return (TweakTier.Balanced,
                "Domain machine — Balanced กันชนกับ policy ขององค์กร");

        var gamingRig = _machine.SystemDriveIsSsd
                        && _machine.TotalMemoryBytes >= 12L * 1024 * 1024 * 1024
                        && _machine.GpuVendor is GpuVendor.Nvidia or GpuVendor.Amd
                        && _machine.LogicalCores >= 6;

        return gamingRig
            ? (TweakTier.Extreme, "Desktop + SSD + GPU เกม + RAM >= 12GB — Extreme ได้เต็ม")
            : (TweakTier.Balanced, "Desktop — Balanced เหมาะกับสเปคเครื่อง");
    }

    private bool AppliesTo(TweakDefinition t)
    {
        var r = t.Requires;
        if (r.Gpu is { } gpu && _machine.GpuVendor != gpu) return false;
        if (r.Cpu is { } cpu && _machine.CpuVendor != cpu) return false;
        if (r.Laptop is { } laptop && _machine.IsLaptop != laptop) return false;
        if (r.SystemDriveIsSsd is { } ssd && _machine.SystemDriveIsSsd != ssd) return false;
        if (r.VirtualMachine is { } vm && _machine.IsVirtualMachine != vm) return false;
        if (r.DomainJoined is { } dj && _machine.DomainJoined != dj) return false;
        if (r.MinBuild is { } min && _machine.WindowsBuild < min) return false;
        return true;
    }
}
