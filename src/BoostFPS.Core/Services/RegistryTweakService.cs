using System.Text.Json;
using BoostFPS.Core.Models;
using Microsoft.Win32;

namespace BoostFPS.Core.Services;

/// <summary>A tweak value resolved to one concrete registry location on this machine.</summary>
public sealed record ResolvedTarget(string RegPath, TweakValue Value)
{
    public string ValueName => Value.ValueName;
}

public enum TweakStatus
{
    /// <summary>Every resolved target already holds the on value.</summary>
    On,
    /// <summary>No resolved target holds the on value.</summary>
    Off,
    /// <summary>Some targets match, some do not (e.g. only half the USB ports).</summary>
    Partial,
    /// <summary>Gated out by hardware, so it is neither on nor applicable.</summary>
    NotApplicable
}

/// <summary>
/// Reads and writes the registry tweaks. Every path token is expanded against the live
/// MachineProfile, so nothing from the authoring machine is baked in.
/// </summary>
public sealed class RegistryTweakService(MachineProfile machine)
{
    public const string TokenGpuClassKey = "{GPU_CLASS_KEY}";
    public const string TokenNetInterfaces = "{NET_INTERFACES}";
    public const string TokenUsbClassKeys = "{USB_CLASS_KEYS}";

    private const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";

    private readonly MachineProfile _machine = machine;

    /// <summary>False when the machine does not satisfy the tweak's hardware requirements.</summary>
    public bool IsApplicable(TweakDefinition t)
    {
        var r = t.Requires;
        if (r.Gpu is { } gpu && _machine.GpuVendor != gpu) return false;
        if (r.Cpu is { } cpu && _machine.CpuVendor != cpu) return false;
        if (r.Laptop is { } laptop && _machine.IsLaptop != laptop) return false;
        if (r.SystemDriveIsSsd is { } ssd && _machine.SystemDriveIsSsd != ssd) return false;
        if (r.VirtualMachine is { } vm && _machine.IsVirtualMachine != vm) return false;
        if (r.DomainJoined is { } dj && _machine.DomainJoined != dj) return false;
        if (r.MinBuild is { } min && _machine.WindowsBuild < min) return false;

        // A tweak whose targets are GPU class keys is pointless without a resolved index.
        if (t.RegPath.Contains(TokenGpuClassKey) && _machine.GpuClassKeyIndex is null) return false;
        if (t.RegPath.Contains(TokenNetInterfaces) && _machine.ActiveNetInterfaceGuids.Count == 0) return false;
        if (t.RegPath.Contains(TokenUsbClassKeys) && _machine.UsbClassKeys.Count == 0) return false;

        return true;
    }

