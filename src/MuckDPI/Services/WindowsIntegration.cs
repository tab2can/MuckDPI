using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using MuckDPI.Engine;

namespace MuckDPI.Services;

public static class WindowsIntegration
{
    public const string ServiceName = "MuckDPI";
    public const string TaskName = "MuckDPI";

    public static string ServiceExe => Path.Combine(AppContext.BaseDirectory, "MuckDPI.Service.exe");
    public static string GuiExe => Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "MuckDPI.exe");

    public static bool ServiceExeExists => File.Exists(ServiceExe);

    public static bool IsServiceRunning
    {
        get
        {
            try
            {
                using var sc = Open();
                return sc?.Status == ServiceControllerStatus.Running;
            }
            catch { return false; }
        }
    }

    public static bool IsInstalled
    {
        get
        {
            try { using var sc = Open(); return sc is not null; }
            catch { return false; }
        }
    }

    public static void Install()
    {
        if (!ServiceExeExists)
            throw new InvalidOperationException("MuckDPI.Service.exe bulunamadı. Programı yeniden derleyin.");

        Directory.CreateDirectory(AppPaths.DataDir);

        if (!IsInstalled)
        {
            var created = Run("sc.exe",
                "create", ServiceName,
                "binPath=", ServiceExe,
                "start=", "delayed-auto",
                "DisplayName=", "MuckDPI Protection");
            if (created != 0 && created != 1073)
                throw new InvalidOperationException($"Windows servisi kurulamadı (sc {created}).");
        }
        else
        {
            Run("sc.exe", "config", ServiceName, "binPath=", ServiceExe);
            Run("sc.exe", "config", ServiceName, "start=", "delayed-auto");
        }

        Run("sc.exe", "description", ServiceName, "Türkiye DPI koruması. WinDivert motoru, oturum açmadan çalışır.");
        Run("sc.exe", "failure", ServiceName, "reset=", "0", "actions=", "//");
        Run("sc.exe", "failureflag", ServiceName, "0");
        Run("schtasks.exe", "/Delete", "/TN", TaskName, "/F");
    }

    public static void Uninstall()
    {
        Stop();
        Run("sc.exe", "config", ServiceName, "start=", "disabled");
        Run("sc.exe", "delete", ServiceName);
        Run("schtasks.exe", "/Delete", "/TN", TaskName, "/F");
        try { File.Delete(AppPaths.StatusFile); } catch { /* ignore */ }
    }

    public static void Start()
    {
        if (!IsInstalled) Install();
        using var sc = Open() ?? throw new InvalidOperationException("MuckDPI servisi yok.");
        if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
        {
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
            return;
        }
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
    }

    public static void Stop()
    {
        try
        {
            using var sc = Open();
            if (sc is null) return;
            if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
            {
                try { sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15)); }
                catch { /* ignore */ }
                return;
            }
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
        catch
        {
            // already gone
        }
    }

    private static ServiceController? Open()
    {
        try
        {
            var sc = new ServiceController(ServiceName);
            _ = sc.Status;
            return sc;
        }
        catch
        {
            return null;
        }
    }

    private static int Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null) return -1;
        p.WaitForExit(30_000);
        return p.ExitCode;
    }
}
