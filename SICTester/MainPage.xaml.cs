using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace STE.TGL.SIPanel
{
    /// <summary>
    /// 可以在本身使用或巡覽至框架內的空白頁面。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        SvcWorker _Worker = null;

        public MainPage()
        {
            this.InitializeComponent();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            _Worker = new SvcWorker(null);
            _Worker.Start();
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            _Worker.Shutdown();
            _Worker.Dispose();
            _Worker = null;
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Window.Current.Close();
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            btnStart_Click(btnStart, new RoutedEventArgs());
        }
    }
}
