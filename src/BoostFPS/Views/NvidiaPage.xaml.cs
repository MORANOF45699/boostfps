using System.IO;
using System.Windows;
using System.Windows.Controls;
using BoostFPS.Core.Models;
using BoostFPS.Core.Services;
using Microsoft.Win32;

namespace BoostFPS.Views;

public sealed class NvidiaProfileRow
{
    public required NvidiaProfile Profile { get; init; }
    public string ProfileName => Profile.ProfileName;
    public IReadOnlyList<NvidiaSetting> Settings => Profile.Settings;
    public string ExecutableList => string.Join(", ", Profile.Executables);
}

public partial class NvidiaPage : Page
{
    private string? _nipPath;

    public NvidiaPage()
    {
        InitializeComponent();

        var m = App.Engine.Machine;
        GpuText.Text = $"{m.GpuName}  [{m.GpuVendor}]";

        RefreshInspector();
        LoadPreset();
    }

    private void RefreshInspector()
    {
        var inspector = App.Engine.Nvidia.FindInspector();

        InspectorText.Text = inspector is null
            ? "ไม่พบ nvidiaProfileInspector.exe — วางไว้ที่โฟลเดอร์ Tools ข้างตัวโปรแกรม หรือกดปุ่มเลือกไฟล์"
            : $"ใช้ตัวช่วย: {inspector}";

        ImportButton.IsEnabled = inspector is not null && App.Engine.Nvidia.IsNvidia;
    }

    private void LoadPreset()
    {
        var preset = Path.Combine(AppContext.BaseDirectory, "Assets", "Presets", "FiveM_Clean.nip");
        if (File.Exists(preset)) SetNip(preset);
        else NipPathText.Text = "ยังไม่ได้เลือกไฟล์";
    }

    private void SetNip(string path)
    {
        _nipPath = path;
        NipPathText.Text = path;

        try
        {
            ProfileList.ItemsSource = NvidiaProfileService.Parse(path)
                .Select(p => new NvidiaProfileRow { Profile = p })
                .ToList();
        }
        catch (Exception ex)
        {
            ProfileList.ItemsSource = null;
            MessageBox.Show($"อ่านไฟล์ .nip ไม่สำเร็จ:\n{ex.Message}", "BoostFPS",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UsePreset_Click(object sender, RoutedEventArgs e) => LoadPreset();

    private void PickNip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "NVIDIA profile (*.nip)|*.nip|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true) SetNip(dialog.FileName);
    }

    private void PickInspector_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "nvidiaProfileInspector.exe|nvidiaProfileInspector.exe|Executable (*.exe)|*.exe"
        };

        if (dialog.ShowDialog() != true) return;

        App.Engine.Nvidia.SetInspectorPath(dialog.FileName);
        RefreshInspector();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(AppPaths.Backups, "nvidia");
        var file = App.Engine.Nvidia.ExportCurrent(dir);

        MessageBox.Show(
            file is null ? "export ไม่สำเร็จ (ยังไม่พบ nvidiaProfileInspector.exe?)" : $"export ไว้ที่\n{file}",
            "Export NVIDIA profiles", MessageBoxButton.OK,
            file is null ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_nipPath is null)
        {
            MessageBox.Show("ยังไม่ได้เลือกไฟล์ .nip", "BoostFPS");
            return;
        }

        var confirm = MessageBox.Show(
            $"จะ import {Path.GetFileName(_nipPath)} ลงไดรเวอร์ NVIDIA\n" +
            "โปรไฟล์ปัจจุบันทั้งหมดจะถูก export เก็บไว้ก่อน ดำเนินการต่อ?",
            "ยืนยัน import", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        ImportButton.IsEnabled = false;
        try
        {
            var result = App.Engine.Nvidia.Import(_nipPath, Path.Combine(AppPaths.Backups, "nvidia"));
            App.Engine.Changelog.Add(result.Success ? "Applied" : "Failed",
                $"NVIDIA import {Path.GetFileName(_nipPath)}: {result.Message}");

            var backup = result.BackupFile is null ? "ไม่ได้ backup โปรไฟล์เดิม" : $"backup เดิม: {result.BackupFile}";
            MessageBox.Show($"{result.Message}\n{backup}", "ผล import",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            RefreshInspector();
        }
    }
}
