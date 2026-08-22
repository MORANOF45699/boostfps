using BoostFPS.Core.Models;
using BoostFPS.Core.Services;

// Headless diagnostic for the Core layer: prints the machine profile, the catalog after
// gating, and the live status of every tweak. Read-only - it never writes to the registry.

var profile = new MachineProfiler().Probe();
var tweaks = new RegistryTweakService(profile);
var services = new WindowsServiceService(profile);

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("=== MACHINE ===");
Console.WriteLine($"CPU        : {profile.CpuName} ({profile.CpuVendor}, {profile.PhysicalCores}C/{profile.LogicalCores}T)");
Console.WriteLine($"GPU        : {profile.GpuName} ({profile.GpuVendor}, class key {profile.GpuClassKeyIndex ?? "-"})");
Console.WriteLine($"RAM        : {profile.TotalMemoryBytes / 1024.0 / 1024 / 1024:F1} GB");
Console.WriteLine($"OS         : {profile.WindowsCaption} build {profile.WindowsBuild}");
Console.WriteLine($"Chassis    : {profile.Chassis} | SSD system drive: {profile.SystemDriveIsSsd} | VM: {profile.IsVirtualMachine} | Domain: {profile.DomainJoined}");
Console.WriteLine($"Devices    : printer={profile.HasPhysicalPrinter} bt={profile.HasBluetooth} touch={profile.HasTouchOrPen} fp={profile.HasFingerprintReader} smartcard={profile.HasSmartCardReader} wifi={profile.HasWifi} ssh={profile.HasSshConfig}");
Console.WriteLine($"NICs       : {profile.ActiveNetInterfaceGuids.Count} | USB class keys: {profile.UsbClassKeys.Count}");

Console.WriteLine();
Console.WriteLine("=== TWEAKS ===");
foreach (var group in Catalog.Tweaks.GroupBy(t => t.Category))
{
    Console.WriteLine($"\n[{group.Key}]");
    foreach (var t in group)
    {
        var status = tweaks.GetStatus(t);
        var targets = tweaks.Resolve(t).Count;
        var tiers = t.Tiers.Length == 0 ? "opt-in" : string.Join("/", t.Tiers);
        Console.WriteLine($"  {status,-14} {t.Id,-38} {targets,3} targets  {t.Risk,-10} {tiers}");
    }
}

Console.WriteLine();
Console.WriteLine("=== SERVICES ===");
foreach (var entry in services.Build(Catalog.Services))
{
    var gate = entry.Gate.Result == GateResult.Allowed ? "" : $"  <- {entry.Gate.Result}: {entry.Gate.Reason}";
    var dependents = entry.Dependents.Count > 0 ? $"  deps:{string.Join(",", entry.Dependents)}" : "";
    Console.WriteLine($"  {entry.Definition.Tier,-9} {entry.Definition.Name,-26} start={entry.CurrentStart,-10} {entry.RunningState,-8}{gate}{dependents}");
}

var blocked = Catalog.Services.Where(s => WindowsServiceService.HardBlocklist.Contains(s.Name)).ToList();
if (blocked.Count > 0)
    Console.WriteLine($"\n!! catalog contains hard-blocklisted services: {string.Join(", ", blocked.Select(b => b.Name))}");

Console.WriteLine();
Console.WriteLine($"Tweaks in catalog: {Catalog.Tweaks.Count}, applicable here: {Catalog.Tweaks.Count(tweaks.IsApplicable)}");
Console.WriteLine($"Services in catalog: {Catalog.Services.Count}");
