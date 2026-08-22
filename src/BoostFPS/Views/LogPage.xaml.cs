using System.Windows;
using System.Windows.Controls;

namespace BoostFPS.Views;

public partial class LogPage : Page
{
    public LogPage()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload() =>
        LogGrid.ItemsSource = App.Engine.Changelog.Entries.OrderByDescending(e => e.Timestamp).ToList();

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();
}
