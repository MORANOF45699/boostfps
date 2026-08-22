using System.Text.RegularExpressions;

namespace BoostFPS.Core.Services;

public sealed record PowerPlan(Guid Guid, string Name, bool IsActive);

/// <summary>Power plan and boot-timer settings. Everything here is reversible through powercfg/bcdedit.</summary>
public sealed partial class PowerService
{
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    [GeneratedRegex(@"([0-9a-fA-F-]{36})\s+\((.+?)\)(\s*\*)?", RegexOptions.Compiled)]
    private static partial Regex PlanLine();

    public IReadOnlyList<PowerPlan> ListPlans()
    {
        var output = ProcessRunner.Run("powercfg.exe", "/list").Output;

        return PlanLine().Matches(output)
            .Select(m => new PowerPlan(
                Guid.Parse(m.Groups[1].Value),
                m.Groups[2].Value.Trim(),
                m.Groups[3].Success))
            .ToList();
    }

    public PowerPlan? ActivePlan() => ListPlans().FirstOrDefault(p => p.IsActive);

    /// <summary>
    /// Activates a plan, duplicating it first when Windows hides it (High Performance and
    /// Ultimate Performance are hidden on many modern builds until duplicated).
    /// </summary>
    public bool Activate(Guid plan)
    {
        if (ProcessRunner.Run("powercfg.exe", $"/setactive {plan}").Success) return true;

        ProcessRunner.Run("powercfg.exe", $"-duplicatescheme {plan}");
        return ProcessRunner.Run("powercfg.exe", $"/setactive {plan}").Success;
    }

    /// <summary>Imports a .pow file and activates it. Returns the new plan GUID when it can be parsed.</summary>
    public (bool Success, string Detail) ImportPowerPlan(string powFile)
    {
        if (!File.Exists(powFile)) return (false, $"ไม่พบไฟล์ {powFile}");

        var import = ProcessRunner.Run("powercfg.exe", $"-import \"{powFile}\"");
        if (!import.Success) return (false, import.Output.Trim());

        var guid = Regex.Match(import.Output, "[0-9a-fA-F-]{36}");
        if (!guid.Success) return (true, "import สำเร็จ แต่หา GUID ในผลลัพธ์ไม่เจอ จึงยังไม่ได้ตั้งเป็น active");

        var plan = Guid.Parse(guid.Value);
        return Activate(plan)
            ? (true, $"import และตั้ง active แล้ว ({plan})")
            : (true, $"import แล้ว แต่ตั้ง active ไม่สำเร็จ ({plan})");
    }

    /// <summary>Exports the active plan so the previous power configuration can be restored.</summary>
    public string? ExportActivePlan(string outDirectory)
    {
        var active = ActivePlan();
        if (active is null) return null;

        Directory.CreateDirectory(outDirectory);
        var file = Path.Combine(outDirectory, $"powerplan_{DateTime.Now:yyyyMMdd-HHmmss}.pow");

        return ProcessRunner.Run("powercfg.exe", $"-export \"{file}\" {active.Guid}").Success && File.Exists(file)
            ? file
            : null;
    }

    public bool IsDynamicTickDisabled() =>
        ProcessRunner.Run("bcdedit.exe", "/enum {current}").Output
            .Contains("disabledynamictick        Yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>bcdedit disabledynamictick. Takes effect only after a reboot.</summary>
    public bool SetDynamicTickDisabled(bool disabled) =>
        ProcessRunner.Run("bcdedit.exe", $"/set disabledynamictick {(disabled ? "yes" : "no")}").Success;
}
