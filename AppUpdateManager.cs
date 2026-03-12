using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace YouTubeDownloader;

public static class AppUpdateManager
{
    public class AppRelease
    {
        public string Version { get; set; } = string.Empty;
        public string DisplayVersion { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public string? ReleasePageUrl { get; set; }
        public string? Notes { get; set; }
    }

    private static readonly Regex VersionRegex = new(@"\d+(\.\d+)+", RegexOptions.Compiled);



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
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var json = await client.GetStringAsync(feedUrl);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string rawVersion = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(rawVersion) && root.TryGetProperty("name", out var nameElement))
        {
            rawVersion = nameElement.GetString() ?? string.Empty;
        }

        var release = new AppRelease
        {
            DisplayVersion = string.IsNullOrWhiteSpace(rawVersion) ? string.Empty : rawVersion.Trim(),
            Notes = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() : null,
            ReleasePageUrl = root.TryGetProperty("html_url", out var htmlElem) ? htmlElem.GetString() : null
        };

        release.Version = NormalizeVersionString(release.DisplayVersion);
        if (string.IsNullOrWhiteSpace(release.DisplayVersion) && !string.IsNullOrWhiteSpace(release.Version))
        {
            release.DisplayVersion = release.Version;
        }

        if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
        {
            string? download = null;
            foreach (var asset in assetsElem.EnumerateArray())
            {
                if (!asset.TryGetProperty("browser_download_url", out var urlElem)) continue;
                var url = urlElem.GetString();
                if (string.IsNullOrWhiteSpace(url)) continue;

                string? assetName = asset.TryGetProperty("name", out var nameElem) ? nameElem.GetString() : null;
                if (assetName != null && (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)))
                {
                    download = url;
                    break;
                }

                download ??= url;
            }

            release.DownloadUrl = download;
        }

        if (string.IsNullOrWhiteSpace(release.DownloadUrl))
            release.DownloadUrl = release.ReleasePageUrl;

        return release;
    }


    private static string NormalizeVersionString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var version = input.Trim();
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            version = version.Substring(1).Trim();

        var match = VersionRegex.Match(version);
        if (match.Success)
            return match.Value;

        var cutoffIndex = version.IndexOfAny(new[] { '-', ' ', '+' });
        if (cutoffIndex > 0)
            version = version.Substring(0, cutoffIndex);
        return version;
    }
    public static string FormatVersion(Version version)
    {
        if (version == null) return string.Empty;
        // Minor is always non-negative; Build/Revision are -1 when unspecified
        var parts = new List<int> { version.Major, version.Minor };
        if (version.Build >= 0) parts.Add(version.Build);
        if (version.Revision >= 0) parts.Add(version.Revision);

        for (int i = parts.Count - 1; i > 0; i--)
        {
            if (parts[i] == 0)
                parts.RemoveAt(i);
            else
                break;
        }

        return string.Join('.', parts);
    }



    public static bool IsNewer(Version current, string latest)
    {
        var normalized = NormalizeVersionString(latest);
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        if (!Version.TryParse(normalized, out var lv)) return false;
        return lv > current;
    }
}

