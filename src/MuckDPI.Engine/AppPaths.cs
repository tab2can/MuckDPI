using System.Text.Json;

namespace MuckDPI.Engine;

public static class AppPaths
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string DataDir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("MUCK_SETTINGS_PATH");
            if (!string.IsNullOrWhiteSpace(env))
            {
                var dir = Path.GetDirectoryName(env);
                if (!string.IsNullOrWhiteSpace(dir)) return dir;
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MuckDPI");
        }
    }

    public static string SettingsFile
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("MUCK_SETTINGS_PATH");
            if (!string.IsNullOrWhiteSpace(env)) return env;
            return Path.Combine(DataDir, "settings.json");
        }
    }

    public static string StatusFile => Path.Combine(DataDir, "status.json");
    public static string ReloadFlag => Path.Combine(DataDir, "reload");
    public static string LegacySettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MuckDPI", "settings.json");
}

public static class SettingsIo
{
    public static AppSettings Load()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            MigrateLegacy();
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var current = JsonSerializer.Deserialize<AppSettings>(json, AppPaths.Json) ?? new AppSettings();
                if (current.SettingsVersion < 5)
                {
                    current.FilterMode = FilterMode.Global;
                    current.DnsProvider = "yandex";
                    current.EnableDnsProtect = true;
                    current.EnablePassiveDrop = true;
                    current.QuicMode = QuicMode.BlockAll;
                    current.WindowsIntegrate = true;
                    current.AutoStartEngine = true;
                    current.MinimizeToTray = true;
                    if (current.StrategyId is "auto" or "" or "split-sni" or "split-reverse" or "tiny-frag")
                        current.StrategyId = "turkey";
                    current.SettingsVersion = 5;
                    Save(current);
                }
                return current;
            }
        }
        catch
        {
            // fall through
        }
        return new AppSettings();
    }

    private static readonly object SaveLock = new();

    public static void Save(AppSettings settings)
    {
        lock (SaveLock)
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, AppPaths.Json));
            File.Copy(tmp, AppPaths.SettingsFile, overwrite: true);
            File.Delete(tmp);
        }
    }

    public static void RememberHardHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        lock (SaveLock)
        {
            var s = LoadUnlocked();
            if (s.LearnedHardHosts.Any(h => h.Equals(host, StringComparison.OrdinalIgnoreCase)))
                return;
            s.LearnedHardHosts.Add(host);
            Directory.CreateDirectory(AppPaths.DataDir);
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, AppPaths.Json));
            File.Copy(tmp, AppPaths.SettingsFile, overwrite: true);
            File.Delete(tmp);
        }
    }

    private static AppSettings LoadUnlocked()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return new AppSettings();
            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, AppPaths.Json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void RequestReload()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        File.WriteAllText(AppPaths.ReloadFlag, DateTime.UtcNow.ToString("O"));
    }

    public static bool ConsumeReload()
    {
        try
        {
            if (!File.Exists(AppPaths.ReloadFlag)) return false;
            File.Delete(AppPaths.ReloadFlag);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void WriteStatus(EngineStatusDto dto)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            var tmp = AppPaths.StatusFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(dto, AppPaths.Json));
            File.Copy(tmp, AppPaths.StatusFile, overwrite: true);
            File.Delete(tmp);
        }
        catch
        {
            // status is best-effort
        }
    }

    public static EngineStatusDto? ReadStatus()
    {
        try
        {
            if (!File.Exists(AppPaths.StatusFile)) return null;
            var json = File.ReadAllText(AppPaths.StatusFile);
            return JsonSerializer.Deserialize<EngineStatusDto>(json, AppPaths.Json);
        }
        catch
        {
            return null;
        }
    }

    private static void MigrateLegacy()
    {
        if (File.Exists(AppPaths.SettingsFile)) return;
        if (!File.Exists(AppPaths.LegacySettingsFile)) return;
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.Copy(AppPaths.LegacySettingsFile, AppPaths.SettingsFile, overwrite: false);
        }
        catch
        {
            // ignore
        }
    }
}
