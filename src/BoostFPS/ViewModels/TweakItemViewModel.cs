using System.Windows.Media;
using BoostFPS.Core.Models;
using BoostFPS.Core.Services;

namespace BoostFPS.ViewModels;

public sealed class TweakItemViewModel : ObservableObject
{
    private readonly RegistryTweakService _service;
    private TweakStatus _status;

    public TweakItemViewModel(TweakDefinition definition, RegistryTweakService service)
    {
        Definition = definition;
        _service = service;
        _status = service.GetStatus(definition);
    }

    public TweakDefinition Definition { get; }

    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Id => Definition.Id;
    public string Category => Definition.Category;
    public int TargetCount => _service.Resolve(Definition).Count;

    /// <summary>The toggle binds to this. True = registry currently holds the on value on every target.</summary>
    public bool IsOn
    {
        get => Status == TweakStatus.On;
        // Setter only exists so a two-way binding compiles; the page intercepts the click
        // and drives Apply/Revert through the engine. Raising here keeps the switch in
        // whichever position the live status says it should be in.
        set { _ = value; Raise(); }
    }

    public TweakStatus Status
    {
        get => _status;
        private set
        {
            if (!Set(ref _status, value)) return;
            Raise(nameof(StatusText));
            Raise(nameof(StatusBrush));
            Raise(nameof(IsOn));
        }
    }

    public string StatusText => Status switch
    {
        TweakStatus.On => "ON",
        TweakStatus.Off => "OFF",
        TweakStatus.Partial => "PARTIAL",
        _ => "N/A"
    };

    public Brush StatusBrush => Status switch
    {
        TweakStatus.On => Brushes.LimeGreen,
        TweakStatus.Partial => Brushes.Goldenrod,
        _ => Brushes.Gray
    };

    public string RiskText => Definition.Risk switch
    {
        RiskLevel.Safe => "ปลอดภัย",
        RiskLevel.Moderate => "ปานกลาง",
        _ => "เสี่ยงสูง"
    };

    public Brush RiskBrush => Definition.Risk switch
    {
        RiskLevel.Safe => Brushes.MediumSeaGreen,
        RiskLevel.Moderate => Brushes.Goldenrod,
        _ => Brushes.IndianRed
    };

    public string Meta
    {
        get
        {
            var tiers = Definition.Tiers.Length == 0 ? "opt-in" : string.Join(" / ", Definition.Tiers);
            var reboot = Definition.RequiresReboot ? "  •  ต้องรีสตาร์ท" : "";
            var targets = TargetCount > 1 ? $"  •  {TargetCount} ค่า" : "";
            return $"{Definition.RegPath}  •  {tiers}{targets}{reboot}";
        }
    }

    public string LiveValues =>
        string.Join("\n", _service.ReadCurrent(Definition)
            .Select(v => $"{v.ValueName} = {Describe(v.Value)}"));

    public void RefreshStatus() => Status = _service.GetStatus(Definition);

    private static string Describe(object? value) => value switch
    {
        null => "(ไม่มีค่า)",
        byte[] bytes => Convert.ToHexString(bytes),
        string[] many => string.Join(", ", many),
        _ => value.ToString() ?? ""
    };
}
