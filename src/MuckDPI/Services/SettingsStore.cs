using System.IO;
using System.Text.Json;
using MuckDPI.Engine;

namespace MuckDPI.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Current { get; private set; } = new();

    public static string Path
    {
        get
        {
            var muck = Environment.GetEnvironmentVariable("MUCK_SETTINGS_PATH");
            if (!string.IsNullOrWhiteSpace(muck)) return muck;
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MuckDPI");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var json = File.ReadAllText(Path);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
        }
        catch
        {
            Current = new AppSettings();
        }
        return Current;
    }

    public static void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(Current, JsonOpts));
    }
}

public static class Loc
{
    public static string Language { get; set; } = "tr";
    public static bool Tr => Language.StartsWith("tr", StringComparison.OrdinalIgnoreCase);

    public static string T(string en, string tr) => Tr ? tr : en;
}
