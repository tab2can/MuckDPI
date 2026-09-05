using System.Threading;
using System.Windows;
using MuckDPI.Engine;
using MuckDPI.Services;

namespace MuckDPI;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = @"Global\MuckDPI.Gui";
        _mutex = new Mutex(true, mutexName, out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(args.Exception.Message, "MuckDPI", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        SettingsStore.Load();
        Loc.Language = SettingsStore.Current.Language;
        var forceTest = e.Args.Any(a =>
            a.Equals("-test", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--test", StringComparison.OrdinalIgnoreCase));

        if (!forceTest && SettingsStore.Current.TuneCompleted)
        {
            try
            {
                WindowsIntegration.Install();
                if (!WindowsIntegration.IsServiceRunning)
                    WindowsIntegration.Start();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "MuckDPI", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }

        var setup = new SetupWindow();
        MainWindow = setup;
        setup.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
