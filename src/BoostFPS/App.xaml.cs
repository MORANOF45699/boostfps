using System.Windows;
using System.Windows.Threading;
using BoostFPS.Core.Services;

namespace BoostFPS;

public partial class App : Application
{
    /// <summary>Built once at startup; every page reads the machine profile and services from here.</summary>
    public static OptimizationEngine Engine { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;
        Engine = OptimizationEngine.Create();
        base.OnStartup(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "BoostFPS - unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
