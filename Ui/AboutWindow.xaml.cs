using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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
                        await HandleUpdateAvailableAsync(result);
                        break;
                }
            }
            finally
            {
                checkUpdatesButton.IsEnabled = true;
            }
        }

        private CancellationTokenSource _updateCts;

        /// <summary>
        /// Offer to install the update in place (download + swap + restart) when we can write next to the exe and the
        /// release has an exe asset; otherwise fall back to opening the download page in a browser.
        /// </summary>
        private async Task HandleUpdateAvailableAsync(UpdateCheckResult result)
        {
            bool canSelfUpdate = !string.IsNullOrEmpty(result.AssetDownloadUrl) && SelfUpdater.CanWriteToExeDir();

            string prompt = canSelfUpdate
                ? string.Format("An update is available: {0} (you have {1}).\n\nDownload and install it now? The app will close and reopen.",
                    result.LatestTag, result.CurrentVersion)
                : string.Format("An update is available: {0} (you have {1}).\n\nWould you like to open the download page?",
                    result.LatestTag, result.CurrentVersion);

            if (System.Windows.MessageBox.Show(this, prompt, "Update available",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information) != System.Windows.MessageBoxResult.Yes)
                return;

            if (!canSelfUpdate)
            {
                OpenUrl(result.DownloadUrl);
                return;
            }

            await DownloadAndApplyAsync(result);
        }

        private async Task DownloadAndApplyAsync(UpdateCheckResult result)
        {
            _updateCts = new CancellationTokenSource();
            updatePanel.Visibility = Visibility.Visible;
            updateProgress.Value = 0;
            updateStatusText.Text = "Downloading update...";

            var progress = new Progress<double>(p =>
            {
                updateProgress.Value = p;
                updateStatusText.Text = string.Format("Downloading update... {0:P0}", p);
            });

            try
            {
                string newExe = await SelfUpdater.DownloadAsync(result.AssetDownloadUrl, progress, _updateCts.Token);
                updateStatusText.Text = "Installing and restarting...";
                SelfUpdater.ApplyAndRestart(newExe); // does not return on success: the app shuts down and relaunches
            }
            catch (OperationCanceledException)
            {
                updatePanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                updatePanel.Visibility = Visibility.Collapsed;
                System.Windows.MessageBox.Show(this,
                    "Automatic update failed: " + ex.Message + "\n\nOpening the download page instead.",
                    "Update", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                OpenUrl(result.DownloadUrl);
            }
            finally
            {
                _updateCts?.Dispose();
                _updateCts = null;
            }
        }

        private void UpdateCancel_Click(object sender, RoutedEventArgs e) => _updateCts?.Cancel();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static void OpenUrl(string url)
        {
            // UseShellExecute is required to launch a URL: it defaults to false on modern .NET.
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* No browser / blocked; nothing useful to do. */ }
        }
    }
}
