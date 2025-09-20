using Dark.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clipboard = System.Windows.Clipboard;

namespace YouTubeDownloader
{
    public partial class AddLinksWindow : Window
    {
        public List<AddLinkEntry> LinkEntries { get; private set; } = new();

        public AddLinksWindow()
        {
            InitializeComponent();
            try { Dark.Net.DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark); } catch { }
            // Intercept paste to normalize and deduplicate lines
            BulkLinksBox.AddHandler(CommandManager.PreviewExecutedEvent,
                new ExecutedRoutedEventHandler(OnPreviewExecuted), true);
        }

        // Update parsed entries whenever the bulk box changes
        private void BulkLinksBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            LinkEntries.Clear();
            var text = BulkLinksBox.Text ?? string.Empty;
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!Uri.IsWellFormedUriString(trimmed, UriKind.Absolute)) continue;
                LinkEntries.Add(new AddLinkEntry
                {
                    Url = trimmed,
                    DownloadType = "Full Video",
                    StartTime = "00:00:00",
                    EndTime = "00:05:00"
                });
            }
        }

        private void AddLinksButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Paste)
            {
                try
                {
                    if (!Clipboard.ContainsText()) return;
                    string pasted = Clipboard.GetText() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(pasted)) return;

                    var newUrls = ExtractUrls(pasted);
                    if (newUrls.Count == 0) return;

                    var existing = new HashSet<string>(
                        (BulkLinksBox.Text ?? string.Empty)
                            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim()),
                        StringComparer.OrdinalIgnoreCase);

                    var toInsert = newUrls.Where(u => !existing.Contains(u)).ToList();
                    if (toInsert.Count == 0)
                    {
                        e.Handled = true; // suppress redundant paste
                        return;
                    }

                    int caret = BulkLinksBox.CaretIndex;
                    string text = BulkLinksBox.Text ?? string.Empty;

                    bool needPrefixNl = caret > 0 && text[caret - 1] != '\n';
                    bool needSuffixNl = caret < text.Length && text[caret] != '\n' && text[caret] != '\r';

                    string insertion = (needPrefixNl && text.Length > 0 ? Environment.NewLine : string.Empty)
                                     + string.Join(Environment.NewLine, toInsert)
                                     + (needSuffixNl ? Environment.NewLine : string.Empty);

                    BulkLinksBox.Text = text.Substring(0, caret) + insertion + text.Substring(caret);
                    BulkLinksBox.CaretIndex = caret + insertion.Length;

                    e.Handled = true;
                }
                catch
                {
                    // Swallow and let default paste occur on any unexpected error
                }
            }
        }

        private static List<string> ExtractUrls(string text)
        {
            var urls = new List<string>();
            if (string.IsNullOrEmpty(text)) return urls;

            // Find tokens resembling URLs
            var regex = new Regex(@"https?://[^\s]+", RegexOptions.IgnoreCase);
            foreach (Match m in regex.Matches(text))
            {
                var u = m.Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}');
                if (Uri.IsWellFormedUriString(u, UriKind.Absolute))
                {
                    urls.Add(u);
                }
            }

            // If no regex hits and entire text is a single URL
            if (urls.Count == 0)
            {
                var t = text.Trim();
                if (Uri.IsWellFormedUriString(t, UriKind.Absolute)) urls.Add(t);
            }

            // De-dup within pasted content
            return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public class AddLinkEntry
    {
        public string Url { get; set; } = string.Empty;
        public string DownloadType { get; set; } = "Full Video";
        public string StartTime { get; set; } = "00:00:00";
        public string EndTime { get; set; } = "00:05:00";
    }
}
