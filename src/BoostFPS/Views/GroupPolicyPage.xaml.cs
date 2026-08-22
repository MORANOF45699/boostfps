using System.Windows;
using System.Windows.Controls;
using BoostFPS.Core.Models;
using BoostFPS.Core.Services;
using BoostFPS.ViewModels;

namespace BoostFPS.Views;

/// <summary>
/// Group Policy front-end. Shows every tweak that either lives in the "Group Policy" category
/// or targets a \SOFTWARE\Policies\ path, so existing policy-shaped tweaks (Game DVR policy,
/// WER policy, QoS) show up here alongside the dedicated GPO catalog.
/// </summary>
public partial class GroupPolicyPage : Page
{
    private readonly List<TweakItemViewModel> _items;
    private bool _suppressToggle;

    public GroupPolicyPage()
    {
        InitializeComponent();

        var engine = App.Engine;
        _items = engine.AvailableTweaks()
            .Where(IsPolicyTweak)
            .Select(t => new TweakItemViewModel(t, engine.Tweaks))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        PolicyList.ItemsSource = _items;
        UpdateSummary();
    }

    private static bool IsPolicyTweak(TweakDefinition t) =>
        t.Category.Equals("Group Policy", StringComparison.OrdinalIgnoreCase)
        || t.RegPath.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase);

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        if (sender is not CheckBox { Tag: TweakItemViewModel vm } cb) return;

        var wantOn = cb.IsChecked == true;
        if (wantOn == (vm.Status == TweakStatus.On)) { RefreshAll(); return; }

        var confirm = MessageBox.Show(
            $"{(wantOn ? "เปิด" : "ปิด")} \"{vm.Name}\"?\n\nโปรแกรมจะ backup ค่าเดิมก่อนเสมอ" +
            (vm.Definition.RequiresReboot ? "\nต้องรีสตาร์ทเพื่อให้มีผลครบ" : ""),
            "ยืนยัน", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) { RefreshAll(); return; }

        if (wantOn)
        {
            App.Engine.Apply(new ApplyRequest
            {
                Tweaks = [vm.Definition],
                Description = $"GPO on: {vm.Definition.Id}"
            });
        }
        else
        {
            try { App.Engine.Tweaks.RevertToDefault(vm.Definition); } catch { }
            App.Engine.Changelog.Add("Reverted", $"GPO off: {vm.Definition.Id}");
        }

        RefreshAll();
    }

    private void ApplyAll_Click(object sender, RoutedEventArgs e)
    {
        var todo = _items.Where(i => i.Status != TweakStatus.On).Select(i => i.Definition).ToList();
        if (todo.Count == 0) { MessageBox.Show("policy เปิดครบแล้ว", "BoostFPS"); return; }

        var confirm = MessageBox.Show(
            $"จะเปิด {todo.Count} policy?\nโปรแกรมจะ backup ค่าเดิมก่อน",
            "ยืนยัน", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        App.Engine.Apply(new ApplyRequest { Tweaks = todo, Description = "GPO apply all" });
        RefreshAll();
    }

    private void RevertAll_Click(object sender, RoutedEventArgs e)
    {
        var on = _items.Where(i => i.Status != TweakStatus.Off).ToList();
        if (on.Count == 0) { MessageBox.Show("ไม่มี policy ที่เปิดอยู่", "BoostFPS"); return; }

        var confirm = MessageBox.Show(
            $"จะปิด {on.Count} policy คืนค่า default?",
            "ยืนยัน revert", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK) return;

        foreach (var vm in on)
        {
            try { App.Engine.Tweaks.RevertToDefault(vm.Definition); } catch { }
            App.Engine.Changelog.Add("Reverted", $"GPO revert: {vm.Definition.Id}");
        }
        RefreshAll();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();

    private void RefreshAll()
    {
        _suppressToggle = true;
        try { foreach (var i in _items) i.RefreshStatus(); }
        finally { _suppressToggle = false; }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var on = _items.Count(i => i.Status == TweakStatus.On);
        SummaryText.Text = $"Policy ทั้งหมด {_items.Count} รายการ  •  เปิดอยู่ {on}";
    }
}
