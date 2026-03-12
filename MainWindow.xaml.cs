using Dark.Net;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

// Resolve ambiguous types when WinForms is referenced
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using RichTextBox = System.Windows.Controls.RichTextBox;
using ScrollViewer = System.Windows.Controls.ScrollViewer;

namespace YouTubeDownloader
{

    public partial class MainWindow : Window
    {
        private ObservableCollection<DownloadItem> downloadQueue = new ObservableCollection<DownloadItem>();
        private void PasteLinkButton_Click(object sender, RoutedEventArgs e)
        {
            PasteLinkFromClipboard();
        }

        private void AddLinksButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AddLinksWindow { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                foreach (var entry in dlg.LinkEntries)
                {
                    AddLinkToQueue(entry.Url);
                }
            }
        }

        private async void StartDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading)
            {
                var dlg = new StopDownloadsDialog { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    if (dlg.Result == StopDownloadsDialog.Choice.StopNow)
                    {
                        _stopAfterActive = false;
                        _downloadCts?.Cancel();
                    }
                    else if (dlg.Result == StopDownloadsDialog.Choice.FinishActive)
                    {
                        _stopAfterActive = true; // do not schedule new items
                    }
                }
                return;
            }

            // Start a new batch: ensure folders will open at most once per target
            DownloadFolderOpener.ResetBatch();
            await StartDownloadsAsync();
        }
        private void OpenItemSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DownloadItem di)
            {
                var dlg = new ItemSettingsWindow(di) { Owner = this };
                dlg.ShowDialog();
            }
        }
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // If invoked from a list item, DataContext is DownloadItem
            if ((sender as Button)?.DataContext is DownloadItem di)
            {
                var dlg = new ItemSettingsWindow(di) { Owner = this };
                dlg.ShowDialog();
            }
            else
            {
                // Top-right settings button
                var dlg = new SettingsWindow { Owner = this };
                dlg.ShowDialog();
            }
        }

        private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
            { "Preparing", "Downloading", "Converting to MP4" };

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ClearDialog { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                if (dialog.SelectedOption == ClearDialog.ClearOption.Completed)
                {
                    var filtered = downloadQueue.Where(d => d.Status != "Completed").ToList();
                    downloadQueue.Clear();
                    foreach (var item in filtered)
                        downloadQueue.Add(item);
                }
                else if (dialog.SelectedOption == ClearDialog.ClearOption.All)
                {
                    // Leave any actively downloading items in the queue so their tasks can finish cleanly
                    var toRemove = downloadQueue.Where(d => !ActiveStatuses.Contains(d.Status)).ToList();
                    foreach (var item in toRemove)
                        downloadQueue.Remove(item);
                }

                for (int i = 0; i < downloadQueue.Count; i++)
                    downloadQueue[i].RowNumber = i + 1;

                UpdateEmptyHintVisibility();
            }
        }

        private async void Retry_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not DownloadItem item) return;
            item.Status = "Queued";
            item.Progress = 0;
            item.ErrorMessage = string.Empty;
            item.RetryVisible = Visibility.Collapsed;
            item.ErrorVisible = Visibility.Collapsed;
            item.OpenVisible = Visibility.Collapsed;
            if (!_isDownloading)
                await StartDownloadsAsync();
        }

        private void ShowError_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is DownloadItem item && !string.IsNullOrWhiteSpace(item.ErrorMessage))
                MessageBox.Show(item.ErrorMessage, "Download Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            // Open folder for the associated item (if available)
            if ((sender as Button)?.DataContext is DownloadItem item)
            {
                var settings = SettingsManager.Load();
                DownloadFolderOpener.OpenOnce(settings, item.FilePath, item.DownloadType);
            }
        }

        // Call this from your actual download completion logic per item
        private void OnDownloadCompleted(DownloadItem item)
        {
            var settings = SettingsManager.Load();
            DownloadFolderOpener.OpenOnce(settings, item.FilePath, item.DownloadType);
        }

        private void OpenDownloadsFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsManager.Load();
            var folder = settings.DownloadDirectory;
            if (string.IsNullOrWhiteSpace(folder))
            {
                MessageBox.Show("Please set a Download Directory in Settings.");
                return;
            }
            try
            {
                Directory.CreateDirectory(folder);
                var psi = new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to open downloads folder '{folder}'.", ex);
                MessageBox.Show("Failed to open folder: " + ex.Message);
            }
        }
        private void DownloadModeChanged(object sender, RoutedEventArgs e)
        {
            bool isSegment = VideoSegmentRadio.IsChecked == true || AudioSegmentRadio.IsChecked == true;

            StartTimeBox.IsEnabled = isSegment;
            EndTimeBox.IsEnabled = isSegment;

            SegmentTimePanel.Opacity = isSegment ? 1.0 : 0.0;

            if (isSegment)
                SegmentTimeBox_TextChanged(this, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));  // Force update
            else
                SegmentDurationText.Text = ""; 
        }


        private (string downloadType, string startTime, string endTime) GetCurrentDownloadSelection()
        {
            var startSpan = NormalizeToTimeSpan(StartTimeBox.Text, TimeSpan.Zero);
            var endSpan = NormalizeToTimeSpan(EndTimeBox.Text, TimeSpan.FromMinutes(5));

            if (VideoSegmentRadio.IsChecked == true || AudioSegmentRadio.IsChecked == true)
            {
                if (endSpan <= startSpan)
                {
                    endSpan = startSpan;
                }

                var type = VideoSegmentRadio.IsChecked == true ? "Video Segment" : "Audio Segment";
                return (type, FormatTime(startSpan), FormatTime(endSpan));
            }

            if (FullAudioRadio.IsChecked == true)
            {
                return ("Full Audio", FormatTime(TimeSpan.Zero), FormatTime(TimeSpan.Zero));
            }

            return ("Full Video", FormatTime(TimeSpan.Zero), FormatTime(TimeSpan.Zero));
        }

        private static TimeSpan NormalizeToTimeSpan(string? value, TimeSpan fallback)
        {
            if (TimeSpan.TryParse(value, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static string FormatTime(TimeSpan value) => value.ToString(@"hh\:mm\:ss");

        private static bool IsAgeRestrictionMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            var lower = message.ToLowerInvariant();
            return lower.Contains("age-restricted") ||
                   lower.Contains("age restricted") ||
                   lower.Contains("sign in to confirm your age") ||
                   lower.Contains("verify your age") ||
                   lower.Contains("adult content") ||
                   lower.Contains("policy violation: age-restricted");
        }

        private static string? ExtractDownloadedFilePath(string? output, string targetFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(output)) return null;
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var raw in lines.Reverse())
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;

                    const string mergeToken = "Merging formats into \"";
                    if (line.Contains(mergeToken, StringComparison.OrdinalIgnoreCase))
                    {
                        var start = line.IndexOf('"');
                        var end = line.LastIndexOf('"');
                        if (start >= 0 && end > start)
                        {
                            var path = line.Substring(start + 1, end - start - 1);
                            return NormalizeDownloadedPath(path, targetFolder);
                        }
                    }

                    const string destinationToken = "Destination:";
                    if (line.StartsWith("[download]", StringComparison.OrdinalIgnoreCase) && line.Contains(destinationToken, StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = line.IndexOf(destinationToken, StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            var pathPart = line.Substring(idx + destinationToken.Length).Trim();
                            pathPart = pathPart.Trim('"');
                            return NormalizeDownloadedPath(pathPart, targetFolder);
                        }
                    }

                    if (line.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase) && line.Contains("into \"", StringComparison.OrdinalIgnoreCase))
                    {
                        var start = line.IndexOf('"');
                        var end = line.LastIndexOf('"');
                        if (start >= 0 && end > start)
                        {
                            var path = line.Substring(start + 1, end - start - 1);
                            return NormalizeDownloadedPath(path, targetFolder);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to parse download output for file path.", ex);
            }

            return GuessMostRecentDownloadedFile(targetFolder);
        }

        private static string? NormalizeDownloadedPath(string path, string targetFolder)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var trimmed = path.Trim();
            if (System.IO.Path.IsPathRooted(trimmed))
            {
                return trimmed;
            }

            if (string.IsNullOrWhiteSpace(targetFolder)) return null;
            try
            {
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(targetFolder, trimmed));
            }
            catch
            {
                return null;
            }
        }

        private static string? GuessMostRecentDownloadedFile(string targetFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder)) return null;
                var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".m4v", ".m4a", ".mp3" };
                var candidate = Directory.EnumerateFiles(targetFolder)
                    .Where(f => extensions.Contains(System.IO.Path.GetExtension(f)))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                return candidate;
            }
            catch
            {
                return null;
            }
        }

        private static string GetUniqueFilePathWithExtension(string sourcePath, string newExtension)
        {
            var directory = System.IO.Path.GetDirectoryName(sourcePath) ?? string.Empty;
            var baseName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
            var candidate = System.IO.Path.Combine(directory, baseName + newExtension);
            int counter = 1;
            while (File.Exists(candidate))
            {
                candidate = System.IO.Path.Combine(directory, $"{baseName} ({counter}){newExtension}");
                counter++;
            }
            return candidate;
        }

        private static bool IsVideoDownload(string? downloadType)
        {
            if (string.IsNullOrWhiteSpace(downloadType)) return false;
            return downloadType.Contains("Video", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> ConvertVideoToMp4Async(DownloadItem item, string sourcePath, string ffmpegPath, CancellationToken token)
        {
            try
            {
                var destination = GetUniqueFilePathWithExtension(sourcePath, ".mp4");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    item.Status = "Converting to MP4";
                    item.ErrorMessage = string.Empty;
                    item.RetryVisible = Visibility.Collapsed;
                    item.ErrorVisible = Visibility.Collapsed;
                    item.OpenVisible = Visibility.Collapsed;
                });

                var args = $"-y -i \"{sourcePath}\" -c:v libx264 -preset medium -crf 20 -c:a aac -movflags +faststart \"{destination}\"";
                await RunProcessCaptureAsync(ffmpegPath, args, token);

                try { File.Delete(sourcePath); } catch (Exception ex) { AppLogger.LogWarning($"Failed to delete source file '{sourcePath}' after MP4 conversion: {ex.Message}"); }
                AppLogger.LogInfo($"Converted {sourcePath} to MP4 at {destination}.");
                return destination;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ProcessExecutionException ex)
            {
                AppLogger.LogProcessFailure($"MP4 conversion failed for {sourcePath}.", ex);
                var summary = SummarizeProcessFailure(ex);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    item.Status = "Failed (Conversion)";
                    item.ErrorMessage = summary;
                    item.RetryVisible = Visibility.Visible;
                    item.ErrorVisible = Visibility.Visible;
                    item.OpenVisible = File.Exists(sourcePath) ? Visibility.Visible : Visibility.Collapsed;
                });
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"MP4 conversion failed for {sourcePath}.", ex);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    item.Status = "Failed (Conversion)";
                    item.ErrorMessage = ex.Message;
                    item.RetryVisible = Visibility.Visible;
                    item.ErrorVisible = Visibility.Visible;
                    item.OpenVisible = File.Exists(sourcePath) ? Visibility.Visible : Visibility.Collapsed;
                });
                return null;
            }
        }

        private void AddLinkToQueue(string url)
        {
            if (downloadQueue.Any(d => d.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            {
                ShowStatusMessage("Link already exists in the queue.");
                return;
            }

            var selection = GetCurrentDownloadSelection();

            if (selection.downloadType.Contains("Segment", StringComparison.OrdinalIgnoreCase))
            {
                if (TimeSpan.TryParse(selection.startTime, out var start) &&
                    TimeSpan.TryParse(selection.endTime, out var end) &&
                    end <= start)
                {
                    ShowStatusMessage("End time must be after start time for segment downloads.");
                    return;
                }
            }

            var item = new DownloadItem
            {
                RowNumber = downloadQueue.Count + 1,
                Url = url,
                DisplayTitle = url, // You can later update this with video title
                Status = "Queued",
                Progress = 0,
                DownloadType = selection.downloadType,
                StartTime = selection.startTime,
                EndTime = selection.endTime,
                RetryVisible = Visibility.Collapsed,
                ErrorVisible = Visibility.Collapsed,
                OpenVisible = Visibility.Collapsed,
                FilePath = string.Empty,
                ErrorMessage = string.Empty
            };

            downloadQueue.Add(item);
            UpdateEmptyHintVisibility();
            // Resolve the title asynchronously
            _ = ResolveTitleForItemAsync(item);
        }

        private void UpdateEmptyHintVisibility()
        {
            EmptyHintText.Visibility = downloadQueue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        [GeneratedRegex(
            @"(https?://)?(www\.)?(youtube\.com/(watch|shorts|playlist|embed)[^\s""'<>]*|youtu\.be/[^\s""'<>]*)",
            RegexOptions.IgnoreCase)]
        private static partial Regex YoutubeUrlRegex();

        private static string? ExtractYouTubeUrl(string text)
        {
            var match = YoutubeUrlRegex().Match(text);
            if (!match.Success) return null;

            string url = match.Value;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;

            // Normalize youtu.be short links to full watch URLs
            if (url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase))
            {
                var prefix = "youtu.be/";
                var idx = url.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) + prefix.Length;
                var videoId = url[idx..].Split('?', '&')[0].TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(videoId))
                    return $"https://www.youtube.com/watch?v={videoId}";
            }

            return url;
        }

        private void PasteLinkFromClipboard()
        {
            if (!Clipboard.ContainsText()) return;

            string text = Clipboard.GetText().Trim();
            string? validUrl = ExtractYouTubeUrl(text);

            if (validUrl != null)
            {
                AddLinkToQueue(validUrl);
            }
            else
            {
                ShowStatusMessage("Clipboard does not contain a valid link.");
            }
        }


        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Don't intercept paste when typing into a text input
                var focused = FocusManager.GetFocusedElement(this);
                if (focused is TextBox || focused is PasswordBox || focused is RichTextBox)
                {
                    base.OnKeyDown(e);
                    return;
                }

                PasteLinkFromClipboard();
                e.Handled = true;
            }
        }


        private Dictionary<TextBox, string> timeInputStates = new();
        private void TimeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!char.IsDigit(e.Text, 0))
            {
                e.Handled = true;
                return;
            }

            var box = sender as TextBox;
            if (box == null) return;

            // Get the raw digit buffer or initialize to 000000
            if (!timeInputStates.ContainsKey(box))
                timeInputStates[box] = "000000";

            string buffer = timeInputStates[box];

            // Rolling buffer: drop first digit, add new one at the end
            buffer = buffer.Substring(1) + e.Text;

            // For display only: clamp MM/SS but don't write back to buffer
            int hh = int.Parse(buffer.Substring(0, 2));
            int mmRaw = int.Parse(buffer.Substring(2, 2));
            int ssRaw = int.Parse(buffer.Substring(4, 2));
            int mm = Math.Min(mmRaw, 59);
            int ss = Math.Min(ssRaw, 59);

            // Update UI
            box.Text = $"{hh:00}:{mm:00}:{ss:00}";
            box.CaretIndex = box.Text.Length;

            // Save original buffer (not clamped version)
            timeInputStates[box] = buffer;

            e.Handled = true;
        }

        private void TimeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox box) return;

            string digits = new string(box.Text.Where(char.IsDigit).ToArray());

            if (digits.Length > 6) digits = digits[..6];
            while (digits.Length < 6) digits = "0" + digits;

            var formatted = $"{digits[..2]}:{digits[2..4]}:{digits[4..6]}";
            if (box.Text == formatted) return; // already correct, avoid re-triggering

            box.Text = formatted;
            box.CaretIndex = box.Text.Length;
        }

        private CancellationTokenSource? _statusCts;
        private async void ShowStatusMessage(string message, int durationSeconds = 4)
        {
            // Cancel any previous pending fade-out so messages don't overlap
            _statusCts?.Cancel();
            _statusCts?.Dispose();
            var cts = new CancellationTokenSource();
            _statusCts = cts;

            StatusMessageTextBlock.Text = message;
            StatusMessageTextBlock.Opacity = 1;
            StatusMessageTextBlock.BeginAnimation(OpacityProperty, null); // stop any running animation

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(durationSeconds), cts.Token);
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1));
                StatusMessageTextBlock.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (OperationCanceledException) { }
        }
        private ScrollViewer? _downloadsScrollViewer;

        private void DownloadsListView_Loaded(object sender, RoutedEventArgs e)
        {
            _downloadsScrollViewer = FindScrollViewer(DownloadsListView);
        }

        private void DownloadsListView_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_downloadsScrollViewer == null) return;

            bool isVerticalScrollVisible = _downloadsScrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible;

            // Prefer the named column, but fall back to index if needed
            if (TitleColumn != null)
            {
                TitleColumn.Width = isVerticalScrollVisible ? 438 : 450;
            }
            else if (DownloadsListView.View is GridView gv && gv.Columns.Count > 1)
            {
                gv.Columns[1].Width = isVerticalScrollVisible ? 438 : 450;
            }
        }

        // Utility to find the ScrollViewer inside the ListView
        private ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer viewer)
                    return viewer;

                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }

            return null;
        }
        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is DownloadItem item)
            {
                RemoveItem(item);
            }
        }

        private void RemoveItem(DownloadItem item)
        {
            if (item == null) return;

            downloadQueue.Remove(item);

            // Recalculate row numbers after removal
            for (int i = 0; i < downloadQueue.Count; i++)
            {
                downloadQueue[i].RowNumber = i + 1;
            }

            UpdateEmptyHintVisibility();
        }
        private void SegmentTimeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (StartTimeBox == null || EndTimeBox == null || SegmentDurationText == null)
                return;

            if (TimeSpan.TryParse(StartTimeBox.Text, out TimeSpan start) &&
                TimeSpan.TryParse(EndTimeBox.Text, out TimeSpan end))
            {
                if (end > start)
                {
                    TimeSpan duration = end - start;

                    var parts = new List<string>();
                    if (duration.Hours > 0) parts.Add($"{duration.Hours}h");
                    if (duration.Minutes > 0) parts.Add($"{duration.Minutes}m");
                    if (duration.Seconds > 0 || parts.Count == 0) parts.Add($"{duration.Seconds}s");

                    SegmentDurationText.Text = $"Duration: {string.Join("", parts)}";
                    StartTimeBox.BorderBrush = Brushes.Gray;
                    EndTimeBox.BorderBrush = Brushes.Gray;
                }
                else
                {
                    SegmentDurationText.Text = "❌ Invalid Segment";
                    StartTimeBox.BorderBrush = Brushes.Red;
                    EndTimeBox.BorderBrush = Brushes.Red;
                }
            }
            else
            {
                SegmentDurationText.Text = "";
                StartTimeBox.BorderBrush = Brushes.Gray;
                EndTimeBox.BorderBrush = Brushes.Gray;
            }
        }

        private static readonly string SaveFilePath = System.IO.Path.Combine(AppPaths.AppDataDirectory, "downloads.json");
        private void SaveDownloadQueue()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(SaveDownloadQueue);
                return;
            }

            var serializableList = downloadQueue.Select(d => new DownloadItemSerializable
            {
                RowNumber = d.RowNumber,
                Url = d.Url,
                DisplayTitle = d.DisplayTitle,
                Progress = d.Progress,
                Status = d.Status,
                DownloadType = d.DownloadType,
                StartTime = d.StartTime,
                EndTime = d.EndTime,
                VideoTitle = d.VideoTitle,
                ErrorMessage = d.ErrorMessage,
                FilePath = d.FilePath
            }).ToList();

            try
            {
                var json = JsonSerializer.Serialize(serializableList);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SaveFilePath)!);
                File.WriteAllText(SaveFilePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to save download queue.", ex);
            }
        }

        private void LoadDownloadQueue()
        {
            if (!File.Exists(SaveFilePath)) return;

            try
            {
                var json = File.ReadAllText(SaveFilePath);
                var items = JsonSerializer.Deserialize<List<DownloadItemSerializable>>(json);
                if (items == null) return;

                downloadQueue.Clear();
                foreach (var item in items)
                {
                    downloadQueue.Add(new DownloadItem
                    {
                        RowNumber = item.RowNumber,
                        Url = item.Url,
                        DisplayTitle = item.DisplayTitle,
                        Progress = item.Progress,
                        Status = item.Status,
                        DownloadType = item.DownloadType ?? "Full Video",
                        StartTime = item.StartTime ?? "00:00:00",
                        EndTime = item.EndTime ?? "00:00:00",
                        VideoTitle = item.VideoTitle ?? "(Resolving Title...)",
                        ErrorMessage = item.ErrorMessage,
                        FilePath = item.FilePath,
                        RetryVisible = Visibility.Collapsed,
                        ErrorVisible = Visibility.Collapsed,
                        OpenVisible = Visibility.Collapsed
                    });
                }

                UpdateEmptyHintVisibility();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load saved downloads: " + ex.Message);
            }
        }


    public MainWindow()
        {
            InitializeComponent();
            DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark);
            // Load settings once to ensure defaults are available
            try { _ = SettingsManager.Load(); } catch { }
            
            StartTimeBox.Text = "00:00:00";
            EndTimeBox.Text = "00:05:00";
            DownloadsListView.ItemTemplate = null;
            DownloadsListView.ItemsSource = downloadQueue;
            FullVideoRadio.Checked += DownloadModeChanged;
            VideoSegmentRadio.Checked += DownloadModeChanged;
            FullAudioRadio.Checked += DownloadModeChanged;
            AudioSegmentRadio.Checked += DownloadModeChanged;
            LoadDownloadQueue();

            this.Tag = this;
            DataContext = this;

            DownloadModeChanged(this, new RoutedEventArgs());

            // Ensure empty hint reflects initial state
            UpdateEmptyHintVisibility();

            // Kick off background title resolution for any unresolved items
            _ = ResolveTitlesForUnresolvedAsync();

            // Optional: check app updates on startup if configured
            _ = CheckAppUpdateOnStartupAsync();

            // Best-effort cleanup of old tool versions
            try { ToolsManager.CleanupWithSettings(SettingsManager.Load()); } catch { }

            // Notify about tool updates in status area
            _ = NotifyToolUpdatesAsync();

            // First-run guidance or auto-setup
            _ = HandleFirstRunAsync();
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            // Keep closing simple and synchronous to avoid perceived hangs.
            try { SaveDownloadQueue(); } catch { }
            try { _downloadCts?.Cancel(); } catch { }
            // Proactively kill any child processes we spawned (yt-dlp) if still running
            try
            {
                foreach (var p in _childProcesses.Values)
                {
                    try { if (p != null && !p.HasExited) p.Kill(true); } catch { }
                }
            }
            catch { }
            base.OnClosing(e);
        }

        private async Task HandleFirstRunAsync()
        {
            var s = SettingsManager.Load();
            if (s.HasCompletedFirstRun) return;

            // If tools already present, mark complete and return
            bool ytOk = !string.IsNullOrWhiteSpace(s.YtDlpPath) && File.Exists(s.YtDlpPath);
            bool ffOk = !string.IsNullOrWhiteSpace(s.FfmpegPath) && File.Exists(s.FfmpegPath);
            if (ytOk && ffOk)
            {
                s.HasCompletedFirstRun = true;
                SettingsManager.Save(s);
                return;
            }

            var res = MessageBox.Show(
                "First run setup:\n\nWould you like to download yt-dlp and ffmpeg now?\n(You can also do this later in Settings)",
                "Setup Tools",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    SetStatusDuringFirstRun("Downloading tools...");
                    var ytTask = ToolsManager.EnsureLatestYtDlpAsync(s.YtDlpPath);
                    var ffTask = ToolsManager.EnsureFfmpegLatestAsync(s.FfmpegPath);
                    await Task.WhenAll(ytTask, ffTask);
                    if (!string.IsNullOrEmpty(ytTask.Result.path)) s.YtDlpPath = ytTask.Result.path;
                    if (!string.IsNullOrEmpty(ffTask.Result.path)) s.FfmpegPath = ffTask.Result.path;
                    s.HasCompletedFirstRun = true;
                    SettingsManager.Save(s);
                    ShowStatusMessage("Tools ready.", 5);
                }
                catch (Exception ex)
                {
                    ShowStatusMessage("Failed to download tools: " + ex.Message, 6);
                }
            }
            else
            {
                // Guide user to Settings
                ShowStatusMessage("Open Settings to download yt-dlp and ffmpeg.", 6);
                s.HasCompletedFirstRun = true;
                SettingsManager.Save(s);
            }
        }

        private void SetStatusDuringFirstRun(string msg)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessageTextBlock.Text = msg;
                    StatusMessageTextBlock.Opacity = 1;
                });
            }
            catch { }
        }

        private async Task NotifyToolUpdatesAsync()
        {
            var s = SettingsManager.Load();
            try
            {
                var yt = await ToolsManager.IsYtDlpUpdateAvailableAsync(s.YtDlpPath);
                if (yt.hasUpdate)
                    ShowStatusMessage("New yt-dlp update available", 6);
            }
            catch { }
            try
            {
                var ff = await ToolsManager.IsFfmpegUpdateAvailableAsync(s.FfmpegPath);
                if (ff)
                    ShowStatusMessage("New ffmpeg update available", 6);
            }
            catch { }
        }

        private async Task ResolveTitlesForUnresolvedAsync()
        {
            var tasks = new List<Task>();
            foreach (var item in downloadQueue.Where(i => string.IsNullOrWhiteSpace(i.VideoTitle) || i.VideoTitle.Contains("Resolving", StringComparison.OrdinalIgnoreCase)))
            {
                tasks.Add(ResolveTitleForItemAsync(item));
            }
            try { await Task.WhenAll(tasks); } catch (Exception ex) { AppLogger.LogError("Failed to resolve one or more video titles.", ex); }
        }

        private async Task ResolveTitleForItemAsync(DownloadItem item)
        {
            var settings = SettingsManager.Load();
            var yt = settings.YtDlpPath;
            if (string.IsNullOrWhiteSpace(yt) || !File.Exists(yt))
                return;
            try
            {
                string title = await RunProcessCaptureAsync(yt, $"-e \"{EscapeArg(item.Url)}\"", CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    // Use first non-empty line as title
                    var first = title.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(first))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            item.VideoTitle = first;
                        });
                        SaveDownloadQueue();
                    }
                }
            }
            catch (ProcessExecutionException ex)
            {
                AppLogger.LogProcessFailure($"Failed to resolve title for {item.Url}.", ex);
                var summary = SummarizeProcessFailure(ex);
                if (IsAgeRestrictionMessage(summary) || IsAgeRestrictionMessage(ex.StdError) || IsAgeRestrictionMessage(ex.StdOutput))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        item.VideoTitle = "Video is age restricted. Cannot download.";
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to resolve title for {item.Url}.", ex);
                if (IsAgeRestrictionMessage(ex.Message))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        item.VideoTitle = "Video is age restricted. Cannot download.";
                    });
                }
            }
        }

        private bool _isDownloading = false;
        private CancellationTokenSource? _downloadCts;
        private volatile bool _stopAfterActive = false;
        private readonly HashSet<DownloadItem> _activeItems = new();
        private async Task StartDownloadsAsync()
        {
            if (_isDownloading) { ShowStatusMessage("Downloads already in progress..."); return; }
            _isDownloading = true;
            _stopAfterActive = false;
            _downloadCts = new CancellationTokenSource();
            StartDownloadButton.Content = "Stop Downloads";
            try
            {
                var settings = SettingsManager.Load();
                int maxParallel = Math.Max(1, Math.Min(3, settings.ConcurrentDownloads));
                using var semaphore = new SemaphoreSlim(maxParallel, maxParallel);

                var tasks = new List<Task>();
                foreach (var item in downloadQueue.Where(i => i.Status == "Queued"))
                {
                    if (_stopAfterActive) break; // user requested to finish active only
                    await semaphore.WaitAsync();
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            lock (_activeItems) _activeItems.Add(item);
                            await DownloadItemAsync(item, settings, _downloadCts!.Token);
                        }
                        finally
                        {
                            lock (_activeItems) _activeItems.Remove(item);
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
                ShowStatusMessage("All downloads finished.");
            }
            finally
            {
                _isDownloading = false;
                StartDownloadButton.Content = "Start Downloads";
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
        }

        private async Task DownloadItemAsync(DownloadItem item, AppSettings settings, CancellationToken token)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                item.Status = "Preparing";
                item.Progress = 0;
            });

            string targetFolder = DownloadFolderOpener.ComputeTargetFolder(settings, item.DownloadType);
            if (string.IsNullOrWhiteSpace(targetFolder))
                targetFolder = settings.DownloadDirectory;
            try
            {
                Directory.CreateDirectory(targetFolder);
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to create download target folder '{targetFolder}'.", ex);
            }

            string yt = settings.YtDlpPath;
            if (string.IsNullOrWhiteSpace(yt) || !File.Exists(yt))
            {
                AppLogger.LogError($"yt-dlp not found at '{yt}'. Unable to process {item.Url} ({item.DownloadType}).");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    item.Status = "Error: yt-dlp not found";
                    item.ErrorMessage = "Set yt-dlp path in Settings.";
                    item.RetryVisible = Visibility.Visible;
                    item.ErrorVisible = Visibility.Visible;
                });
                return;
            }

            var args = BuildYtDlpArgs(item, targetFolder, settings);

            Application.Current.Dispatcher.Invoke(() => item.Status = "Downloading");
            string output;
            string? finalFilePath = null;
            try
            {
                output = await RunProcessCaptureAsync(yt, args, token);
                finalFilePath = ExtractDownloadedFilePath(output, targetFolder);
                if (!string.IsNullOrWhiteSpace(finalFilePath))
                {
                    item.FilePath = finalFilePath;
                }
            }
            catch (OperationCanceledException)
            {
                AppLogger.LogInfo($"Download cancelled for {item.Url} ({item.DownloadType}).");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    item.Status = "Failed";
                    item.ErrorMessage = "Cancelled by user";
                    item.RetryVisible = Visibility.Visible;
                    item.ErrorVisible = Visibility.Visible;
                    item.OpenVisible = Visibility.Collapsed;
                });
                TryCleanupPartialFiles(targetFolder);
                return;
            }
            catch (ProcessExecutionException ex)
            {
                AppLogger.LogProcessFailure($"Download failed for {item.Url} ({item.DownloadType}).", ex);
                var summary = SummarizeProcessFailure(ex);
                var ageRestricted = IsAgeRestrictionMessage(summary) || IsAgeRestrictionMessage(ex.StdError) || IsAgeRestrictionMessage(ex.StdOutput);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (ageRestricted)
                    {
                        const string ageMessage = "Video is age restricted. Cannot download.";
                        item.Status = "Failed (Age Restricted)";
                        item.ErrorMessage = ageMessage;
                        item.VideoTitle = ageMessage;
                    }
                    else
                    {
                        item.Status = "Failed";
                        item.ErrorMessage = summary;
                    }
                    item.RetryVisible = ageRestricted ? Visibility.Collapsed : Visibility.Visible;
                    item.ErrorVisible = Visibility.Visible;
                    item.OpenVisible = Visibility.Collapsed;
                });
                TryCleanupPartialFiles(targetFolder);
                return;
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Download failed for {item.Url} ({item.DownloadType}).", ex);
                var message = ex.Message;
                var ageRestricted = IsAgeRestrictionMessage(message);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (ageRestricted)
                    {
                        const string ageMessage = "Video is age restricted. Cannot download.";
                        item.Status = "Failed (Age Restricted)";
                        item.ErrorMessage = ageMessage;
                        item.VideoTitle = ageMessage;
                    }
                    else
                    {
                        item.Status = "Failed";
                        item.ErrorMessage = message;
                    }
                    item.RetryVisible = ageRestricted ? Visibility.Collapsed : Visibility.Visible;
                    item.ErrorVisible = Visibility.Visible;
                    item.OpenVisible = Visibility.Collapsed;
                });
                TryCleanupPartialFiles(targetFolder);
                return;
            }

            if (string.IsNullOrWhiteSpace(finalFilePath))
            {
                if (!string.IsNullOrWhiteSpace(item.FilePath))
                {
                    finalFilePath = item.FilePath;
                }
                else
                {
                    finalFilePath = GuessMostRecentDownloadedFile(targetFolder);
                    if (!string.IsNullOrWhiteSpace(finalFilePath))
                        item.FilePath = finalFilePath;
                }
            }

            if (settings.ConvertVideoToMp4 && IsVideoDownload(item.DownloadType))
            {
                if (string.IsNullOrWhiteSpace(settings.FfmpegPath) || !File.Exists(settings.FfmpegPath))
                {
                    AppLogger.LogError("Cannot convert to MP4 because ffmpeg path is not configured.");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        item.Status = "Failed (Conversion)";
                        item.ErrorMessage = "Cannot convert to MP4: ffmpeg path is not configured.";
                        item.RetryVisible = Visibility.Visible;
                        item.ErrorVisible = Visibility.Visible;
                        item.OpenVisible = !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath) ? Visibility.Visible : Visibility.Collapsed;
                    });
                    SaveDownloadQueue();
                    return;
                }

                if (string.IsNullOrWhiteSpace(finalFilePath) || !File.Exists(finalFilePath))
                {
                    AppLogger.LogError("Cannot convert to MP4 because the downloaded file could not be located.");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        item.Status = "Failed (Conversion)";
                        item.ErrorMessage = "Cannot convert to MP4: downloaded file not found.";
                        item.RetryVisible = Visibility.Visible;
                        item.ErrorVisible = Visibility.Visible;
                        item.OpenVisible = Visibility.Collapsed;
                    });
                    SaveDownloadQueue();
                    return;
                }

                string? mp4Path;
                try
                {
                    mp4Path = await ConvertVideoToMp4Async(item, finalFilePath, settings.FfmpegPath!, token);
                }
                catch (OperationCanceledException)
                {
                    AppLogger.LogInfo($"Download cancelled for {item.Url} ({item.DownloadType}).");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        item.Status = "Failed";
                        item.ErrorMessage = "Cancelled by user";
                        item.RetryVisible = Visibility.Visible;
                        item.ErrorVisible = Visibility.Visible;
                        item.OpenVisible = !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath) ? Visibility.Visible : Visibility.Collapsed;
                    });
                    TryCleanupPartialFiles(targetFolder);
                    return;
                }

                if (mp4Path == null)
                {
                    SaveDownloadQueue();
                    return;
                }

                finalFilePath = mp4Path;
                item.FilePath = mp4Path;
            }

            if (!string.IsNullOrWhiteSpace(finalFilePath))
            {
                item.FilePath = finalFilePath;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                item.Status = "Completed";
                item.Progress = 100;
                item.OpenVisible = Visibility.Visible;
            });

            // Try to open folder/file (file if known later)
            OnDownloadCompleted(item);
            SaveDownloadQueue();
        }

        private static string EscapeArg(string value) => value.Replace("\"", "\\\"");

        private string BuildYtDlpArgs(DownloadItem item, string targetFolder, AppSettings settings)
        {
            // Common flags: set output template
            var outTemplatePath = System.IO.Path.Combine(targetFolder, "%(title)s.%(ext)s");
            var output = $"-o \"{EscapeArg(outTemplatePath.Replace("\\", "/"))}\""; // yt-dlp accepts forward slashes on Windows
            var ffmpeg = settings.FfmpegPath;
            var ffArg = string.IsNullOrWhiteSpace(ffmpeg) ? string.Empty : $" --ffmpeg-location \"{EscapeArg(ffmpeg)}\"";
            var url = $"\"{EscapeArg(item.Url)}\"";

            bool isSeg = item.DownloadType?.Contains("Segment", StringComparison.OrdinalIgnoreCase) == true;
            string segArg = string.Empty;
            if (isSeg && TimeSpan.TryParse(item.StartTime, out var start) && TimeSpan.TryParse(item.EndTime, out var end) && end > start)
            {
                segArg = $" --download-sections \"*{item.StartTime}-{item.EndTime}\"";
            }

            switch (item.DownloadType)
            {
                case "Full Audio":
                    return $"{output}{ffArg} -x --audio-format mp3 {url}";
                case "Audio Segment":
                    return $"{output}{ffArg}{segArg} -x --audio-format mp3 {url}";
                case "Video Segment":
                    return $"{output}{ffArg}{segArg} {url}";
                case "Full Video":
                default:
                    return $"{output}{ffArg} {url}";
            }
        }

        private static readonly ConcurrentDictionary<int, Process> _childProcesses = new();
        private static async Task<string> RunProcessCaptureAsync(string exe, string args, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<string>();
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var output = new StringBuilder();
            var error = new StringBuilder();
            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };
            proc.Exited += (_, __) =>
            {
                _childProcesses.TryRemove(proc.Id, out Process? _);
                var stdOut = output.ToString();
                var stdErr = error.ToString();

                if (proc.ExitCode == 0)
                {
                    tcs.TrySetResult(stdOut);
                }
                else
                {
                    var message = $"Process '{exe}' exited with code {proc.ExitCode}.";
                    if (!string.IsNullOrWhiteSpace(stdErr))
                        message += $" stderr: {stdErr.Trim()}";
                    else if (!string.IsNullOrWhiteSpace(stdOut))
                        message += $" output: {stdOut.Trim()}";

                    tcs.TrySetException(new ProcessExecutionException(message, exe, args, proc.ExitCode, stdOut, stdErr));
                }
            };

            try
            {
                if (!proc.Start())
                {
                    throw new ProcessExecutionException($"Failed to start process '{exe}'.", exe, args, -1, string.Empty, string.Empty);
                }
            }
            catch (Exception ex) when (ex is not ProcessExecutionException)
            {
                throw new ProcessExecutionException($"Failed to start process '{exe}'. {ex.Message}", exe, args, -1, string.Empty, string.Empty);
            }

            _childProcesses.TryAdd(proc.Id, proc);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            using (token.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } }))
            {
                token.ThrowIfCancellationRequested();
                return await tcs.Task.ConfigureAwait(false);
            }
        }

        private static string SummarizeProcessFailure(ProcessExecutionException ex)
        {
            static string FirstLine(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return string.Empty;
                var parts = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return parts.FirstOrDefault() ?? string.Empty;
            }

            var summary = FirstLine(ex.StdError);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                return summary;
            }

            summary = FirstLine(ex.StdOutput);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                return summary;
            }

            return ex.Message;
        }

        private async Task CheckAppUpdateOnStartupAsync()
        {
            var s = SettingsManager.Load();
            if (!s.AutoCheckAppUpdates) return;
            if (string.IsNullOrWhiteSpace(s.AppUpdateFeedUrl)) return;
            try
            {
                var current = AppUpdateManager.GetCurrentVersion();
                var latest = await AppUpdateManager.GetLatestAsync(s.AppUpdateFeedUrl);
                if (latest != null && AppUpdateManager.IsNewer(current, latest.Version))
                {
                    var displayVersion = string.IsNullOrWhiteSpace(latest.DisplayVersion) ? latest.Version : latest.DisplayVersion;
                    var currentDisplay = AppUpdateManager.FormatVersion(current);
                    var msg = $"A new version {displayVersion} is available.\nCurrent version: {currentDisplay}.";
                    if (!string.IsNullOrWhiteSpace(latest.Notes)) msg += "\n\n" + latest.Notes;

                    var launchUrl = !string.IsNullOrWhiteSpace(latest.DownloadUrl) ? latest.DownloadUrl : latest.ReleasePageUrl;
                    if (!string.IsNullOrWhiteSpace(launchUrl))
                    {
                        msg += "\n\nDownload the new version now?";
                        if (MessageBox.Show(msg, "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                        {
                            try { Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true }); } catch { }
                        }
                    }
                    else
                    {
                        MessageBox.Show(msg, "Update Available", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch { }
        }

        private static void TryCleanupPartialFiles(string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
                foreach (var file in Directory.EnumerateFiles(folder, "*.part"))
                {
                    try { File.Delete(file); } catch { }
                }
                foreach (var file in Directory.EnumerateFiles(folder, "*.ytdl"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

    }

}

public class DownloadItem : INotifyPropertyChanged
{
    private int rowNumber;
    public int RowNumber
    {
        get => rowNumber;
        set { rowNumber = value; OnPropertyChanged(nameof(RowNumber)); }
    }

    private string url = string.Empty;
    public string Url
    {
        get => url;
        set { url = value; OnPropertyChanged(nameof(Url)); }
    }

    private string displayTitle = string.Empty;
    public string DisplayTitle
    {
        get => displayTitle;
        set { displayTitle = value; OnPropertyChanged(nameof(DisplayTitle)); }
    }

    private int progress;
    public int Progress
    {
        get => progress;
        set { progress = value; OnPropertyChanged(nameof(Progress)); }
    }

    private string status = "Queued";
    public string Status
    {
        get => status;
        set { status = value; OnPropertyChanged(nameof(Status)); }
    }
    private string videoTitle = "(Resolving Title...)";
    public string VideoTitle
    {
        get => videoTitle;
        set { videoTitle = value; OnPropertyChanged(nameof(VideoTitle)); }
    }

    private string downloadType = "Full Video";
    public string DownloadType
    {
        get => downloadType;
        set { downloadType = value; OnPropertyChanged(nameof(DownloadType)); OnPropertyChanged(nameof(FormattedSegment)); }
    }

    private string startTime = "00:00:00";
    public string StartTime
    {
        get => startTime;
        set { startTime = value; OnPropertyChanged(nameof(StartTime)); OnPropertyChanged(nameof(FormattedSegment)); }
    }

    private string endTime = "00:00:00";
    public string EndTime
    {
        get => endTime;
        set { endTime = value; OnPropertyChanged(nameof(EndTime)); OnPropertyChanged(nameof(FormattedSegment)); }
    }

    public string FormattedSegment =>
        DownloadType.Contains("Segment")
            ? $"Start: {StartTime} — End: {EndTime}"
            : string.Empty;

    private Visibility retryVisible = Visibility.Collapsed;
    public Visibility RetryVisible
    {
        get => retryVisible;
        set { retryVisible = value; OnPropertyChanged(nameof(RetryVisible)); }
    }

    private Visibility errorVisible = Visibility.Collapsed;
    public Visibility ErrorVisible
    {
        get => errorVisible;
        set { errorVisible = value; OnPropertyChanged(nameof(ErrorVisible)); }
    }

    private Visibility openVisible = Visibility.Collapsed;
    public Visibility OpenVisible
    {
        get => openVisible;
        set { openVisible = value; OnPropertyChanged(nameof(OpenVisible)); }
    }

    private string errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => errorMessage;
        set { errorMessage = value; OnPropertyChanged(nameof(ErrorMessage)); }
    }

    private string filePath = string.Empty;
    public string FilePath
    {
        get => filePath;
        set { filePath = value; OnPropertyChanged(nameof(FilePath)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class DownloadItemSerializable
{
    public int RowNumber { get; set; }
    public string Url { get; set; } = "";
    public string DisplayTitle { get; set; } = "";
    public int Progress { get; set; }
    public string Status { get; set; } = "";
    public string? DownloadType { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? VideoTitle { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string FilePath { get; set; } = "";
}
