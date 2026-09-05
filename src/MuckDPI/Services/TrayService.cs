using System.Windows;
using Forms = System.Windows.Forms;
using MuckDPI.ViewModels;

namespace MuckDPI.Services;

public static class TrayService
{
    private static Forms.NotifyIcon? _icon;
    private static Window? _window;
    private static MainViewModel? _vm;
    public static bool ExitRequested { get; set; }

    public static void Attach(Window window, MainViewModel vm)
    {
        _window = window;
        _vm = vm;
        _icon = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "MuckDPI",
            Icon = System.Drawing.SystemIcons.Shield
        };
        _icon.DoubleClick += (_, _) => Show();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("Open", "Aç"), null, (_, _) => Show());
        menu.Items.Add(Loc.T("Start protection", "Korumayı başlat"), null, async (_, _) =>
        {
            if (_vm is not null) await _vm.StartAsync();
        });
        menu.Items.Add(Loc.T("Stop protection", "Korumayı durdur"), null, async (_, _) =>
        {
            if (_vm is not null) await _vm.StopAsync();
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Exit (keep protection)", "Çıkış (koruma açık kalsın)"), null, (_, _) =>
        {
            ExitRequested = true;
            _icon!.Visible = false;
            _window?.Close();
        });
        _icon.ContextMenuStrip = menu;
    }

    public static void ShowBalloon(string title, string text)
    {
        _icon?.ShowBalloonTip(2500, title, text, Forms.ToolTipIcon.Info);
    }

    public static void Show()
    {
        if (_window is null) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public static void Dispose()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}
