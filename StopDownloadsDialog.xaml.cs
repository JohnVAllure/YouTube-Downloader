using Dark.Net;
using System.Windows;

namespace YouTubeDownloader;

public partial class StopDownloadsDialog : Window
{
    public enum Choice { Cancel, StopNow, FinishActive }
    public Choice Result { get; private set; } = Choice.Cancel;

    public StopDownloadsDialog()
    {
        InitializeComponent();
        try { DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark); } catch { }
    }

    private void StopNow_Click(object sender, RoutedEventArgs e)
    {
        Result = Choice.StopNow;
        DialogResult = true;
    }

    private void FinishActive_Click(object sender, RoutedEventArgs e)
    {
        Result = Choice.FinishActive;
        DialogResult = true;
    }
}

