using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BoostFPS.Core.Models;
using BoostFPS.Core.Services;

namespace BoostFPS.Views;

public sealed class SnapshotRow
{
    public required BackupSnapshot Snapshot { get; init; }

    public string Id => Snapshot.Id;
    public string Description => Snapshot.Description;
    public DateTimeOffset CreatedAt => Snapshot.CreatedAt;

    public string Summary =>
        $"{Snapshot.RegistryValues.Count} registry values  •  {Snapshot.Services.Count} services  •  " +
        $"restore point: {(Snapshot.RestorePointSequence is null ? "ไม่มี" : Snapshot.RestorePointSequence.ToString())}  •  " +
        $"tweaks: {(Snapshot.AppliedTweakIds.Count == 0 ? "-" : string.Join(", ", Snapshot.AppliedTweakIds.Take(4)))}" +
        (Snapshot.AppliedTweakIds.Count > 4 ? $" +{Snapshot.AppliedTweakIds.Count - 4}" : "");
}

public partial class BackupPage : Page
{
    public BackupPage()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload() =>
        SnapshotList.ItemsSource = App.Engine.Backups.List()
            .Select(s => new SnapshotRow { Snapshot = s })
            .ToList();

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Backups}\"") { UseShellExecute = true });
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BackupSnapshot snapshot }) return;

        var confirm = MessageBox.Show(
            $"จะเขียนค่าเดิมจาก snapshot {snapshot.Id} กลับทั้งหมด\n" +
            $"({snapshot.RegistryValues.Count} registry values, {snapshot.Services.Count} services)\n\n" +
            "ค่าที่เปลี่ยนหลังจาก snapshot นี้จะถูกทับ ดำเนินการต่อ?",
            "ยืนยันกู้คืน", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK) return;

        var report = App.Engine.Revert(snapshot);

        var lines = new List<string>
        {
            $"คืนค่า registry {report.RegistryRestored} รายการ",
            $"คืนค่า service {report.ServicesRestored} รายการ",
            "บาง tweak มีผลหลังรีสตาร์ทเท่านั้น"
        };
        if (report.Failures.Count > 0) lines.Add("\nล้มเหลว:\n" + string.Join("\n", report.Failures));

        MessageBox.Show(string.Join("\n", lines), "ผลการกู้คืน",
            MessageBoxButton.OK,
            report.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
}
