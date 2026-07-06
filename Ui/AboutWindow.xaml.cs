using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Wpf.Ui.Controls;

namespace TTMulti.Ui
{
    /// <summary>WPF replacement for the WinForms AboutWnd: app identity, version, homepage, update check.</summary>
    public partial class AboutWindow : FluentWindow
    {
        public AboutWindow()
        {
            InitializeComponent();

            // Colour the logo's two cat heads with the user's Multi (front) and Mirror (back) mode colours.
            appLogo.LeftBrush = ToBrush(Colors.LeftGroup);
            appLogo.RightBrush = ToBrush(Colors.AllGroups);
            Controls.AppLogo.ApplyAppIcon(this, titleBar);
            versionText.Text = "Version " + UpdateChecker.CurrentVersion;

            string url = Properties.Settings.Default.homepageUrl;
            homepageRun.Text = url;
            try { homepageLink.NavigateUri = new Uri(url); }
            catch { homepageLink.IsEnabled = false; }
        }

        private static System.Windows.Media.SolidColorBrush ToBrush(System.Drawing.Color c)
        {
            var b = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private void Homepage_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            OpenUrl(e.Uri.AbsoluteUri);
            e.Handled = true;
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            checkUpdatesButton.IsEnabled = false;
            try
            {
                UpdateCheckResult result = await UpdateChecker.CheckAsync();
                switch (result.Status)
                {
                    case UpdateStatus.CheckFailed:
                        System.Windows.MessageBox.Show(this,
                            "Could not check for updates. Please check your internet connection and try again later.",
                            "Update check failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        break;

                    case UpdateStatus.UpToDate:
                        System.Windows.MessageBox.Show(this, "You already have the latest version.",
                            "No updates available", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        break;

                    case UpdateStatus.UpdateAvailable:
                        string message = string.Format(
                            "An update is available: {0} (you have {1}).\n\nWould you like to open the download page?",
                            result.LatestTag, result.CurrentVersion);
                        if (System.Windows.MessageBox.Show(this, message, "Update available",
                                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information) == System.Windows.MessageBoxResult.Yes)
                        {
                            OpenUrl(result.DownloadUrl);
                        }
                        break;
                }
            }
            finally
            {
                checkUpdatesButton.IsEnabled = true;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static void OpenUrl(string url)
        {
            // UseShellExecute is required to launch a URL: it defaults to false on modern .NET.
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* No browser / blocked; nothing useful to do. */ }
        }
    }
}
