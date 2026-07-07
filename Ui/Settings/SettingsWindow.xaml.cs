using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// The WPF Options window (R8): a Windows-11-Settings-style rail + cards replacing the 9-tab WinForms
    /// OptionsDlg. Pages bind live to <c>Settings.Default</c>; OK commits (Save), Cancel/X reverts (Reload)
    /// through <see cref="SettingsSession"/>. R8a implements Controller Modes, Hotkeys, and General; the
    /// remaining pages (Multi-Click, Auto-Find, Colors, Custom Modes, Layout Presets) arrive in R8b/R8c.
    /// </summary>
    public partial class SettingsWindow : FluentWindow
    {
        private readonly SettingsSession _session = new SettingsSession();

        private System.Collections.Generic.Dictionary<string, System.Windows.FrameworkElement> _pages;
        private readonly KeyBindingsEditor _keyBindings = new KeyBindingsEditor();
        private readonly CustomModesEditor _customModes;
        private readonly LayoutPresetsEditor _layoutPresets = new LayoutPresetsEditor();

        public SettingsWindow() : this(null) { }

        /// <summary>
        /// <paramref name="toons"/> is the current toon list (index + friendly label) used by the Custom Modes
        /// target dropdowns; pass it from the main window so rules can target toons by name instead of a bare number.
        /// </summary>
        internal SettingsWindow(System.Collections.Generic.IReadOnlyList<CustomModeToonOption> toons)
        {
            _customModes = new CustomModesEditor(toons);
            InitializeComponent();
            TTMulti.Ui.Controls.AppLogo.ApplyAppIcon(this, titleBar);

            // The XAML sets a roomy 1080x720 default; clamp it to 90% of the display's work area so the window
            // still opens fully on-screen on smaller monitors. MinWidth/MinHeight remain the floor.
            System.Windows.Rect workArea = SystemParameters.WorkArea;
            Width = System.Math.Min(Width, workArea.Width * 0.9);
            Height = System.Math.Min(Height, workArea.Height * 0.9);
            // Every page binds directly to the live settings object (matching the old dialog's data-bindings).
            DataContext = Properties.Settings.Default;

            // The keybindings grid owns non-Settings state (serialized XML), committed inside the transaction.
            _session.Register(_keyBindings);
            keyBindingsItems.ItemsSource = _keyBindings.Rows;

            // The custom-modes editor owns the custom-modes JSON file, committed inside the transaction.
            _session.Register(_customModes);
            pageCustomModes.DataContext = _customModes;

            // The layout-presets editor owns the layout-presets JSON file, committed inside the transaction.
            _session.Register(_layoutPresets);
            pageLayoutPresets.DataContext = _layoutPresets;

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
                { "General", pageGeneral },
                { "Appearance", pageAppearance },
                { "Keybinds", pageKeyBindings },
                { "Controller Modes", pageModes },
                { "Custom Modes", pageCustomModes },
                { "Multi-Click", pageMultiClick },
                { "Window Management", pageWindowManagement },
                { "Layout Presets", pageLayoutPresets },
            };

            // The rail's SelectionChanged fired during InitializeComponent (before _pages existed) and no-op'd,
            // so sync the visible page to the initially-selected rail item now instead of relying on each
            // page's XAML default Visibility matching the selection.
            NavList_SelectionChanged(navList, null);
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_pages == null)
                return; // still initializing

            string selected = (navList.SelectedItem as ListBoxItem)?.Tag as string;
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

        private void CmDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CustomModeBindingViewModel rule)
                _customModes.RemoveBinding(rule);
        }

        // ── Layout presets (inline editor on the Layout Presets page) ──

        private void LpAddPreset_Click(object sender, RoutedEventArgs e) => _layoutPresets.AddPreset();

        private void LpDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            var selected = _layoutPresets.SelectedPreset;
            if (selected == null)
                return;
            string name = string.IsNullOrWhiteSpace(selected.Name) ? "this preset" : selected.Name;
            if (System.Windows.MessageBox.Show(this, "Delete layout preset \"" + name + "\"?", "Delete Preset",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes)
                _layoutPresets.RemovePreset(selected);
        }

        private void LpAddRegion_Click(object sender, RoutedEventArgs e) =>
            _layoutPresets.SelectedPreset?.AddRegion();

        private void LpDeleteRegion_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TTMulti.Ui.ViewModels.LayoutRegionViewModel region)
                _layoutPresets.SelectedPreset?.RemoveRegion(region);
        }

        private void LpAdjustOnScreen_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is TTMulti.Ui.ViewModels.LayoutRegionViewModel vm))
                return;
            var region = vm.Region;
            var item = new TTMulti.Forms.LayoutOverlayForm.RegionOverlayItem
            {
                Rect = LayoutPresetBuilder.GetRegionRect(region),
                Rows = System.Math.Max(1, region.Rows),
                Cols = System.Math.Max(1, region.Cols),
            };
            using (var overlay = new TTMulti.Forms.LayoutOverlayForm(
                new System.Collections.Generic.List<TTMulti.Forms.LayoutOverlayForm.RegionOverlayItem> { item }))
            {
                if (overlay.ShowDialog(WinFormsOwner()) != System.Windows.Forms.DialogResult.OK)
                    return;
                region.Source = LayoutRegionSource.Custom;
                region.CustomX = item.Rect.X;
                region.CustomY = item.Rect.Y;
                region.CustomWidth = item.Rect.Width;
                region.CustomHeight = item.Rect.Height;
            }
            vm.RaiseAll();
        }

        private void LpAdjustSlotsOnScreen_Click(object sender, RoutedEventArgs e)
        {
            var vm = _layoutPresets.SelectedPreset;
            if (vm == null)
                return;
            var preset = vm.Preset;
            var slots = LayoutPresetBuilder.BuildSlots(preset);
            if (slots == null || slots.Count == 0)
            {
                System.Windows.MessageBox.Show(this, "Add at least one grid first, so there are windows to adjust.",
                    "Layout Presets", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // One draggable/resizable numbered box per window slot.
            var list = new System.Collections.Generic.List<TTMulti.Forms.LayoutOverlayForm.RegionOverlayItem>();
            for (int i = 0; i < slots.Count; i++)
                list.Add(new TTMulti.Forms.LayoutOverlayForm.RegionOverlayItem { Rect = slots[i].Rect, Rows = 1, Cols = 1, SlotIndex = i + 1 });

            using (var overlay = new TTMulti.Forms.LayoutOverlayForm(list))
            {
                if (overlay.ShowDialog(WinFormsOwner()) != System.Windows.Forms.DialogResult.OK)
                    return;

                var existingBySlot = new System.Collections.Generic.Dictionary<int, SlotOverride>();
                foreach (var o in preset.SlotOverrides ?? new System.Collections.Generic.List<SlotOverride>())
                    existingBySlot[o.SlotIndex] = o;
                var edited = new System.Collections.Generic.HashSet<int>();
                var newOverrides = new System.Collections.Generic.List<SlotOverride>();
                foreach (var it in list)
                {
                    if (it.SlotIndex < 1)
                        continue;
                    edited.Add(it.SlotIndex);
                    existingBySlot.TryGetValue(it.SlotIndex, out var existing);
                    newOverrides.Add(new SlotOverride
                    {
                        SlotIndex = it.SlotIndex,
                        Rect = LayoutRect.FromRectangle(it.Rect),
                        Minimized = existing?.Minimized, // preserve any minimize flag
                    });
                }
                // Keep overrides for slots that weren't part of this edit (e.g. minimize-only rows beyond the grid).
                foreach (var o in preset.SlotOverrides ?? new System.Collections.Generic.List<SlotOverride>())
                    if (!edited.Contains(o.SlotIndex))
                        newOverrides.Add(o);

                vm.SetSlotOverridesAndRefresh(newOverrides);
            }
        }

        private System.Windows.Forms.IWin32Window WinFormsOwner() =>
            new WpfWin32Owner(new System.Windows.Interop.WindowInteropHelper(this).Handle);

        private sealed class WpfWin32Owner : System.Windows.Forms.IWin32Window
        {
            public WpfWin32Owner(System.IntPtr handle) { Handle = handle; }
            public System.IntPtr Handle { get; }
        }

        private TTMulti.Ui.LayoutPreviewWindow _previewWindow;

        private void LpPopOutPreview_Click(object sender, RoutedEventArgs e)
        {
            if (_previewWindow == null)
            {
                _previewWindow = new TTMulti.Ui.LayoutPreviewWindow(_layoutPresets) { Owner = this };
                _previewWindow.Closed += (s, ev) => { _previewWindow = null; _layoutPresets.PreviewPoppedOut = false; };
                _layoutPresets.PreviewPoppedOut = true; // hide the inline preview while it lives in the pop-out
                _previewWindow.Show();
            }
            else
            {
                _previewWindow.Activate();
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
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
            // Close the pop-out preview along with Options so it doesn't linger.
            _previewWindow?.Close();
            _previewWindow = null;
            // Any close path that isn't OK (X button, Alt+F4) discards the live-bound edits, exactly like the
            // WinForms dialog's OnFormClosing Reload(). Commit/Discard are idempotent (guarded).
            if (DialogResult != true)
                _session.Discard();
        }
    }
}