    /// <summary>Expands path tokens into every concrete key this tweak touches on this machine.</summary>
    public IReadOnlyList<string> ResolvePaths(TweakDefinition t)
    {
        if (!IsApplicable(t)) return [];

        IEnumerable<string> paths = [t.RegPath];

        if (t.RegPath.Contains(TokenGpuClassKey))
        {
            var index = _machine.GpuClassKeyIndex!;
            paths = paths.Select(p => p.Replace(TokenGpuClassKey,
                $@"SYSTEM\CurrentControlSet\Control\Class\{DisplayClassGuid}\{index}"));
        }

        if (t.RegPath.Contains(TokenNetInterfaces))
        {
            paths = paths.SelectMany(p => _machine.ActiveNetInterfaceGuids.Select(g =>
                p.Replace(TokenNetInterfaces,
                    $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{g}")));
        }

        if (t.RegPath.Contains(TokenUsbClassKeys))
        {
            paths = paths.SelectMany(p => _machine.UsbClassKeys.Select(k =>
                p.Replace(TokenUsbClassKeys, $@"SYSTEM\CurrentControlSet\Services\Class\USB\{k}")));
        }

        return paths.ToList();
    }

    /// <summary>Every (key, value) pair this tweak writes on this machine.</summary>
    public IReadOnlyList<ResolvedTarget> Resolve(TweakDefinition t) =>
        ResolvePaths(t).SelectMany(p => t.Values.Select(v => new ResolvedTarget(p, v))).ToList();

    public TweakStatus GetStatus(TweakDefinition t)
    {
        var targets = Resolve(t);
        if (targets.Count == 0) return TweakStatus.NotApplicable;

        var matches = 0;
        foreach (var target in targets)
        {
            using var key = RegistryPath.OpenRead(target.RegPath);
            var current = key?.GetValue(target.ValueName);
            if (current is not null && ValuesEqual(current, Normalize(target.Value.OnValue, target.Value.Kind)))
                matches++;
        }

        if (matches == 0) return TweakStatus.Off;
        return matches == targets.Count ? TweakStatus.On : TweakStatus.Partial;
    }

    /// <summary>Live values of the first resolved key, for the "show real value" button.</summary>
    public IReadOnlyList<(string ValueName, object? Value)> ReadCurrent(TweakDefinition t)
    {
        var path = ResolvePaths(t).FirstOrDefault();
        if (path is null) return [];

        using var key = RegistryPath.OpenRead(path);
        return t.Values.Select(v => (v.ValueName, key?.GetValue(v.ValueName))).ToList();
    }

    /// <summary>Writes the on values to every resolved target. Caller must have taken a backup first.</summary>
    public void Apply(TweakDefinition t)
    {
        foreach (var path in ResolvePaths(t))
        {
            using var key = RegistryPath.OpenWrite(path);
            foreach (var v in t.Values)
                key.SetValue(v.ValueName, Normalize(v.OnValue, v.Kind), v.Kind);
        }
    }

    /// <summary>
    /// Restores from a snapshot entry: writes the captured value back, or deletes the value
    /// when it did not exist before the tweak was applied.
    /// </summary>
    public static void Restore(RegistryValueSnapshot snap)
    {
        if (!snap.Existed)
        {
            if (!RegistryPath.KeyExists(snap.RegPath)) return;
            using var writable = RegistryPath.OpenWrite(snap.RegPath);
            writable.DeleteValue(snap.ValueName, throwOnMissingValue: false);
            return;
        }

        var kind = Enum.TryParse<RegistryValueKind>(snap.Kind, out var k) ? k : RegistryValueKind.DWord;
        using var target = RegistryPath.OpenWrite(snap.RegPath);
        target.SetValue(snap.ValueName, Normalize(snap.Value!, kind), kind);
    }

    /// <summary>Fallback revert when no snapshot exists: write DefaultValue, or delete when it is null.</summary>
    public void RevertToDefault(TweakDefinition t)
    {
        foreach (var path in ResolvePaths(t))
        {
            foreach (var v in t.Values)
            {
                if (v.DefaultValue is null)
                {
                    if (!RegistryPath.KeyExists(path)) continue;
                    using var writable = RegistryPath.OpenWrite(path);
                    writable.DeleteValue(v.ValueName, throwOnMissingValue: false);
                }
                else
                {
                    using var writable = RegistryPath.OpenWrite(path);
                    writable.SetValue(v.ValueName, Normalize(v.DefaultValue, v.Kind), v.Kind);
                }
            }
        }
    }

    // --- value handling ----------------------------------------------------

    /// <summary>JSON gives us JsonElement; the registry wants int/long/string/byte[]. Convert per value kind.</summary>
    public static object Normalize(object raw, RegistryValueKind kind)
    {
        var value = raw is JsonElement je ? FromJson(je) : raw;

        return kind switch
        {
            RegistryValueKind.DWord => unchecked((int)Convert.ToInt64(value)),
            RegistryValueKind.QWord => Convert.ToInt64(value),
            RegistryValueKind.String or RegistryValueKind.ExpandString => Convert.ToString(value) ?? "",
            RegistryValueKind.MultiString => value as string[] ?? [Convert.ToString(value) ?? ""],
            RegistryValueKind.Binary => value as byte[] ?? ToBytes(value),
            _ => value
        };
    }

    private static object FromJson(JsonElement je) => je.ValueKind switch
    {
        JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
        JsonValueKind.String => je.GetString()!,
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        JsonValueKind.Array => je.EnumerateArray().Select(e => e.GetString() ?? "").ToArray(),
        _ => throw new NotSupportedException($"Unsupported JSON value kind {je.ValueKind}")
    };

    private static byte[] ToBytes(object value) => value switch
    {
        string s => Convert.FromHexString(s.Replace(",", "").Replace(" ", "").Replace("-", "")),
        int i => BitConverter.GetBytes(i),
        long l => BitConverter.GetBytes(l),
        _ => []
    };

    private static bool ValuesEqual(object a, object b)
    {
        if (a is byte[] ba && b is byte[] bb) return ba.AsSpan().SequenceEqual(bb);
        if (a is string[] sa && b is string[] sb) return sa.SequenceEqual(sb);
        if (a is string || b is string) return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);

        try { return Convert.ToInt64(a) == Convert.ToInt64(b); }
        catch { return Equals(a, b); }
    }
}
