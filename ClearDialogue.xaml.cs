using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Dark.Net;

namespace YouTubeDownloader
{
    public partial class ClearDialog : Window
    {
        public enum ClearOption { None, Completed, All }
        public ClearOption SelectedOption { get; private set; } = ClearOption.None;

        public ClearDialog()
        {
            InitializeComponent();

            DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark);
        }

        private void ClearCompleted_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = ClearOption.Completed;
            DialogResult = true;
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = ClearOption.All;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
