namespace Mono.Control.ViewModels;

internal static class Prefs
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mono");
    private static string FilePath => Path.Combine(Dir, "prefs.ini");

    private static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(FilePath)) return d;
        foreach (var line in File.ReadAllLines(FilePath))
        {
            var i = line.IndexOf('=');
            if (i <= 0) continue;
            d[line[..i]] = line[(i + 1)..];
        }
        return d;
    }

    private static void Save(Dictionary<string, string> d)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllLines(FilePath, d.Select(kv => kv.Key + "=" + kv.Value));
    }

    public static string Get(string key, string fallback)
    {
        var d = Load();
        return d.TryGetValue(key, out var v) ? v : fallback;
    }

    public static void Set(string key, string value)
    {
        var d = Load();
        d[key] = value;
        Save(d);
    }

    public static bool GetBool(string key) => Get(key, "") == "1";
    public static void SetBool(string key, bool value) => Set(key, value ? "1" : "0");
}
