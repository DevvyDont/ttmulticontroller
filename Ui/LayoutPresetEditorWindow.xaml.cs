using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using TTMulti.Forms;
using TTMulti.Ui.ViewModels;
using Wpf.Ui.Controls;

namespace TTMulti.Ui
{
    /// <summary>
    /// WPF replacement for the WinForms LayoutPresetEditorForm: edits a single layout preset (name, hotkey,
    /// regions, slot overrides). The PropertyGrid becomes a bound region editor and the DataGridView a WPF
    /// DataGrid; the "on screen" editors and the monitor picker still use the kept WinForms overlay forms.
    /// The preset is edited in place; <see cref="Preset"/> is read by the caller when the dialog returns true.
    /// </summary>
    public partial class LayoutPresetEditorWindow : FluentWindow, INotifyPropertyChanged
    {
        private readonly LayoutPreset _preset;
        private LayoutRegionViewModel _selectedRegion;

        public LayoutPreset Preset => _preset;

        public ObservableCollection<LayoutRegionViewModel> Regions { get; } = new ObservableCollection<LayoutRegionViewModel>();
        public ObservableCollection<SlotOverrideRow> SlotRows { get; } = new ObservableCollection<SlotOverrideRow>();

        public LayoutRegionViewModel SelectedRegion
        {
            get => _selectedRegion;
            set { _selectedRegion = value; OnPropertyChanged(); }
        }

        public LayoutPresetEditorWindow(LayoutPreset preset)
        {
            _preset = preset ?? new LayoutPreset();
            if (_preset.Regions == null) _preset.Regions = new List<LayoutRegion>();
            if (_preset.SlotOverrides == null) _preset.SlotOverrides = new List<SlotOverride>();

            InitializeComponent();
            Controls.AppLogo.ApplyAppIcon(this, titleBar);
            DataContext = this;

            nameBox.Text = _preset.Name ?? "";
            hotkeyBox.KeyCode = _preset.HotkeyCode;
            var mods = (Win32.KeyModifiers)_preset.HotkeyModifiers;
            hotkeyAlt.IsChecked = (mods & Win32.KeyModifiers.Alt) != 0;
            hotkeyCtrl.IsChecked = (mods & Win32.KeyModifiers.Control) != 0;
            hotkeyShift.IsChecked = (mods & Win32.KeyModifiers.Shift) != 0;

            RebuildRegions();
            SelectedRegion = Regions.FirstOrDefault();
            RebuildSlotRows();
        }

        private void RebuildRegions()
        {
            Regions.Clear();
            foreach (var region in _preset.Regions)
                Regions.Add(new LayoutRegionViewModel(region));
        }

        // ── Regions ─────────────────────────────────────────────────────────────────

        private void AddRegion_Click(object sender, RoutedEventArgs e)
        {
            var region = new LayoutRegion { Source = LayoutRegionSource.Monitor, MonitorIndex = 0, Rows = 2, Cols = 2 };
            _preset.Regions.Add(region);
            var vm = new LayoutRegionViewModel(region);
            Regions.Add(vm);
            SelectedRegion = vm;
            RebuildSlotRows();
        }

