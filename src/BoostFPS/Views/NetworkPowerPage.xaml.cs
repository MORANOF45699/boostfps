using System.IO;
using System.Windows;
using System.Windows.Controls;
using BoostFPS.Core.Services;
using Microsoft.Win32;

namespace BoostFPS.Views;

public partial class NetworkPowerPage : Page
{
    public NetworkPowerPage()
    {
        InitializeComponent();

        if (App.Engine.Machine.IsLaptop)
        {
            LaptopWarning.Text =
                "เครื่องนี้เป็นโน้ตบุ๊ก — Ultimate performance จะกินแบตหนักและทำให้ร้อน ใช้เฉพาะตอนเสียบปลั๊ก";
        }
        else
        {
            LaptopWarning.Visibility = Visibility.Collapsed;
        }

        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var active = App.Engine.Power.ActivePlan();
        ActivePlanText.Text = active is null
            ? "อ่าน power plan ปัจจุบันไม่ได้"
            : $"ใช้อยู่: {active.Name}  ({active.Guid})";

        DynamicTickText.Text = App.Engine.Power.IsDynamicTickDisabled()
            ? "disabledynamictick = Yes (ปิดอยู่ ลด timer latency) — มีผลหลังรีสตาร์ท"
            : "disabledynamictick = No (ค่าเริ่มต้นของ Windows)";
    }

    private void Log(string text)
    {
        OutputBox.Text = $"[{DateTime.Now:HH:mm:ss}] {text}\n\n{OutputBox.Text}";
    }

    private void NicPower_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "การ์ดเน็ตจะถูกรีสตาร์ต เน็ตจะหลุดสองสามวินาที ดำเนินการต่อ?",
            "ปิด NIC power saving", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        var result = App.Engine.Network.DisableNicPowerSaving();
        App.Engine.Changelog.Add(result.Success ? "Applied" : "Failed", $"NIC power saving: {result.Detail}");
        Log($"{result.Step}: {(result.Success ? "OK" : "FAILED")}\n{result.Detail}");
    }

    private void TcpGlobals_Click(object sender, RoutedEventArgs e)
    {
        var result = App.Engine.Network.ApplyTcpGlobals();
        App.Engine.Changelog.Add(result.Success ? "Applied" : "Failed", $"netsh TCP globals: {result.Detail}");
        Log($"{result.Step}: {(result.Success ? "OK" : "FAILED")}\n{result.Detail}");
    }

    private void ShowTcp_Click(object sender, RoutedEventArgs e) =>
        Log(App.Engine.Network.ReadTcpGlobals());

    private void HighPerf_Click(object sender, RoutedEventArgs e) => Activate(PowerService.HighPerformance, "High performance");

    private void Ultimate_Click(object sender, RoutedEventArgs e)
    {
        if (App.Engine.Machine.IsLaptop)
        {
            var confirm = MessageBox.Show(
                "Ultimate performance บนโน้ตบุ๊กจะกินแบตหนักมากและทำให้เครื่องร้อน ยืนยัน?",
                "ยืนยัน", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;
        }

        Activate(PowerService.UltimatePerformance, "Ultimate performance");
    }

    private void BalancedPlan_Click(object sender, RoutedEventArgs e) => Activate(PowerService.Balanced, "Balanced");

    private void Activate(Guid plan, string name)
    {
        var ok = App.Engine.Power.Activate(plan);
        App.Engine.Changelog.Add(ok ? "Applied" : "Failed", $"Power plan -> {name}");
        Log($"Power plan {name}: {(ok ? "OK" : "FAILED")}");
        RefreshStatus();
    }

    private void ImportPow_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Power plan (*.pow)|*.pow|All files (*.*)|*.*",
            InitialDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Presets")
        };

        if (dialog.ShowDialog() != true) return;

        var (success, detail) = App.Engine.Power.ImportPowerPlan(dialog.FileName);
        App.Engine.Changelog.Add(success ? "Applied" : "Failed", $"Import power plan {Path.GetFileName(dialog.FileName)}: {detail}");
        Log($"Import {Path.GetFileName(dialog.FileName)}: {detail}");
        RefreshStatus();
    }

    private void ExportPow_Click(object sender, RoutedEventArgs e)
    {
        var file = App.Engine.Power.ExportActivePlan(Path.Combine(AppPaths.Backups, "power"));
        Log(file is null ? "export power plan ไม่สำเร็จ" : $"export ไว้ที่ {file}");
    }

    private void DisableTick_Click(object sender, RoutedEventArgs e) => SetTick(true);
    private void EnableTick_Click(object sender, RoutedEventArgs e) => SetTick(false);

    private void SetTick(bool disabled)
    {
        var ok = App.Engine.Power.SetDynamicTickDisabled(disabled);
        App.Engine.Changelog.Add(ok ? "Applied" : "Failed", $"bcdedit disabledynamictick = {(disabled ? "yes" : "no")}");
        Log($"bcdedit disabledynamictick {(disabled ? "yes" : "no")}: {(ok ? "OK" : "FAILED")} (มีผลหลังรีสตาร์ท)");
        RefreshStatus();
    }
}
