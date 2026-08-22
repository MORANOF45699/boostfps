using System.Windows.Media;
using BoostFPS.Core.Models;
using BoostFPS.Core.Services;

namespace BoostFPS.ViewModels;

public sealed class ServiceItemViewModel(ServiceEntry entry) : ObservableObject
{
    public ServiceEntry Entry { get; private set; } = entry;
    public ServiceDefinition Definition => Entry.Definition;

    public string Name => Definition.Name;
    public string DisplayName => string.IsNullOrEmpty(Definition.DisplayName) ? Definition.Name : Definition.DisplayName;
    public string Impact => Definition.Impact;
    public string TierText => Definition.Tier.ToString();
    public bool CanToggle => Entry.CanToggle;

    /// <summary>Toggle-switch binding. On = service is currently Disabled (our on state for a service).</summary>
    public bool IsDisabled
    {
        get => Entry.CurrentStart == ServiceStart.Disabled;
        // Page intercepts the click and calls the engine; the setter only exists so
        // TwoWay binding compiles. Raising snaps the switch back if the user cancels.
        set { _ = value; Raise(); }
    }

    public string StatusText => Entry.CurrentStart switch
    {
        null => "ไม่มี",
        ServiceStart.Disabled => "ปิดแล้ว",
        var s => s.ToString() ?? "-"
    };

    public Brush StatusBrush => Entry.CurrentStart == ServiceStart.Disabled
        ? Brushes.LimeGreen
        : Brushes.Goldenrod;

    public string GateText
    {
        get
        {
            var parts = new List<string>();

            if (Entry.Gate.Result != GateResult.Allowed && !string.IsNullOrEmpty(Entry.Gate.Reason))
            {
                parts.Add(Entry.Gate.Result == GateResult.Warned
                    ? $"เตือน: {Entry.Gate.Reason}"
                    : $"ข้าม: {Entry.Gate.Reason}");
            }

            if (Entry.Dependents.Count > 0)
                parts.Add($"มี service อื่นพึ่งพา: {string.Join(", ", Entry.Dependents)}");

            return string.Join("  •  ", parts);
        }
    }

    public Brush GateBrush => Entry.Gate.Result switch
    {
        GateResult.HardwareMismatch => Brushes.SkyBlue,
        GateResult.Warned => Brushes.Goldenrod,
        GateResult.Blocked => Brushes.IndianRed,
        _ => Brushes.Goldenrod
    };

    public void Refresh(ServiceEntry updated)
    {
        Entry = updated;
        Raise(nameof(StatusText));
        Raise(nameof(StatusBrush));
        Raise(nameof(GateText));
        Raise(nameof(GateBrush));
        Raise(nameof(CanToggle));
        Raise(nameof(IsDisabled));
    }
}