        private void RemoveRegion_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRegion == null)
                return;
            _preset.Regions.Remove(SelectedRegion.Region);
            Regions.Remove(SelectedRegion);
            SelectedRegion = Regions.FirstOrDefault();
            RebuildSlotRows();
        }

        // ── On-screen editors (kept WinForms overlays) ──────────────────────────────

        private void SetAreaOnScreen_Click(object sender, RoutedEventArgs e)
        {
            var vm = SelectedRegion;
            if (vm == null)
                return;
            var region = vm.Region;
            var overlayItem = new LayoutOverlayForm.RegionOverlayItem
            {
                Rect = LayoutPresetBuilder.GetRegionRect(region),
                Rows = Math.Max(1, region.Rows),
                Cols = Math.Max(1, region.Cols),
            };
            using (var overlay = new LayoutOverlayForm(new List<LayoutOverlayForm.RegionOverlayItem> { overlayItem }))
            {
                if (overlay.ShowDialog(WinFormsOwner()) != System.Windows.Forms.DialogResult.OK)
                    return;
                region.Source = LayoutRegionSource.Custom;
                region.CustomX = overlayItem.Rect.X;
                region.CustomY = overlayItem.Rect.Y;
                region.CustomWidth = overlayItem.Rect.Width;
                region.CustomHeight = overlayItem.Rect.Height;
            }
            vm.RefreshAll();
            RefreshRegionDisplay();
        }

        private void EditAllOnScreen_Click(object sender, RoutedEventArgs e)
        {
            if (_preset.Regions.Count == 0)
                return;
            var list = new List<LayoutOverlayForm.RegionOverlayItem>();
            int nextSlot = 1;
            foreach (var region in _preset.Regions)
            {
                int rows = Math.Max(1, region.Rows);
                int cols = Math.Max(1, region.Cols);
                list.Add(new LayoutOverlayForm.RegionOverlayItem
                {
                    Rect = LayoutPresetBuilder.GetRegionRect(region),
                    Rows = rows,
                    Cols = cols,
                    StartSlotIndex = nextSlot,
                });
                nextSlot += rows * cols;
            }
            using (var overlay = new LayoutOverlayForm(list))
            {
                if (overlay.ShowDialog(WinFormsOwner()) != System.Windows.Forms.DialogResult.OK)
                    return;
                for (int i = 0; i < _preset.Regions.Count && i < list.Count; i++)
                {
                    var region = _preset.Regions[i];
                    region.Source = LayoutRegionSource.Custom;
                    region.CustomX = list[i].Rect.X;
                    region.CustomY = list[i].Rect.Y;
                    region.CustomWidth = list[i].Rect.Width;
                    region.CustomHeight = list[i].Rect.Height;
                }
            }
            foreach (var vm in Regions) vm.RefreshAll();
            RefreshRegionDisplay();
        }

        private void EditSlotsOnScreen_Click(object sender, RoutedEventArgs e)
        {
            var slots = LayoutPresetBuilder.BuildSlots(_preset);
            if (slots == null || slots.Count == 0)
            {
                System.Windows.MessageBox.Show(this, "Add at least one region with a grid to define slots first.",
                    "No slots", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            var list = new List<LayoutOverlayForm.RegionOverlayItem>();
            for (int i = 0; i < slots.Count; i++)
                list.Add(new LayoutOverlayForm.RegionOverlayItem { Rect = slots[i].Rect, Rows = 1, Cols = 1, SlotIndex = i + 1 });

            using (var overlay = new LayoutOverlayForm(list))
            {
                if (overlay.ShowDialog(WinFormsOwner()) != System.Windows.Forms.DialogResult.OK)
                    return;
                var overridesBySlot = _preset.SlotOverrides.ToDictionary(o => o.SlotIndex);
                var editedSlots = new HashSet<int>(list.Where(x => x.SlotIndex >= 0).Select(x => x.SlotIndex));
                var newOverrides = new List<SlotOverride>();
                foreach (var item in list)
                {
                    if (item.SlotIndex < 0) continue;
                    var existing = overridesBySlot.TryGetValue(item.SlotIndex, out var ov) ? ov : null;
                    newOverrides.Add(new SlotOverride
                    {
                        SlotIndex = item.SlotIndex,
                        Rect = LayoutRect.FromRectangle(item.Rect),
                        Minimized = existing?.Minimized,
                    });
                }
                foreach (var ov in _preset.SlotOverrides)
                {
                    if (!editedSlots.Contains(ov.SlotIndex))
                        newOverrides.Add(ov);
                }
                _preset.SlotOverrides = newOverrides;
            }
            RebuildSlotRows();
        }

        private void PickMonitor_Click(object sender, RoutedEventArgs e)
        {
            var vm = SelectedRegion;
            if (vm == null)
                return;
            using (var picker = new MonitorPickerForm())
            {
                if (picker.ShowDialog(WinFormsOwner()) != System.Windows.Forms.DialogResult.OK)
                    return;
                vm.MonitorIndex = picker.SelectedMonitorIndex;
                vm.RefreshAll();
                RefreshRegionDisplay();
            }
        }

        // ── Slot overrides grid ─────────────────────────────────────────────────────

        private void RebuildSlotRows()
        {
            SlotRows.Clear();
            var overridesBySlot = _preset.SlotOverrides.ToDictionary(o => o.SlotIndex);
            var defaultSlots = LayoutPresetBuilder.BuildSlots(_preset);
            int maxSlot = defaultSlots.Count;
            if (overridesBySlot.Count > 0)
                maxSlot = Math.Max(maxSlot, overridesBySlot.Keys.Max());
            if (maxSlot < 1) maxSlot = 1;

            for (int slot = 1; slot <= maxSlot; slot++)
            {
                var ov = overridesBySlot.TryGetValue(slot, out var o) ? o : null;
                var rect = ov?.Rect != null
                    ? ov.Rect.ToRectangle()
                    : (slot <= defaultSlots.Count ? defaultSlots[slot - 1].Rect : new System.Drawing.Rectangle());
                SlotRows.Add(new SlotOverrideRow
                {
                    Slot = slot,
                    OverrideRect = ov?.Rect != null,
                    X = rect.X,
                    Y = rect.Y,
                    Width = rect.Width,
                    Height = rect.Height,
                    Minimized = ov?.Minimized ?? false,
                });
            }
        }

        // ── OK / Cancel ─────────────────────────────────────────────────────────────

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // Commit any in-progress DataGrid cell edit before reading the rows.
            slotGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

            _preset.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? "Unnamed" : nameBox.Text.Trim();
            _preset.HotkeyCode = hotkeyBox.KeyCode;
            Win32.KeyModifiers mods = Win32.KeyModifiers.None;
            if (hotkeyAlt.IsChecked == true) mods |= Win32.KeyModifiers.Alt;
            if (hotkeyCtrl.IsChecked == true) mods |= Win32.KeyModifiers.Control;
            if (hotkeyShift.IsChecked == true) mods |= Win32.KeyModifiers.Shift;
            _preset.HotkeyModifiers = (int)mods;

            var overrides = new List<SlotOverride>();
            foreach (var row in SlotRows)
            {
                if (row.Slot < 1) continue;
                if (!row.OverrideRect && !row.Minimized) continue;
                var ov = new SlotOverride { SlotIndex = row.Slot, Minimized = row.Minimized };
                if (row.OverrideRect)
                    ov.Rect = new LayoutRect { X = row.X, Y = row.Y, Width = row.Width, Height = row.Height };
                overrides.Add(ov);
            }
            _preset.SlotOverrides = overrides;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RefreshRegionDisplay()
        {
            // The ListBox shows DisplayName; RefreshAll already raised it, but re-selecting forces the label.
            var selected = SelectedRegion;
            regionList.Items.Refresh();
            SelectedRegion = selected;
        }

        private System.Windows.Forms.IWin32Window WinFormsOwner() =>
            new WpfWin32Owner(new WindowInteropHelper(this).Handle);

        private sealed class WpfWin32Owner : System.Windows.Forms.IWin32Window
        {
            public WpfWin32Owner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>One editable row of the slot-overrides grid.</summary>
        public sealed class SlotOverrideRow
        {
            public int Slot { get; set; }
            public bool OverrideRect { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public bool Minimized { get; set; }
        }
    }
}
