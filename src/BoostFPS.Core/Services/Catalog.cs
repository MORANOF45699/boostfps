using System.Reflection;
using System.Text.Json;
using BoostFPS.Core.Models;

namespace BoostFPS.Core.Services;

/// <summary>Loads the embedded tweak and service definitions shipped with the app.</summary>
public static class Catalog
{
    private static readonly Lazy<IReadOnlyList<TweakDefinition>> _tweaks =
        new(() => Load<TweakDefinition>("tweaks.json"));

    private static readonly Lazy<IReadOnlyList<ServiceDefinition>> _services =
        new(() => Load<ServiceDefinition>("services.json"));

    public static IReadOnlyList<TweakDefinition> Tweaks => _tweaks.Value;
    public static IReadOnlyList<ServiceDefinition> Services => _services.Value;

    private static IReadOnlyList<T> Load<T>(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Embedded resource {fileName} is missing");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<List<T>>(stream, Json.Options) ?? [];
    }
}
