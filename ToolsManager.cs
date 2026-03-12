using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace YouTubeDownloader;

public static class ToolsManager
{
    // Official yt-dlp latest binary URL
    private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    // FFmpeg latest build (zip) from BtbN GitHub builds (GPL win64)
    private const string YtDlpLatestApi = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
    private const string FfmpegBuildsLatestApi = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest";
    private const string FfmpegFallbackZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    private static string GetAppDataDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "YouTubeDownloader", "bin");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // For ffmpeg, provide a simple presence check and a helper to fetch a minimal ffmpeg.exe
    // Users often manage ffmpeg via package managers; downloading an EXE directly varies by provider.
    // To keep UX simple, we fetch from Gyan.dev latest release (stable) single-archive and extract ffmpeg.exe in future.
    // For now, EnsureFfmpegPresentAsync creates a placeholder path and instructs the user if download is needed.

    public static async Task<(bool changed, string path)> EnsureLatestYtDlpAsync(string desiredPath)
    {
        // Decide install root
        string root = Path.Combine(GetAppDataDir(), "yt-dlp");
        Directory.CreateDirectory(root);
        string version = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string versionDir = Path.Combine(root, version);
        Directory.CreateDirectory(versionDir);
        string destExe = Path.Combine(versionDir, "yt-dlp.exe");

        // If exists, compare versions by --version value vs remote HEAD ETag (best effort) or just replace if older.
        // Simpler approach: always download to temp and replace if different bytes.
        var tmp = Path.Combine(Path.GetTempPath(), "YouTubeDownloader", "yt-dlp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var tmpExe = Path.Combine(tmp, "yt-dlp.exe");
        using (var resp = await SharedClient.GetAsync(YtDlpUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(tmpExe);
            await resp.Content.CopyToAsync(fs);
        }

        bool changed = await ReplaceIfDifferentAsync(tmpExe, destExe);
        TryMakeExecutable(destExe);

        // Update path: prefer versioned path to avoid in-place overwrite of active file
        string finalPath = destExe;
        try { CleanupOldVersions(Path.Combine(GetAppDataDir(), "yt-dlp"), finalPath); } catch { }
        return (changed, finalPath);
    }

    public static async Task<string> GetYtDlpVersionAsync(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        return await GetProcessStdoutAsync(path, "--version");
    }

    public static async Task<(bool hasUpdate, string latest)> IsYtDlpUpdateAvailableAsync(string currentPath)
    {
        try
        {
            var currentVer = (await GetYtDlpVersionAsync(currentPath)).Trim();
            var latest = await GetLatestYtDlpVersionAsync();
            if (string.IsNullOrWhiteSpace(latest)) return (false, "");
            if (string.IsNullOrWhiteSpace(currentVer)) return (true, latest);

            // yt-dlp versions are typically yyyy.MM.dd
            if (DateTime.TryParse(currentVer, out var cv) && DateTime.TryParse(latest, out var lv))
                return (lv > cv, latest);
            return (!string.Equals(currentVer, latest, StringComparison.OrdinalIgnoreCase), latest);
        }
        catch { return (false, ""); }
    }

    public static async Task<string> GetLatestYtDlpVersionAsync()
    {
        try
        {
            var json = await SharedClient.GetStringAsync(YtDlpLatestApi);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tag_name", out var tag))
                return tag.GetString() ?? string.Empty;
            if (doc.RootElement.TryGetProperty("name", out var name))
                return name.GetString() ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    private static async Task<string> ResolveFfmpegDownloadUrlAsync()
    {
        try
        {
            var json = await SharedClient.GetStringAsync(FfmpegBuildsLatestApi);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameElem) && asset.TryGetProperty("browser_download_url", out var urlElem))
                    {
                        var name = nameElem.GetString() ?? string.Empty;
                        if (name.Contains("win64", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            var url = urlElem.GetString();
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                return url;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("Failed to resolve ffmpeg download URL from GitHub. Falling back to secondary mirror.");
            AppLogger.LogError("ffmpeg download URL resolution error.", ex);
        }

        return FfmpegFallbackZipUrl;
    }

    public static async Task<(bool changed, string path)> EnsureFfmpegLatestAsync(string desiredPath)
    {
        string root = Path.Combine(GetAppDataDir(), "ffmpeg");
        Directory.CreateDirectory(root);
        string version = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string versionDir = Path.Combine(root, version);
        Directory.CreateDirectory(versionDir);

        // staging dirs
        string stage = Path.Combine(Path.GetTempPath(), "YouTubeDownloader", "ffmpeg", Guid.NewGuid().ToString("N"));
        string zipPath = Path.Combine(stage, "download.zip");
        string extractDir = Path.Combine(stage, "extracted");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(extractDir);

        string downloadUrl = await ResolveFfmpegDownloadUrlAsync();
        AppLogger.LogInfo($"Downloading ffmpeg package from {downloadUrl}.");

        try
        {
            using (var resp = await SharedClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(zipPath);
                await resp.Content.CopyToAsync(fs);
            }

            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var ffmpegExe = Directory.EnumerateFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (ffmpegExe == null)
            {
                AppLogger.LogError("Downloaded ffmpeg archive did not contain ffmpeg.exe.");
                throw new Exception("ffmpeg.exe not found in archive.");
            }

            // If the current ffmpeg is identical to what we just downloaded, skip the install
            if (!string.IsNullOrWhiteSpace(desiredPath) && File.Exists(desiredPath))
            {
                var newHash = await GetHashAsync(ffmpegExe);
                var curHash = await GetHashAsync(desiredPath);
                if (newHash.Equals(curHash, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.LogInfo("ffmpeg is already up to date.");
                    return (false, desiredPath);
                }
            }

            var binDir = Path.GetDirectoryName(ffmpegExe)!;
            CopyDirectory(binDir, versionDir);

            var destFfmpeg = Path.Combine(versionDir, "ffmpeg.exe");
            TryMakeExecutable(destFfmpeg);
            try { CleanupOldVersions(root, destFfmpeg); } catch { }
            return (true, destFfmpeg);
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to download or extract ffmpeg from {downloadUrl}.", ex);
            throw;
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); } catch { }
        }
    }

    public static async Task<string> GetFfmpegVersionAsync(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        var outText = await GetProcessStdoutAsync(path, "-version");
        // First line contains version
        using var reader = new StringReader(outText);
        return reader.ReadLine() ?? outText;
    }

    public static async Task<bool> IsFfmpegUpdateAvailableAsync(string currentPath)
    {
        try
        {
            // Determine current install timestamp from versioned folder (yyyyMMdd_HHmmss)
            var dir = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return true;
            var currentStamp = Path.GetFileName(dir);
            DateTime current;
            if (!DateTime.TryParseExact(currentStamp, "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.AssumeUniversal, out current))
            {
                // If not in our versioned layout, fall back to no update info
                return false;
            }

            var json = await SharedClient.GetStringAsync(FfmpegBuildsLatestApi);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("published_at", out var published))
            {
                if (DateTime.TryParse(published.GetString(), out var remote))
                {
                    return remote.ToUniversalTime() > current.ToUniversalTime();
                }
            }
        }
        catch { }
        return false;
    }

    private static readonly HttpClient SharedClient = CreateSharedClient();
    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeDownloader/1.0");
        return client;
    }
    private static HttpClient MakeHttpClient() => SharedClient;

    private static string NormalizeTargetPath(string desiredPath, string defaultFileName)
    {
        if (string.IsNullOrWhiteSpace(desiredPath))
        {
            var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
            return Path.Combine(toolsDir, defaultFileName);
        }

        if (Directory.Exists(desiredPath))
            return Path.Combine(desiredPath, defaultFileName);

        // If it looks like a directory (ends with slash), ensure as directory
        if (desiredPath.EndsWith(Path.DirectorySeparatorChar) || desiredPath.EndsWith(Path.AltDirectorySeparatorChar))
        {
            Directory.CreateDirectory(desiredPath);
            return Path.Combine(desiredPath, defaultFileName);
        }

        // Otherwise assume it's a file path
        var dir = Path.GetDirectoryName(desiredPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return desiredPath;
    }

    private static async Task<bool> ReplaceIfDifferentAsync(string sourceFile, string destFile)
    {
        try
        {
            if (File.Exists(destFile))
            {
                var srcHash = await GetHashAsync(sourceFile);
                var dstHash = await GetHashAsync(destFile);
                if (srcHash.Equals(dstHash, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(sourceFile);
                    return false;
                }
            }

            // Replace
            if (File.Exists(destFile)) File.Delete(destFile);
            File.Move(sourceFile, destFile);
            return true;
        }
        finally
        {
            try { if (File.Exists(sourceFile)) File.Delete(sourceFile); } catch { }
        }
    }

    private static async Task<string> GetHashAsync(string filePath)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        await using var fs = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(fs);
        return Convert.ToHexString(hash);
    }

    private static void TryMakeExecutable(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
        }
        catch { }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var destSub = Path.Combine(destinationDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSub);
        }
    }

    private static void CleanupOldVersions(string toolRoot, string keepExePath)
    {
        try
        {
            var keepDir = Path.GetDirectoryName(keepExePath)!;
            if (!Directory.Exists(toolRoot)) return;
            foreach (var dir in Directory.EnumerateDirectories(toolRoot))
            {
                try
                {
                    if (string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar), keepDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Attempt delete; on failure, schedule for deletion on reboot
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch
                    {
                        // schedule files for deletion; then try remove dir
                        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                        {
                            NativeWin32.ScheduleDeleteOnReboot(file);
                        }
                        try { Directory.Delete(dir, recursive: true); } catch { }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    public static void CleanupWithSettings(AppSettings settings)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.YtDlpPath))
            {
                var root = Path.Combine(GetAppDataDir(), "yt-dlp");
                CleanupOldVersions(root, settings.YtDlpPath);
            }
        }
        catch { }
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.FfmpegPath))
            {
                var root = Path.Combine(GetAppDataDir(), "ffmpeg");
                CleanupOldVersions(root, settings.FfmpegPath);
            }
        }
        catch { }
    }

    private static async Task<string> GetProcessStdoutAsync(string exe, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using var proc = new Process { StartInfo = psi };
        proc.Start();
        // Read both streams concurrently to avoid deadlock when one buffer fills
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await Task.Run(() => proc.WaitForExit());
        string output = await stdoutTask;
        string error = await stderrTask;
        return string.IsNullOrWhiteSpace(output) ? error : output;
    }
}