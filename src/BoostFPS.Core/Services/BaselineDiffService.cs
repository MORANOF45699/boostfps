using BoostFPS.Core.Models;
using Microsoft.Win32;

namespace BoostFPS.Core.Services;

public enum DiffState
{
    /// <summary>Current value matches the tweak's on-value.</summary>
    Tuned,
    /// <summary>Current value matches the Windows default.</summary>
    Default,
    /// <summary>The value is unset on this machine but a Windows default is documented.</summary>
    Missing,
    /// <summary>Current value is neither the on-value nor the documented default.</summary>
    Other
}

public sealed class DiffRow
{
    public required string TweakId { get; init; }
    public required string TweakName { get; init; }
    public required string Category { get; init; }
    public required string RegPath { get; init; }
    public required string ValueName { get; init; }
    public required string CurrentDisplay { get; init; }
    public required string OnDisplay { get; init; }
    public required string DefaultDisplay { get; init; }
    public required DiffState State { get; init; }

    public bool DiffersFromDefault => State != DiffState.Default;
}

public sealed record BaselineSummary(int Tuned, int Default, int Missing, int Other, int NoDefault);

/// <summary>
/// Compares every value written by every applicable tweak against Windows stock defaults
/// so the user can see, without a fresh install, exactly what has been changed.
/// </summary>
public sealed class BaselineDiffService(RegistryTweakService tweaks)
{
    private readonly RegistryTweakService _tweaks = tweaks;

    public IReadOnlyList<DiffRow> BuildDiff()
    {
        var rows = new List<DiffRow>();

        foreach (var tweak in Catalog.Tweaks.Where(_tweaks.IsApplicable))
        {
            var paths = _tweaks.ResolvePaths(tweak);
            if (paths.Count == 0) continue;
            var probePath = paths[0];

            using var key = RegistryPath.OpenRead(probePath);

            foreach (var v in tweak.Values)
            {
                var current = key?.GetValue(v.ValueName);
                var onNorm = RegistryTweakService.Normalize(v.OnValue, v.Kind);
                var defNorm = v.DefaultValue is null
                    ? null
                    : RegistryTweakService.Normalize(v.DefaultValue, v.Kind);

                var state = ClassifyValue(current, onNorm, defNorm);

                rows.Add(new DiffRow
                {
                    TweakId = tweak.Id,
                    TweakName = tweak.Name,
                    Category = tweak.Category,
                    RegPath = probePath,
                    ValueName = v.ValueName,
                    CurrentDisplay = Describe(current),
                    OnDisplay = Describe(onNorm),
                    DefaultDisplay = defNorm is null ? "(ไม่ทราบ default)" : Describe(defNorm),
                    State = state
                });
            }
        }

        return rows
            .OrderByDescending(r => r.DiffersFromDefault)
            .ThenBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.TweakName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static BaselineSummary Summarize(IReadOnlyList<DiffRow> rows) => new(
        Tuned: rows.Count(r => r.State == DiffState.Tuned),
        Default: rows.Count(r => r.State == DiffState.Default),
        Missing: rows.Count(r => r.State == DiffState.Missing),
        Other: rows.Count(r => r.State == DiffState.Other),
        NoDefault: rows.Count(r => r.DefaultDisplay.StartsWith("(ไม่ทราบ")));

    private static DiffState ClassifyValue(object? current, object onValue, object? defaultValue)
    {
        if (current is null)
            return defaultValue is null ? DiffState.Default : DiffState.Missing;

        if (Equal(current, onValue)) return DiffState.Tuned;
        if (defaultValue is not null && Equal(current, defaultValue)) return DiffState.Default;
        return DiffState.Other;
    }

    private static bool Equal(object a, object b)
    {
        if (a is byte[] ba && b is byte[] bb) return ba.AsSpan().SequenceEqual(bb);
        if (a is string[] sa && b is string[] sb) return sa.SequenceEqual(sb);
        if (a is string || b is string) return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
        try { return Convert.ToInt64(a) == Convert.ToInt64(b); } catch { return Equals(a, b); }
    }

    private static string Describe(object? value) => value switch
    {
        null => "(ไม่มีค่า)",
        byte[] bytes => bytes.Length <= 16 ? Convert.ToHexString(bytes) : Convert.ToHexString(bytes)[..30] + "...",
        string[] many => string.Join(", ", many),
        int i => $"0x{i:X} ({i})",
        long l => $"0x{l:X} ({l})",
        _ => value.ToString() ?? ""
    };
}
