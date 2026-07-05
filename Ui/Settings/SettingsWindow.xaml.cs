using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// The WPF Options window (R8) — a Windows-11-Settings-style rail + cards replacing the 9-tab WinForms
    /// OptionsDlg. Pages bind live to <c>Settings.Default</c>; OK commits (Save), Cancel/X reverts (Reload)
    /// through <see cref="SettingsSession"/>. R8a implements Controller Modes, Hotkeys, and General; the
    /// remaining pages (Multi-Click, Auto-Find, Colors, Custom Modes, Layout Presets) arrive in R8b/R8c.
    /// </summary>
    public partial class SettingsWindow : FluentWindow
    {
        private readonly SettingsSession _session = new SettingsSession();

        public SettingsWindow()
        {
            InitializeComponent();
            // Every page binds directly to the live settings object (matching the old dialog's data-bindings).
            DataContext = Properties.Settings.Default;
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (pageModes == null)
                return; // still initializing

            pageModes.Visibility = Visibility.Collapsed;
            pageHotkeys.Visibility = Visibility.Collapsed;
            pageGeneral.Visibility = Visibility.Collapsed;

            switch (navList.SelectedIndex)
            {
                case 0: pageModes.Visibility = Visibility.Visible; break;
                case 1: pageHotkeys.Visibility = Visibility.Visible; break;
                case 2: pageGeneral.Visibility = Visibility.Visible; break;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            _session.Commit();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _session.Discard();
            DialogResult = false;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            // Any close path that isn't OK (X button, Alt+F4) discards the live-bound edits, exactly like the
            // WinForms dialog's OnFormClosing Reload(). Commit/Discard are idempotent (guarded).
            if (DialogResult != true)
                _session.Discard();
        }
    }
}
