using System.Windows;
using MuckDPI.Services;

namespace MuckDPI;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(args.Exception.Message, "MuckDPI", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        SettingsStore.Load();
        Loc.Language = SettingsStore.Current.Language;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TrayService.Dispose();
        base.OnExit(e);
    }
}
