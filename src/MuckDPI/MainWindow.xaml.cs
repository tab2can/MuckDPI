using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MuckDPI.Services;
using MuckDPI.ViewModels;

namespace MuckDPI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        TrayService.Attach(this);
        Loaded += async (_, _) =>
        {
            if (_vm.S.AutoStartEngine)
                await _vm.StartAsync();
        };
    }

    private void NavHome(object sender, RoutedEventArgs e) => _vm.Page = "home";
    private void NavWizard(object sender, RoutedEventArgs e) => _vm.Page = "wizard";
    private void NavServices(object sender, RoutedEventArgs e) => _vm.Page = "services";
    private void NavDns(object sender, RoutedEventArgs e) => _vm.Page = "dns";
    private void NavProbe(object sender, RoutedEventArgs e) => _vm.Page = "probe";
    private void NavLog(object sender, RoutedEventArgs e) => _vm.Page = "log";
    private void NavSettings(object sender, RoutedEventArgs e) => _vm.Page = "settings";

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        if (_vm.MinimizeToTray)
        {
            Hide();
            return;
        }
        _forceClose = true;
        Close();
    }

    private async void OnClosing(object sender, CancelEventArgs e)
    {
        if (!_forceClose && _vm.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        await _vm.OnCloseAsync();
        TrayService.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnStateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _vm.MinimizeToTray)
            Hide();
    }
}
