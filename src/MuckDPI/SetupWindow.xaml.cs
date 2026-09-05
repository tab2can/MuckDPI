using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MuckDPI.ViewModels;

namespace MuckDPI;

public partial class SetupWindow : Window
{
    private readonly SetupViewModel _vm = new();
    private bool _allowClose;

    public SetupWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.Finished += () =>
        {
            _allowClose = true;
            Dispatcher.Invoke(() =>
            {
                Close();
                System.Windows.Application.Current.Shutdown();
            });
        };
        Loaded += async (_, _) => await _vm.RunAsync();
    }

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (!_allowClose && _vm.Busy)
            e.Cancel = true;
    }
}
