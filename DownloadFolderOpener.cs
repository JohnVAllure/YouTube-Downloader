using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace YouTubeDownloader;

public static class DownloadFolderOpener
{
    private static readonly HashSet<string> OpenedThisBatch = new(StringComparer.OrdinalIgnoreCase);

    public static void ResetBatch()
    {
        OpenedThisBatch.Clear();
    }

    // Opens Windows Explorer to the folder (or selects the file) once per batch per folder
    public static void OpenOnce(AppSettings settings, string? filePath, string downloadType)
    {
        if (settings == null || settings.AutoOpenOnComplete == false)
            return;

        string folder;
        bool canSelectFile = !string.IsNullOrWhiteSpace(filePath);
        if (canSelectFile)
        {
            try { folder = Path.GetDirectoryName(filePath!) ?? string.Empty; }
            catch { folder = string.Empty; }
        }
        else
        {
            folder = ComputeTargetFolder(settings, downloadType);
        }

        if (string.IsNullOrWhiteSpace(folder)) return;
        if (OpenedThisBatch.Contains(folder)) return;

        var targetDescription = canSelectFile && !string.IsNullOrWhiteSpace(filePath) ? filePath! : folder;

        try
        {
            Directory.CreateDirectory(folder);

            ProcessStartInfo psi;
            if (canSelectFile && File.Exists(filePath))
            {
                // Highlight the completed file
                psi = new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
                {
                    UseShellExecute = true
                };
            }
            else
            {
                // Open the target folder
                psi = new ProcessStartInfo("explorer.exe", $"\"{folder}\"")
                {
                    UseShellExecute = true
                };
            }

            Process.Start(psi);
            OpenedThisBatch.Add(folder);
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to open download location '{targetDescription}'.", ex);
        }
    }

    public static string ComputeTargetFolder(AppSettings settings, string downloadType)
    {
        var root = settings.DownloadDirectory;
        if (string.IsNullOrWhiteSpace(root)) return string.Empty;
        if (!settings.OrganizeByType) return root;

        string sub = downloadType switch
        {
            "Full Video" => "Video",
            "Video Segment" => "Partial Video",
            "Full Audio" => "Audio",
            "Audio Segment" => "Partial Audio",
            _ => "Other"
        };
        return Path.Combine(root, sub);
    }
}

