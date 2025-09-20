using Dark.Net;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace YouTubeDownloader;

public partial class SettingsWindow : Window
{
    private AppSettings settings = null!;

    public SettingsWindow()
    {
        InitializeComponent();
        try { DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark); } catch { }
        LoadSettingsToUI();
    }

    private void LoadSettingsToUI()
    {
        settings = SettingsManager.Load();

        DownloadDirectoryText.Text = settings.DownloadDirectory;
        AutoOpenCheck.IsChecked = settings.AutoOpenOnComplete;
        AutoUpdateToolsCheck.IsChecked = settings.AutoUpdateTools;
        SingleFolderRadio.IsChecked = !settings.OrganizeByType;
        SubfoldersRadio.IsChecked = settings.OrganizeByType;

        // Set combo selection (1,2,3). Default to 2 if out-of-range.
        int val = settings.ConcurrentDownloads;
        if (val < 1 || val > 3) val = 2;
        ConcurrentDownloadsCombo.SelectedIndex = val - 1; // 0,1,2

        // Tools
        YtDlpPathText.Text = settings.YtDlpPath;
        FfmpegPathText.Text = settings.FfmpegPath;
        UpdateToolButtons();
        _ = RefreshToolStatusAsync();

        // App version label
        try
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                var display = version.Build >= 0 ? version.ToString(3) : version.ToString();
                AppVersionText.Text = $"Version {display}";
            }
            else
            {
                AppVersionText.Text = string.Empty;
            }
        }
        catch { }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Update settings from UI
        settings.DownloadDirectory = DownloadDirectoryText.Text ?? string.Empty;
        settings.AutoOpenOnComplete = AutoOpenCheck.IsChecked == true;
        settings.AutoUpdateTools = AutoUpdateToolsCheck.IsChecked == true;
        settings.OrganizeByType = SubfoldersRadio.IsChecked == true;
        settings.ConcurrentDownloads = (ConcurrentDownloadsCombo.SelectedIndex >= 0 ? ConcurrentDownloadsCombo.SelectedIndex + 1 : 2);
        settings.YtDlpPath = YtDlpPathText.Text?.Trim() ?? string.Empty;
        settings.FfmpegPath = FfmpegPathText.Text?.Trim() ?? string.Empty;

        SettingsManager.Save(settings);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select download directory",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrWhiteSpace(DownloadDirectoryText.Text))
        {
            try { dialog.SelectedPath = DownloadDirectoryText.Text; } catch { }
        }

        var result = dialog.ShowDialog();
        if (result == WinForms.DialogResult.OK)
        {
            DownloadDirectoryText.Text = dialog.SelectedPath;
        }
    }

    private void AutoOpenCheck_Checked(object sender, RoutedEventArgs e)
    {

    }

    private void BrowseYtDlp_Click(object sender, RoutedEventArgs e)
    {
        using var ofd = new WinForms.OpenFileDialog
        {
            Title = "Select yt-dlp.exe",
            Filter = "yt-dlp|yt-dlp.exe|Executable|*.exe|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (ofd.ShowDialog() == WinForms.DialogResult.OK)
        {
            YtDlpPathText.Text = ofd.FileName;
            UpdateToolButtons();
        }
    }

    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        using var ofd = new WinForms.OpenFileDialog
        {
            Title = "Select ffmpeg.exe",
            Filter = "ffmpeg|ffmpeg.exe|Executable|*.exe|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (ofd.ShowDialog() == WinForms.DialogResult.OK)
        {
            FfmpegPathText.Text = ofd.FileName;
            UpdateToolButtons();
        }
    }

    private async void GetUpdateYtDlp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetYtDlpStatus("Checking...");
            var (changed, path) = await ToolsManager.EnsureLatestYtDlpAsync(YtDlpPathText.Text);
            if (!string.IsNullOrEmpty(path)) YtDlpPathText.Text = path;
            SetYtDlpStatus(changed ? "yt-dlp updated." : "yt-dlp is up to date.");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("yt-dlp update failed during manual update.", ex);
            SetYtDlpStatus("yt-dlp update failed: " + ex.Message, isError: true);
        }
        finally
        {
            UpdateToolButtons();
            await RefreshToolStatusAsync();
        }
    }

    private async void GetUpdateFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetFfmpegStatus("Checking...");
            var (changed, path) = await ToolsManager.EnsureFfmpegLatestAsync(FfmpegPathText.Text);
            if (!string.IsNullOrEmpty(path)) FfmpegPathText.Text = path;
            SetFfmpegStatus(changed ? "ffmpeg downloaded/updated." : "ffmpeg is present.");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("ffmpeg update failed during manual update.", ex);
            SetFfmpegStatus("ffmpeg update failed: " + ex.Message, isError: true);
        }
        finally
        {
            UpdateToolButtons();
            await RefreshToolStatusAsync();
        }
    }

    private async Task RefreshToolStatusAsync()
    {
        try
        {
            var (hasUpdate, latest) = await ToolsManager.IsYtDlpUpdateAvailableAsync(YtDlpPathText.Text);
            SetYtDlpStatus(hasUpdate ? "Update Available" : "Up to date");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to check yt-dlp status.", ex);
            SetYtDlpStatus("yt-dlp: " + ex.Message, isError: true);
        }

        try
        {
            var has = await ToolsManager.IsFfmpegUpdateAvailableAsync(FfmpegPathText.Text);
            SetFfmpegStatus(has ? "Update Available" : "Up to date");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to check ffmpeg status.", ex);
            SetFfmpegStatus("ffmpeg: " + ex.Message, isError: true);
        }
    }

    private void SetYtDlpStatus(string text, bool isError = false)
    {
        YtDlpStatusText.Text = text;
        YtDlpStatusText.Foreground = isError ? System.Windows.Media.Brushes.IndianRed : System.Windows.Media.Brushes.LightGray;
    }

    private void SetFfmpegStatus(string text, bool isError = false)
    {
        FfmpegStatusText.Text = text;
        FfmpegStatusText.Foreground = isError ? System.Windows.Media.Brushes.IndianRed : System.Windows.Media.Brushes.LightGray;
    }

    private void UpdateToolButtons()
    {
        GetYtBtn.Content = string.IsNullOrWhiteSpace(YtDlpPathText.Text) || !System.IO.File.Exists(YtDlpPathText.Text)
            ? "Download"
            : "Update";
        GetFfmpegBtn.Content = string.IsNullOrWhiteSpace(FfmpegPathText.Text) || !System.IO.File.Exists(FfmpegPathText.Text)
            ? "Download"
            : "Update";
    }

    private void FfmpegPathText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {

    }


    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = AppLogger.LogDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Failed to open log directory.", ex);
            System.Windows.MessageBox.Show("Failed to open log directory: " + ex.Message);
        }
    }

}

