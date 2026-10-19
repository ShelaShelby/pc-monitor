using PcMonitor.Core;

namespace PcMonitor.App;

internal static class SettingsStore
{
    public static string FilePath { get; set; } = Path.Combine(Log.Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? AppSettings.FromJson(File.ReadAllText(FilePath)) : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Settings load failed: {ex.Message}");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, settings.ToJson());
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Settings save failed: {ex.Message}");
        }
    }
}
