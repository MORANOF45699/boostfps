using System.IO;
using System.Windows;
using System.Windows.Controls;
using BoostFPS.Core.Services;
using Microsoft.Win32;

namespace BoostFPS.Views;

public sealed class BaselineRow
{
    public required DiffRow Diff { get; init; }

    public string Category => Diff.Category;
    public string TweakName => Diff.TweakName;
    public string ValueName => Diff.ValueName;
    public string CurrentDisplay => Diff.CurrentDisplay;
    public string DefaultDisplay => Diff.DefaultDisplay;
    public string OnDisplay => Diff.OnDisplay;

    public string StateText => Diff.State switch
    {
        DiffState.Tuned => "TUNED",
        DiffState.Default => "default",
        DiffState.Missing => "missing",
        DiffState.Other => "OTHER",
        _ => "-"
    };
}

public partial class BaselinePage : Page
{
    private IReadOnlyList<DiffRow> _all = [];

    public BaselinePage()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        _all = App.Engine.BaselineDiff.BuildDiff();
        ApplyFilter();

        var s = BaselineDiffService.Summarize(_all);
        SummaryText.Text =
            $"ทั้งหมด {_all.Count} ค่า  •  " +
            $"tune แล้ว {s.Tuned}  •  ตรง default {s.Default}  •  " +
            $"missing {s.Missing}  •  ค่าอื่น {s.Other}  •  ไม่มี default อ้างอิง {s.NoDefault}";
    }

    private void ApplyFilter()
    {
        var rows = DiffOnly.IsChecked == true
            ? _all.Where(r => r.DiffersFromDefault)
            : _all;

        DiffGrid.ItemsSource = rows.Select(r => new BaselineRow { Diff = r }).ToList();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();
    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"boostfps_baseline_{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog() != true) return;

        using var writer = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(true));
        writer.WriteLine("Category,TweakId,TweakName,RegPath,ValueName,Current,Default,OnValue,State");

        foreach (var r in _all)
        {
            writer.WriteLine(string.Join(",",
                Csv(r.Category), Csv(r.TweakId), Csv(r.TweakName), Csv(r.RegPath),
                Csv(r.ValueName), Csv(r.CurrentDisplay), Csv(r.DefaultDisplay),
                Csv(r.OnDisplay), r.State));
        }

        MessageBox.Show($"เซฟไว้ที่\n{dialog.FileName}", "Export");
    }

    private static string Csv(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
