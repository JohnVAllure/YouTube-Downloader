using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YouTubeDownloader;

public class AppSettings
{
    public string DownloadDirectory { get; set; } = string.Empty;
    public bool OrganizeByType { get; set; } = true;
    public bool AutoOpenOnComplete { get; set; } = true;
    public bool AutoUpdateTools { get; set; } = true;
    public int ConcurrentDownloads { get; set; } = 2; // 1,2,3
    public string YtDlpPath { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = string.Empty;
    public bool AutoCheckAppUpdates { get; set; } = true;
    public string AppUpdateFeedUrl { get; set; } = string.Empty; // JSON feed with { version, downloadUrl }
    public bool ConvertVideoToMp4 { get; set; } = false;
    public bool HasCompletedFirstRun { get; set; } = false;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

public static class SettingsManager
{
    private static string SettingsFilePath => Path.Combine(AppPaths.AppDataDirectory, "settings.json");

    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static AppSettings? _cached;

    public static AppSettings Load()
    {
        if (_cached != null) return _cached.Clone();

        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var obj = JsonSerializer.Deserialize<AppSettings>(json);
                if (obj != null)
                {
                    if (string.IsNullOrWhiteSpace(obj.AppUpdateFeedUrl))
                        obj.AppUpdateFeedUrl = "https://api.github.com/repos/JohnVAllure/YouTube-Downloader/releases/latest";
                    _cached = obj;
                    return _cached.Clone();
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to load settings file. Falling back to defaults.", ex);
        }

        // Defaults
        var def = new AppSettings
        {
            // Tools default to blank; user must download or browse
            YtDlpPath = string.Empty,
            FfmpegPath = string.Empty,
            AppUpdateFeedUrl = "https://api.github.com/repos/JohnVAllure/YouTube-Downloader/releases/latest",
            ConvertVideoToMp4 = false
        };
        try
        {
            var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(downloads))
            {
                def.DownloadDirectory = Path.Combine(downloads, "Downloads", "YouTubeDownloader");
            }
        }
        catch { }

        _cached = def;
        return _cached.Clone();
    }

    public static void Save(AppSettings settings)
    {
        _cached = settings.Clone();
        var json = JsonSerializer.Serialize(settings, SaveOptions);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to save settings file.", ex);
        }
    }
}
