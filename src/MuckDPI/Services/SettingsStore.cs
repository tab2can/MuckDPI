using MuckDPI.Engine;

namespace MuckDPI.Services;

public static class SettingsStore
{
    public static AppSettings Current { get; private set; } = new();

    public static string Path => AppPaths.SettingsFile;

    public static AppSettings Load()
    {
        Current = SettingsIo.Load();
        return Current;
    }

    public static void Save()
    {
        SettingsIo.Save(Current);
    }
}

public static class Loc
{
    public static string Language { get; set; } = "tr";
    public static bool Tr => Language.StartsWith("tr", StringComparison.OrdinalIgnoreCase);

    public static string T(string en, string tr) => Tr ? tr : en;
}
