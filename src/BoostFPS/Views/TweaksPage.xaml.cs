using System.Windows;
using System.Windows.Controls;
using BoostFPS.Core.Models;
using BoostFPS.Core.Services;
using BoostFPS.ViewModels;

namespace BoostFPS.Views;

public sealed record TweakCategory(string Name, IReadOnlyList<TweakItemViewModel> Items);

public partial class TweaksPage : Page
{
    private readonly List<TweakItemViewModel> _items;
    private bool _suppressToggle;

    public TweaksPage()
    {
        InitializeComponent();

        var engine = App.Engine;
        _items = engine.AvailableTweaks()
            .Select(t => new TweakItemViewModel(t, engine.Tweaks))
            .ToList();

        CategoryList.ItemsSource = _items
            .GroupBy(i => i.Category)
            .Select(g => new TweakCategory(g.Key, g.ToList()))
            .ToList();

        UpdateSummary();
    }

    /// <summary>
    /// Toggle click = apply the tweak on, or revert it off. Confirmation covers
    /// Aggressive-risk ones; the engine takes a backup and adds a changelog entry.
    /// The switch itself is bound one-way to the live status, so if the user cancels
    /// we simply refresh and it snaps back to where it was.
    /// </summary>
    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        if (sender is not CheckBox { Tag: TweakItemViewModel vm } cb) return;

        var wantOn = cb.IsChecked == true;
        if (wantOn == (vm.Status == TweakStatus.On))
        {
            RefreshAll();
            return;
        }

        var confirm = ConfirmToggle(vm, wantOn);
        if (confirm == MessageBoxResult.Cancel)
        {
            RefreshAll();
            return;
        }

        if (wantOn) ApplyOne(vm);
        else RevertOne(vm);
    }

    private static MessageBoxResult ConfirmToggle(TweakItemViewModel vm, bool wantOn)
    {
        var action = wantOn ? "เปิด" : "ปิด";
        var extra = vm.Definition.Risk == RiskLevel.Aggressive
            ? "\n\ntweak นี้เป็นระดับ Aggressive อาจกระทบเสถียรภาพ"
            : "";
        var reboot = vm.Definition.RequiresReboot ? "\nต้องรีสตาร์ทเพื่อให้มีผลครบ" : "";

        return MessageBox.Show(
            $"{action} \"{vm.Name}\"?{extra}{reboot}\n\nโปรแกรมจะ backup ค่าเดิมก่อนเสมอ",
            "ยืนยัน", MessageBoxButton.OKCancel,
            vm.Definition.Risk == RiskLevel.Aggressive ? MessageBoxImage.Warning : MessageBoxImage.Question);
    }

    private void ApplyOne(TweakItemViewModel vm) => RunApply([vm], $"Toggle on: {vm.Name}");

    private void RunApply(IReadOnlyList<TweakItemViewModel> vms, string description)
    {
        var result = App.Engine.Apply(new ApplyRequest
        {
            Tweaks = vms.Select(v => v.Definition).ToList(),
            Description = description
        });

        RefreshAll();

        if (!result.Success)
            MessageBox.Show(string.Join("\n", result.Failures), "ล้มเหลวบางส่วน",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RevertOne(TweakItemViewModel vm)
    {
        try { App.Engine.Tweaks.RevertToDefault(vm.Definition); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "revert ล้มเหลว", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        App.Engine.Changelog.Add("Reverted", $"Toggle off: {vm.Name} [{vm.Id}]");
        RefreshAll();
    }

    private void ApplyTier_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tierName }) return;
        if (!Enum.TryParse<TweakTier>(tierName, out var tier)) return;

        var todo = _items
            .Where(i => i.Definition.Tiers.Contains(tier) && i.Status != TweakStatus.On)
            .ToList();

        if (todo.Count == 0)
        {
            MessageBox.Show($"ชุด {tier} เปิดครบแล้ว", "BoostFPS");
            return;
        }

        var aggressive = todo.Count(i => i.Definition.Risk == RiskLevel.Aggressive);
        var msg = $"จะเปิด {todo.Count} tweak ในชุด {tier}"
                  + (aggressive > 0 ? $" (Aggressive {aggressive} รายการ)" : "")
                  + "\n\nดำเนินการต่อ?";

        var confirm = MessageBox.Show(msg, "ยืนยัน", MessageBoxButton.OKCancel,
            aggressive > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question);

        if (confirm == MessageBoxResult.OK) RunApply(todo, $"Apply tier {tier}");
    }

    private void RevertAll_Click(object sender, RoutedEventArgs e)
    {
        var on = _items.Where(i => i.Status != TweakStatus.Off && i.Status != TweakStatus.NotApplicable).ToList();
        if (on.Count == 0)
        {
            MessageBox.Show("ไม่มี tweak ที่เปิดอยู่", "BoostFPS");
            return;
        }

        var confirm = MessageBox.Show(
            $"จะปิด (revert) {on.Count} tweak ที่เปิดอยู่\nถ้ามี backup snapshot ที่ตรงกัน แนะนำใช้หน้า Backup แทน เพื่อคืนค่าเดิมเป๊ะ\n\nทำต่อ?",
            "ยืนยัน revert ทั้งหมด", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK) return;

        foreach (var vm in on)
        {
            try { App.Engine.Tweaks.RevertToDefault(vm.Definition); } catch { }
            App.Engine.Changelog.Add("Reverted", $"Revert all: {vm.Name}");
        }
        RefreshAll();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();

    private void ShowValues_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TweakItemViewModel vm }) return;
        var values = vm.LiveValues;
        MessageBox.Show(
            string.IsNullOrWhiteSpace(values) ? "ไม่พบ key นี้บนเครื่อง" : values,
            $"ค่าจริงตอนนี้ - {vm.Name}", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshAll()
    {
        _suppressToggle = true;
        try
        {
            foreach (var i in _items) i.RefreshStatus();
        }
        finally { _suppressToggle = false; }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var on = _items.Count(i => i.Status == TweakStatus.On);
        var partial = _items.Count(i => i.Status == TweakStatus.Partial);

        SummaryText.Text =
            $"ใช้ได้บนเครื่องนี้ {_items.Count} tweak  •  เปิดอยู่ {on}  •  บางส่วน {partial}" +
            $"  •  ซ่อนเพราะฮาร์ดแวร์ไม่ตรง {Catalog.Tweaks.Count - _items.Count}";
    }
}
