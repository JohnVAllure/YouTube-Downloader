using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace YouTubeDownloader;

public static class AppUpdateManager
{
    public class AppRelease
    {
        public string Version { get; set; } = "";
        public string? DownloadUrl { get; set; }
        public string? Notes { get; set; }
    }

    public static Version GetCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (Version.TryParse(infoVer, out var v)) return v;
        return asm.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    public static async Task<AppRelease?> GetLatestAsync(string feedUrl)
    {
        if (string.IsNullOrWhiteSpace(feedUrl)) return null;
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeDownloader/1.0");
        var json = await client.GetStringAsync(feedUrl);
        var release = JsonSerializer.Deserialize<AppRelease>(json);
        return release;
    }

    public static bool IsNewer(Version current, string latest)
    {
        if (!Version.TryParse(latest, out var lv)) return false;
        return lv > current;
    }
}

