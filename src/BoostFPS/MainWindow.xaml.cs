using System.Windows;
using System.Windows.Controls;
using BoostFPS.Views;

namespace BoostFPS;

public partial class MainWindow : Window
{
    // Pages are cached so navigating away and back keeps scroll position and selections.
    private readonly Dictionary<string, Page> _pages = [];

    public MainWindow()
    {
        InitializeComponent();

        var m = App.Engine.Machine;
        MachineText.Text = $"{m.CpuName}\n{m.GpuName}\n{m.WindowsCaption} ({m.WindowsBuild})";

        Navigate("Dashboard");
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && ContentFrame is null) return;
        if (sender is RadioButton { Tag: string tag }) Navigate(tag);
    }

    private void Navigate(string tag)
    {
        if (ContentFrame is null) return;

        if (!_pages.TryGetValue(tag, out var page))
        {
            page = tag switch
            {
                "Tweaks" => new TweaksPage(),
                "Services" => new ServicesPage(),
                "Nvidia" => new NvidiaPage(),
                "Gpo" => new GroupPolicyPage(),
                "NetworkPower" => new NetworkPowerPage(),
                "Cleaner" => new CleanerPage(),
                "Dns" => new DnsPage(),
                "Games" => new GamesPage(),
                "Baseline" => new BaselinePage(),
                "Backup" => new BackupPage(),
                "Log" => new LogPage(),
                _ => new DashboardPage()
            };
            _pages[tag] = page;
        }

        ContentFrame.Navigate(page);
    }
}
