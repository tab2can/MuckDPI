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
        TrayService.Attach(this, _vm);
        Loaded += async (_, _) =>
        {
            if (_vm.S.AutoStartEngine && !_vm.IsRunning)
                await _vm.StartAsync();
        };
    }

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
        if (!_forceClose && !TrayService.ExitRequested && _vm.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _forceClose = true;
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
