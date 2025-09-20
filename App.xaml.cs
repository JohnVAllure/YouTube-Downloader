using System.Runtime;
using System.Windows;
using System.Windows.Media;
using Dark.Net;

namespace YouTubeDownloader
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DarkNet.Instance.SetCurrentProcessTheme(Theme.Auto);
        }
    }
}
