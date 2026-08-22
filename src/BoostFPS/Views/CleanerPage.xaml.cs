using System.Windows;
using System.Windows.Controls;
using BoostFPS.Core.Services;

namespace BoostFPS.Views;

public partial class CleanerPage : Page
{
    public CleanerPage()
    {
        InitializeComponent();
        RefreshStats();
    }

    private void RefreshStats()
    {
        var s = App.Engine.Memory.ReadStats();
        var used = (s.TotalBytes - s.AvailableBytes) / 1024.0 / 1024 / 1024;
        var total = s.TotalBytes / 1024.0 / 1024 / 1024;

        StatsText.Text =
            $"ใช้: {used:F1} GB / {total:F1} GB   ({s.UsedPercent:F0}%)\n" +
            $"ว่าง: {s.AvailableBytes / 1024.0 / 1024 / 1024:F1} GB   •   commit: {s.CommittedBytes / 1024.0 / 1024 / 1024:F1} GB";

        UsageBar.Value = s.UsedPercent;
    }

    private void Log(CleanerStep step)
    {
        var mark = step.Success ? "OK" : "FAIL";
        OutputBox.Text = $"[{DateTime.Now:HH:mm:ss}] {mark}  {step.Name}  -  {step.Detail}\n" + OutputBox.Text;
        App.Engine.Changelog.Add(step.Success ? "Applied" : "Failed", $"Cleaner: {step.Name} - {step.Detail}");
        RefreshStats();
    }

    private void Standby_Click(object sender, RoutedEventArgs e) => Log(App.Engine.Memory.PurgeStandbyList());
    private void WorkingSet_Click(object sender, RoutedEventArgs e) => Log(App.Engine.Memory.EmptyAllWorkingSets());
    private void Modified_Click(object sender, RoutedEventArgs e) => Log(App.Engine.Memory.FlushModifiedPageList());
    private void Temp_Click(object sender, RoutedEventArgs e) => Log(App.Engine.Memory.ClearTempFolders());

    private void All_Click(object sender, RoutedEventArgs e)
    {
        var mem = App.Engine.Memory;
        Log(mem.EmptyAllWorkingSets());
        Log(mem.FlushModifiedPageList());
        Log(mem.PurgeLowPriorityStandbyList());
        Log(mem.PurgeStandbyList());
        Log(mem.ClearTempFolders());
    }
}
