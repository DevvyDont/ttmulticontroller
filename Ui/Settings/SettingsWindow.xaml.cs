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

        private System.Collections.Generic.Dictionary<string, System.Windows.FrameworkElement> _pages;
        private readonly KeyBindingsEditor _keyBindings = new KeyBindingsEditor();
        private readonly CustomModesEditor _customModes = new CustomModesEditor();

        public SettingsWindow()
        {
            InitializeComponent();
            // Every page binds directly to the live settings object (matching the old dialog's data-bindings).
            DataContext = Properties.Settings.Default;

            // The keybindings grid owns non-Settings state (serialized XML), committed inside the transaction.
            _session.Register(_keyBindings);
            keyBindingsItems.ItemsSource = _keyBindings.Rows;

            // The custom-modes editor owns the custom-modes JSON file, committed inside the transaction.
            _session.Register(_customModes);
            pageCustomModes.DataContext = _customModes;

            // Sub-panels whose controls need read-modify-write over a shared setting get their own DataContext.
            switchKeyPanel.DataContext = new SwitchKeyViewModel();
            autoFindModifiersPanel.DataContext = new ModifierFlagsViewModel(
                () => Properties.Settings.Default.autoFindWindowsKeyModifiers,
                v => Properties.Settings.Default.autoFindWindowsKeyModifiers = v);
            minimizeModifiersPanel.DataContext = new ModifierFlagsViewModel(
                () => Properties.Settings.Default.minimizeUnconnectedKeyModifiers,
                v => Properties.Settings.Default.minimizeUnconnectedKeyModifiers = v);

            _pages = new System.Collections.Generic.Dictionary<string, System.Windows.FrameworkElement>
            {
                { "Multi-Mode Keys", pageKeyBindings },
                { "Controller Modes", pageModes },
                { "Custom Modes", pageCustomModes },
                { "Hotkeys", pageHotkeys },
                { "Multi-Click", pageMultiClick },
                { "Auto-Find", pageAutoFind },
                { "Colors", pageColors },
                { "General", pageGeneral },
            };
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_pages == null)
                return; // still initializing

            string selected = (navList.SelectedItem as ListBoxItem)?.Content as string;
            foreach (var kv in _pages)
                kv.Value.Visibility = kv.Key == selected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddBinding_Click(object sender, RoutedEventArgs e) => _keyBindings.AddNew();

        private void RemoveBinding_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is KeyMappingRowViewModel row)
                _keyBindings.Remove(row);
        }

        private void CmAddMode_Click(object sender, RoutedEventArgs e) => _customModes.AddMode();

        private void CmRemoveMode_Click(object sender, RoutedEventArgs e)
        {
            if (_customModes.SelectedMode != null &&
                System.Windows.MessageBox.Show(this, "Remove this custom mode?", "Confirm",
                    System.Windows.MessageBoxButton.YesNo) == System.Windows.MessageBoxResult.Yes)
            {
                _customModes.RemoveMode(_customModes.SelectedMode);
            }
        }

        private void CmAddBinding_Click(object sender, RoutedEventArgs e) => _customModes.AddBinding();

        private void CmRemoveBinding_Click(object sender, RoutedEventArgs e) =>
            _customModes.RemoveBinding(_customModes.SelectedBinding);

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
