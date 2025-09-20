using Dark.Net;
using System;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace YouTubeDownloader;

public partial class ItemSettingsWindow : Window
{
    private readonly DownloadItem item;

    public ItemSettingsWindow(DownloadItem item)
    {
        InitializeComponent();
        try { DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark); } catch { }
        this.item = item;

        // Load current values
        UrlText.Text = item.Url;
        StartText.Text = item.StartTime;
        EndText.Text = item.EndTime;

        switch (item.DownloadType)
        {
            case "Full Video": RbFullVideo.IsChecked = true; break;
            case "Video Segment": RbVideoSegment.IsChecked = true; break;
            case "Full Audio": RbFullAudio.IsChecked = true; break;
            case "Audio Segment": RbAudioSegment.IsChecked = true; break;
            default: RbFullVideo.IsChecked = true; break;
        }

        UpdateSegmentInputsVisibility();

        RbFullVideo.Checked += (_, __) => UpdateSegmentInputsVisibility();
        RbVideoSegment.Checked += (_, __) => UpdateSegmentInputsVisibility();
        RbFullAudio.Checked += (_, __) => UpdateSegmentInputsVisibility();
        RbAudioSegment.Checked += (_, __) => UpdateSegmentInputsVisibility();
    }

    private void UpdateSegmentInputsVisibility()
    {
        bool seg = RbVideoSegment.IsChecked == true || RbAudioSegment.IsChecked == true;
        StartText.IsEnabled = seg;
        EndText.IsEnabled = seg;
        StartText.Opacity = seg ? 1.0 : 0.5;
        EndText.Opacity = seg ? 1.0 : 0.5;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Persist edits back to item
        var url = (UrlText.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("URL cannot be empty.");
            return;
        }

        item.Url = url;
        item.DisplayTitle = url; // keep simple for now

        if (RbFullVideo.IsChecked == true) item.DownloadType = "Full Video";
        else if (RbVideoSegment.IsChecked == true) item.DownloadType = "Video Segment";
        else if (RbFullAudio.IsChecked == true) item.DownloadType = "Full Audio";
        else if (RbAudioSegment.IsChecked == true) item.DownloadType = "Audio Segment";

        // Validate segment times if needed
        bool seg = item.DownloadType.Contains("Segment", StringComparison.OrdinalIgnoreCase);
        if (seg)
        {
            string s = string.IsNullOrWhiteSpace(StartText.Text) ? "00:00:00" : StartText.Text.Trim();
            string e2 = string.IsNullOrWhiteSpace(EndText.Text) ? "00:00:01" : EndText.Text.Trim();
            if (!TimeSpan.TryParse(s, out _) || !TimeSpan.TryParse(e2, out _))
            {
                MessageBox.Show("Please enter valid times as HH:MM:SS.");
                return;
            }
            item.StartTime = s;
            item.EndTime = e2;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        item.Status = "Queued";
        item.Progress = 0;
        item.RetryVisible = Visibility.Collapsed;
        item.ErrorVisible = Visibility.Collapsed;
        item.OpenVisible = Visibility.Collapsed;
        item.ErrorMessage = string.Empty;
        item.FilePath = string.Empty;
        MessageBox.Show("Item reset to Queued.");
    }
}
