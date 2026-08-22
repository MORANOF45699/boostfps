using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using BoostFPS.Core.Services;
using Microsoft.Win32;

namespace BoostFPS.Views;

public sealed class GameRow
{
    public required string ExePath { get; init; }
    public required string ExeName { get; init; }
    public required string WindowTitle { get; init; }
    public required bool InGameMode { get; init; }
    public required int Pid { get; init; }
    public required WindowsGpuPreference GpuPreference { get; init; }
    public required bool IsRunning { get; init; }
    public required bool InWindowsGraphicsList { get; init; }

    public string StatusText
    {
        get
        {
            var parts = new List<string>();
            if (IsRunning) parts.Add("กำลังเล่น");
            if (InGameMode) parts.Add("Game Mode");
            if (InWindowsGraphicsList) parts.Add($"Graphics: {GpuPreference}");
            return parts.Count == 0 ? "" : "  •  " + string.Join("  •  ", parts);
        }
    }

    public Brush StatusBrush => IsRunning ? Brushes.LimeGreen : Brushes.SkyBlue;

    public Visibility BoostVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AddGmVisibility => InGameMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RemoveGmVisibility => InGameMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AddGpuVisibility => InWindowsGraphicsList ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RemoveGpuVisibility => InWindowsGraphicsList ? Visibility.Visible : Visibility.Collapsed;
}

public partial class GamesPage : Page
{
    public GamesPage()
    {
        InitializeComponent();
        Reload();
    }

    /// <summary>
    /// Merges three sources so every exe shows up once:
    ///   - live processes with a visible window (Boost live)
    ///   - Game Mode registrations under HKCU\System\GameConfigStore\Children
    ///   - Windows Graphics settings under HKCU\SOFTWARE\Microsoft\DirectX\UserGpuPreferences
    /// </summary>
    private void Reload()
    {
        var running = App.Engine.Games.ListCandidates().ToList();
        var graphics = App.Engine.Games.ListWindowsGraphicsPreferences().ToList();

        var byPath = new Dictionary<string, GameRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in running)
        {
            var pref = graphics.FirstOrDefault(p => PathsEqual(p.ExePath, g.ExePath));
            byPath[g.ExePath] = new GameRow
            {
                ExePath = g.ExePath,
                ExeName = g.ExeName,
                WindowTitle = g.WindowTitle,
                Pid = g.Pid,
                IsRunning = true,
                InGameMode = App.Engine.Games.IsInGameMode(g.ExePath),
                InWindowsGraphicsList = pref is not null,
                GpuPreference = pref?.Preference ?? WindowsGpuPreference.SystemDefault
            };
        }

        foreach (var pref in graphics)
        {
            if (byPath.ContainsKey(pref.ExePath)) continue;

            byPath[pref.ExePath] = new GameRow
            {
                ExePath = pref.ExePath,
                ExeName = pref.ExeName,
                WindowTitle = pref.Exists ? "" : "(ไฟล์หายไป)",
                Pid = 0,
                IsRunning = false,
                InGameMode = App.Engine.Games.IsInGameMode(pref.ExePath),
                InWindowsGraphicsList = true,
                GpuPreference = pref.Preference
            };
        }

        GameList.ItemsSource = byPath.Values
            .OrderByDescending(r => r.IsRunning)
            .ThenBy(r => r.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Boost_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameRow row }) return;

        var ok = App.Engine.Games.BoostLiveProcess(row.Pid);
        App.Engine.Changelog.Add(ok ? "Applied" : "Failed", $"Live boost {row.ExeName} pid={row.Pid}");

        MessageBox.Show(
            ok ? $"ตั้ง {row.ExeName} เป็น High + affinity หนี core 0 แล้ว\nคืนค่าตอนปิดโปรเซส"
               : "boost ไม่สำเร็จ (อาจปิดไปแล้ว หรือ access denied)",
            "Boost", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void AddGameMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameRow row }) return;
        App.Engine.Games.AddToGameMode(row.ExePath);
        App.Engine.Changelog.Add("Applied", $"Game Mode add: {row.ExePath}");
        Reload();
    }

    private void RemoveGameMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameRow row }) return;
        App.Engine.Games.RemoveFromGameMode(row.ExePath);
        App.Engine.Changelog.Add("Reverted", $"Game Mode remove: {row.ExePath}");
        Reload();
    }

    private void AddManual_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe" };
        if (dialog.ShowDialog() != true) return;

        App.Engine.Games.AddToGameMode(dialog.FileName);
        App.Engine.Games.SetWindowsGpuPreference(dialog.FileName, WindowsGpuPreference.HighPerformance);
        App.Engine.Changelog.Add("Applied", $"Game add manual: {dialog.FileName}");
        MessageBox.Show($"เพิ่ม {Path.GetFileName(dialog.FileName)} ลง Game Mode + Graphics=High performance",
            "BoostFPS");
        Reload();
    }

    private void SetHighPerf_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameRow row }) return;
        App.Engine.Games.SetWindowsGpuPreference(row.ExePath, WindowsGpuPreference.HighPerformance);
        App.Engine.Changelog.Add("Applied", $"Graphics HighPerformance: {row.ExePath}");
        Reload();
    }

    private void RemoveGpu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GameRow row }) return;
        App.Engine.Games.RemoveWindowsGpuPreference(row.ExePath);
        App.Engine.Changelog.Add("Reverted", $"Graphics preference remove: {row.ExePath}");
        Reload();
    }
}
