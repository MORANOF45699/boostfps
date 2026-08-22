using System.Windows;
using System.Windows.Controls;
using BoostFPS.Core.Models;
using BoostFPS.Core.Services;
using BoostFPS.ViewModels;

namespace BoostFPS.Views;

public partial class ServicesPage : Page
{
    private List<ServiceItemViewModel> _items = [];

    public ServicesPage()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        _items = App.Engine.AvailableServices().Select(e => new ServiceItemViewModel(e)).ToList();
        ServiceList.ItemsSource = _items;
        UpdateSummary();
    }

    /// <summary>
    /// Toggle click on = disable the service and stop it; toggle off = restore to the
    /// definition's default start type. Confirmation covers Extreme tier and any service
    /// with dependents. Cancel snaps the switch back via a full reload.
    /// </summary>
    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: ServiceItemViewModel vm } cb) return;

        var wantDisabled = cb.IsChecked == true;
        if (wantDisabled == vm.IsDisabled)
        {
            Reload();
            return;
        }

        var confirm = ConfirmToggle(vm, wantDisabled);
        if (confirm == MessageBoxResult.Cancel) { Reload(); return; }

        if (wantDisabled)
        {
            var result = App.Engine.Apply(new ApplyRequest
            {
                Tweaks = [],
                Services = [vm.Definition],
                Description = $"Disable service {vm.Name}"
            });

            if (!result.Success)
                MessageBox.Show(string.Join("\n", result.Failures), "ล้มเหลว",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            RestoreService(vm);
        }

        Reload();
    }

    private static MessageBoxResult ConfirmToggle(ServiceItemViewModel vm, bool wantDisabled)
    {
        var action = wantDisabled ? "ปิด" : "เปิดกลับ";
        var dependents = vm.Entry.Dependents.Count > 0
            ? $"\n\nมี service อื่นพึ่งพา: {string.Join(", ", vm.Entry.Dependents)}"
            : "";
        var extreme = vm.Definition.Tier == TweakTier.Extreme
            ? "\n\ntier Extreme - กระทบระบบชัดเจน อ่านผลกระทบก่อน"
            : "";

        return MessageBox.Show(
            $"{action} \"{vm.DisplayName}\" ({vm.Name})?\n{vm.Impact}{dependents}{extreme}",
            "ยืนยัน", MessageBoxButton.OKCancel,
            vm.Definition.Tier == TweakTier.Extreme || vm.Entry.Dependents.Count > 0
                ? MessageBoxImage.Warning : MessageBoxImage.Question);
    }

    private static void RestoreService(ServiceItemViewModel vm)
    {
        var target = vm.Definition.DefaultStart ?? ServiceStart.Manual;
        try
        {
            WindowsServiceService.WriteStart(vm.Name, target);
            App.Engine.Changelog.Add("Reverted", $"Service {vm.Name} -> {target}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "revert ล้มเหลว", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DisableTier_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tierName }) return;
        if (!Enum.TryParse<TweakTier>(tierName, out var tier)) return;

        var todo = _items
            .Where(i => i.CanToggle && i.Definition.Tier <= tier && !i.IsDisabled)
            .ToList();

        if (todo.Count == 0)
        {
            MessageBox.Show($"ชุด {tier} ปิดครบแล้ว", "BoostFPS");
            return;
        }

        var confirm = MessageBox.Show(
            $"จะปิด {todo.Count} service ในชุด {tier}\nดำเนินการต่อ?",
            "ยืนยัน", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        App.Engine.Apply(new ApplyRequest
        {
            Tweaks = [],
            Services = todo.Select(i => i.Definition).ToList(),
            Description = $"Disable tier {tier}"
        });

        Reload();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void UpdateSummary()
    {
        var disabled = _items.Count(i => i.IsDisabled);
        var gated = _items.Count(i => !i.CanToggle);

        SummaryText.Text =
            $"แสดง {_items.Count} service  •  ปิดอยู่ {disabled}  •  ถูก gate ตัด {gated}" +
            $"  •  blocklist ที่ไม่แสดงเลย {WindowsServiceService.HardBlocklist.Count} ตัว";
    }
}
