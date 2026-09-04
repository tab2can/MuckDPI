using System.Windows;
using Forms = System.Windows.Forms;

namespace MuckDPI.Services;

public static class TrayService
{
    private static Forms.NotifyIcon? _icon;
    private static Window? _window;

    public static void Attach(Window window)
    {
        _window = window;
        _icon = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "MuckDPI",
            Icon = System.Drawing.SystemIcons.Shield
        };
        _icon.DoubleClick += (_, _) => Show();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("Open", "Aç"), null, (_, _) => Show());
        menu.Items.Add(Loc.T("Exit", "Çıkış"), null, (_, _) =>
        {
            _icon!.Visible = false;
            System.Windows.Application.Current.Shutdown();
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
