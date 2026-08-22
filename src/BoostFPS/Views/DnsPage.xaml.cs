using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BoostFPS.Core.Services;

namespace BoostFPS.Views;

/// <summary>Row shown in the ranked-DNS list. Rank + colored latency badge based on ping.</summary>
public sealed class DnsRow
{
    public required int Rank { get; init; }
    public required DnsPingResult Result { get; init; }

    public string Name => Result.Preset.Name;
    public string Description => Result.Preset.Description;
    public string Servers => "  " + string.Join(" / ", Result.Preset.Servers);
    public string LatencyText => Result.LatencyText;

    public Brush LatencyBrush => Result.LatencyMs switch
    {
        < 0 => Brushes.Gray,
        < 20 => Brushes.LimeGreen,
        < 50 => Brushes.MediumSeaGreen,
        < 100 => Brushes.Goldenrod,
        _ => Brushes.IndianRed
    };
}

public sealed class AdapterOption
{
    public required string Alias { get; init; }
    public required string Label { get; init; }
}

public partial class DnsPage : Page
{
    public DnsPage()
    {
        InitializeComponent();
        LoadAdapters();
    }

    private void LoadAdapters()
    {
        var adapters = App.Engine.Dns.ListAdapters().Where(a => a.IsPhysical).ToList();

        AdapterCombo.ItemsSource = adapters
            .Select(a => new AdapterOption
            {
                Alias = a.InterfaceAlias,
                Label = $"{a.InterfaceAlias} — {a.Name}"
            })
            .ToList();

        if (adapters.Count > 0)
        {
            AdapterCombo.SelectedIndex = 0;
            ShowCurrent(adapters[0]);
            AdapterCombo.SelectionChanged += (_, _) => RefreshCurrent();
        }
    }

    private void RefreshCurrent()
    {
        if (AdapterCombo.SelectedValue is not string alias) return;

        var adapter = App.Engine.Dns.ListAdapters().FirstOrDefault(a => a.InterfaceAlias == alias);
        if (adapter is not null) ShowCurrent(adapter);
    }

    private void ShowCurrent(DnsAdapter a) =>
        CurrentDnsText.Text = a.CurrentDns.Length == 0
            ? "ตอนนี้ใช้: DHCP หรือไม่มี DNS"
            : "ตอนนี้ใช้: " + string.Join(", ", a.CurrentDns);

    private async void Rank_Click(object sender, RoutedEventArgs e)
    {
        RankButton.IsEnabled = false;
        UpdatedText.Text = "กำลัง ping...";

        try
        {
            var ranked = await App.Engine.Dns.RankPresetsAsync();
            ResultList.ItemsSource = ranked
                .Select((r, i) => new DnsRow { Rank = i + 1, Result = r })
                .ToList();

            UpdatedText.Text = $"Updated at {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            RankButton.IsEnabled = true;
        }
    }

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DnsRow row }) return;
        if (AdapterCombo.SelectedValue is not string alias)
        {
            MessageBox.Show("เลือก network adapter ก่อน", "BoostFPS");
            return;
        }

        var confirm = MessageBox.Show(
            $"ตั้ง DNS บน \"{alias}\" เป็น {row.Name}\n({string.Join(", ", row.Result.Preset.Servers)})?",
            "ยืนยัน", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        var ok = App.Engine.Dns.ApplyPreset(alias, row.Result.Preset);
        App.Engine.Changelog.Add(ok ? "Applied" : "Failed", $"DNS {alias} -> {row.Name}");

        MessageBox.Show(ok ? $"ตั้ง DNS เป็น {row.Name} แล้ว" : "ตั้ง DNS ไม่สำเร็จ",
            "DNS", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

        RefreshCurrent();
    }

    private void Dhcp_Click(object sender, RoutedEventArgs e)
    {
        if (AdapterCombo.SelectedValue is not string alias) return;

        var ok = App.Engine.Dns.ResetToDhcp(alias);
        App.Engine.Changelog.Add(ok ? "Reverted" : "Failed", $"DNS {alias} -> DHCP");
        RefreshCurrent();

        MessageBox.Show(ok ? "คืนค่า DHCP แล้ว" : "คืนไม่สำเร็จ", "DNS");
    }

    private void Flush_Click(object sender, RoutedEventArgs e)
    {
        App.Engine.Dns.FlushCache();
        UpdatedText.Text = $"Flushed at {DateTime.Now:HH:mm:ss}";
    }
}
