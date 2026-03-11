using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using TTMulti;
using TTMulti.Controls;

namespace TTMulti.Forms
{
    /// <summary>
    /// Modal dialog to edit a single layout preset: name, hotkey, regions, and slot overrides.
    /// </summary>
    public partial class LayoutPresetEditorForm : Form
    {
        private LayoutPreset _preset;
        private ListBox _regionListBox;
        private Button _addRegionBtn;
        private Button _removeRegionBtn;
        private PropertyGrid _regionPropertyGrid;
        private DataGridView _slotOverridesGrid;
        private Button _setAreaOnScreenBtn;
        private Button _editAllOnScreenBtn;
        private Button _editSlotsOnScreenBtn;
        private Button _pickMonitorBtn;
        private TextBox _nameTextBox;
        private KeyPicker _hotkeyPicker;
        private CheckBox _hotkeyAlt;
        private CheckBox _hotkeyCtrl;
        private CheckBox _hotkeyShift;

        public LayoutPreset Preset => _preset;

        public LayoutPresetEditorForm(LayoutPreset preset)
        {
            _preset = preset ?? new LayoutPreset();
            if (_preset.Regions == null) _preset.Regions = new List<LayoutRegion>();
            if (_preset.SlotOverrides == null) _preset.SlotOverrides = new List<SlotOverride>();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Edit Layout Preset";
            this.Size = new Size(700, 550);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(600, 450);

            int y = 10;

            var nameLabel = new Label { Text = "Name:", Location = new Point(10, y), Size = new Size(40, 20) };
            this.Controls.Add(nameLabel);
            _nameTextBox = new TextBox
            {
                Location = new Point(55, y - 2),
                Size = new Size(300, 23),
                Text = _preset.Name ?? ""
            };
            this.Controls.Add(_nameTextBox);
            y += 35;

            var hotkeyLabel = new Label { Text = "Hotkey:", Location = new Point(10, y), Size = new Size(45, 20) };
            this.Controls.Add(hotkeyLabel);
            _hotkeyPicker = new KeyPicker { Location = new Point(60, y - 2), Size = new Size(120, 23) };
            _hotkeyPicker.ChosenKey = (Keys)_preset.HotkeyCode;
            this.Controls.Add(_hotkeyPicker);
            _hotkeyAlt = new CheckBox { Text = "Alt", Location = new Point(190, y), Size = new Size(45, 20) };
            _hotkeyCtrl = new CheckBox { Text = "Ctrl", Location = new Point(240, y), Size = new Size(50, 20) };
            _hotkeyShift = new CheckBox { Text = "Shift", Location = new Point(295, y), Size = new Size(55, 20) };
            var mods = (Win32.KeyModifiers)_preset.HotkeyModifiers;
            _hotkeyAlt.Checked = (mods & Win32.KeyModifiers.Alt) != 0;
            _hotkeyCtrl.Checked = (mods & Win32.KeyModifiers.Control) != 0;
            _hotkeyShift.Checked = (mods & Win32.KeyModifiers.Shift) != 0;
            this.Controls.Add(_hotkeyAlt);
            this.Controls.Add(_hotkeyCtrl);
            this.Controls.Add(_hotkeyShift);
            y += 35;

            var regionsLabel = new Label { Text = "Regions (define grid areas; slots are filled from regions in order):", Location = new Point(10, y), Size = new Size(450, 20) };
            this.Controls.Add(regionsLabel);
            y += 22;

            _regionListBox = new ListBox
            {
                Location = new Point(10, y),
                Size = new Size(220, 140),
                DisplayMember = "DisplayName"
            };
            _regionListBox.SelectedIndexChanged += RegionListBox_SelectedIndexChanged;
            this.Controls.Add(_regionListBox);
            _addRegionBtn = new Button { Text = "Add Region", Location = new Point(10, y + 145), Size = new Size(100, 28) };
            _addRegionBtn.Click += AddRegion_Click;
            this.Controls.Add(_addRegionBtn);
            _removeRegionBtn = new Button { Text = "Remove", Location = new Point(115, y + 145), Size = new Size(75, 28) };
            _removeRegionBtn.Click += RemoveRegion_Click;
            this.Controls.Add(_removeRegionBtn);
            _setAreaOnScreenBtn = new Button { Text = "Set area on screen...", Location = new Point(195, y + 145), Size = new Size(140, 28) };
            _setAreaOnScreenBtn.Click += SetAreaOnScreen_Click;
            this.Controls.Add(_setAreaOnScreenBtn);
            _editAllOnScreenBtn = new Button { Text = "Edit all on screen...", Location = new Point(340, y + 145), Size = new Size(130, 28) };
            _editAllOnScreenBtn.Click += EditAllOnScreen_Click;
            this.Controls.Add(_editAllOnScreenBtn);

            _pickMonitorBtn = new Button { Text = "Pick monitor...", Location = new Point(240, y), Size = new Size(110, 26) };
            _pickMonitorBtn.Click += PickMonitor_Click;
            this.Controls.Add(_pickMonitorBtn);

            _regionPropertyGrid = new PropertyGrid
            {
                Location = new Point(240, y + 28),
                Size = new Size(430, 106),
                PropertySort = PropertySort.Categorized,
                HelpVisible = false
            };
            _regionPropertyGrid.PropertyValueChanged += RegionPropertyGrid_PropertyValueChanged;
            HidePropertyGridToolbar(_regionPropertyGrid);
            this.Controls.Add(_regionPropertyGrid);
            y += 175;

            var slotsLabel = new Label { Text = "Slot overrides (optional; slot index 1-based, from regions in order):", Location = new Point(10, y), Size = new Size(450, 20) };
            this.Controls.Add(slotsLabel);
            _editSlotsOnScreenBtn = new Button { Text = "Edit slots on screen...", Location = new Point(470, y - 2), Size = new Size(150, 24) };
            _editSlotsOnScreenBtn.Click += EditSlotsOnScreen_Click;
            this.Controls.Add(_editSlotsOnScreenBtn);
            y += 22;

            _slotOverridesGrid = new DataGridView
            {
                Location = new Point(10, y),
                Size = new Size(660, 150),
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };
            _slotOverridesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Slot", HeaderText = "Slot", Width = 50 });
            _slotOverridesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "OverrideRect", HeaderText = "Override position", Width = 100 });
            _slotOverridesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "X", HeaderText = "X", Width = 60 });
            _slotOverridesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Y", HeaderText = "Y", Width = 60 });
            _slotOverridesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Width", HeaderText = "Width", Width = 60 });
            _slotOverridesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Height", HeaderText = "Height", Width = 60 });
            _slotOverridesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Minimized", HeaderText = "Minimized", Width = 80 });
            this.Controls.Add(_slotOverridesGrid);
            y += 160;

            var okBtn = new Button { Text = "OK", Location = new Point(400, y), Size = new Size(85, 28), DialogResult = DialogResult.OK };
            okBtn.Click += OkBtn_Click;
            var cancelBtn = new Button { Text = "Cancel", Location = new Point(495, y), Size = new Size(85, 28), DialogResult = DialogResult.Cancel };
            this.AcceptButton = okBtn;
            this.CancelButton = cancelBtn;
            this.Controls.Add(okBtn);
            this.Controls.Add(cancelBtn);

            RefreshRegionList();
            if (_preset.Regions.Count > 0)
                _regionListBox.SelectedIndex = 0;
            _editAllOnScreenBtn.Enabled = _preset.Regions.Count > 0;
            _editSlotsOnScreenBtn.Enabled = _preset.Regions.Count > 0;
            LoadSlotOverridesGrid();
        }

        private void RefreshRegionList()
        {
            _regionListBox.Items.Clear();
            for (int i = 0; i < _preset.Regions.Count; i++)
            {
                var r = _preset.Regions[i];
                string src = r.Source == LayoutRegionSource.Monitor ? $"Monitor {r.MonitorIndex + 1}" : "Custom";
                string grid = r.Rows > 0 && r.Cols > 0 ? $"{r.Rows}×{r.Cols}" : "1×1";
                _regionListBox.Items.Add(new RegionDisplayItem { Index = i, Region = r, DisplayName = $"{i + 1}. {src} {grid}" });
            }
        }

        private class RegionDisplayItem
        {
            public int Index;
            public LayoutRegion Region;
            public string DisplayName { get; set; }
        }

        private static void HidePropertyGridToolbar(PropertyGrid grid)
        {
            var pgType = typeof(PropertyGrid);
            var toolbarVisibleProp = pgType.GetProperty("ToolbarVisible", BindingFlags.Public | BindingFlags.Instance);
            if (toolbarVisibleProp != null)
            {
                try { toolbarVisibleProp.SetValue(grid, false); return; } catch { }
            }
            foreach (Control c in grid.Controls)
            {
                if (c.GetType().Name.IndexOf("ToolStrip", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    c.Visible = false;
                    break;
                }
            }
        }

        private void RegionPropertyGrid_PropertyValueChanged(object s, PropertyValueChangedEventArgs e)
        {
            if (e.ChangedItem?.PropertyDescriptor?.Name == "Source")
            {
                _regionPropertyGrid.Refresh();
            }
        }

        private void RegionListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var item = _regionListBox.SelectedItem as RegionDisplayItem;
            _regionPropertyGrid.SelectedObject = item?.Region;
            _removeRegionBtn.Enabled = item != null;
            _setAreaOnScreenBtn.Enabled = item != null;
            _pickMonitorBtn.Enabled = item != null;
        }

        private void AddRegion_Click(object sender, EventArgs e)
        {
            _preset.Regions.Add(new LayoutRegion { Source = LayoutRegionSource.Monitor, MonitorIndex = 0, Rows = 2, Cols = 2 });
            RefreshRegionList();
            _regionListBox.SelectedIndex = _preset.Regions.Count - 1;
            _editAllOnScreenBtn.Enabled = true;
            _editSlotsOnScreenBtn.Enabled = true;
        }

        private void RemoveRegion_Click(object sender, EventArgs e)
        {
            var item = _regionListBox.SelectedItem as RegionDisplayItem;
            if (item == null) return;
            _preset.Regions.RemoveAt(item.Index);
            RefreshRegionList();
            _editAllOnScreenBtn.Enabled = _preset.Regions.Count > 0;
            _editSlotsOnScreenBtn.Enabled = _preset.Regions.Count > 0;
            LoadSlotOverridesGrid();
        }

        private void SetAreaOnScreen_Click(object sender, EventArgs e)
        {
            var item = _regionListBox.SelectedItem as RegionDisplayItem;
            if (item == null) return;
            var region = item.Region;
            var rect = LayoutPresetBuilder.GetRegionRect(region);
            var overlayItem = new LayoutOverlayForm.RegionOverlayItem
            {
                Rect = rect,
                Rows = Math.Max(1, region.Rows),
                Cols = Math.Max(1, region.Cols)
            };
            var list = new List<LayoutOverlayForm.RegionOverlayItem> { overlayItem };
            using (var overlay = new LayoutOverlayForm(list))
            {
                if (overlay.ShowDialog(this) != DialogResult.OK) return;
                region.Source = LayoutRegionSource.Custom;
                region.CustomX = overlayItem.Rect.X;
                region.CustomY = overlayItem.Rect.Y;
                region.CustomWidth = overlayItem.Rect.Width;
                region.CustomHeight = overlayItem.Rect.Height;
            }
            RefreshRegionList();
            _regionPropertyGrid.Refresh();
        }

        private void EditSlotsOnScreen_Click(object sender, EventArgs e)
        {
            var slots = LayoutPresetBuilder.BuildSlots(_preset);
            if (slots == null || slots.Count == 0)
            {
                MessageBox.Show(this, "Add at least one region with a grid to define slots first.", "No slots", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var list = new List<LayoutOverlayForm.RegionOverlayItem>();
            for (int i = 0; i < slots.Count; i++)
            {
                list.Add(new LayoutOverlayForm.RegionOverlayItem
                {
                    Rect = slots[i].Rect,
                    Rows = 1,
                    Cols = 1,
                    SlotIndex = i + 1
                });
            }
            using (var overlay = new LayoutOverlayForm(list))
            {
                if (overlay.ShowDialog(this) != DialogResult.OK) return;
                var overridesBySlot = (_preset.SlotOverrides ?? new List<SlotOverride>()).ToDictionary(o => o.SlotIndex);
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
                        Minimized = existing?.Minimized
                    });
                }
                foreach (var ov in _preset.SlotOverrides ?? new List<SlotOverride>())
                {
                    if (!editedSlots.Contains(ov.SlotIndex))
                        newOverrides.Add(ov);
                }
                _preset.SlotOverrides = newOverrides;
            }
            LoadSlotOverridesGrid();
        }

        private void PickMonitor_Click(object sender, EventArgs e)
        {
            var item = _regionListBox.SelectedItem as RegionDisplayItem;
            if (item == null) return;
            using (var picker = new MonitorPickerForm())
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                item.Region.MonitorIndex = picker.SelectedMonitorIndex;
                int idx = item.Index;
                RefreshRegionList();
                if (idx >= 0 && idx < _regionListBox.Items.Count)
                    _regionListBox.SelectedIndex = idx;
                _regionPropertyGrid.Refresh();
            }
        }

        private void EditAllOnScreen_Click(object sender, EventArgs e)
        {
            if (_preset.Regions == null || _preset.Regions.Count == 0) return;
            var list = new List<LayoutOverlayForm.RegionOverlayItem>();
            int nextSlot = 1;
            foreach (var region in _preset.Regions)
            {
                var rect = LayoutPresetBuilder.GetRegionRect(region);
                int rows = Math.Max(1, region.Rows);
                int cols = Math.Max(1, region.Cols);
                list.Add(new LayoutOverlayForm.RegionOverlayItem
                {
                    Rect = rect,
                    Rows = rows,
                    Cols = cols,
                    StartSlotIndex = nextSlot
                });
                nextSlot += rows * cols;
            }
            using (var overlay = new LayoutOverlayForm(list))
            {
                if (overlay.ShowDialog(this) != DialogResult.OK) return;
                for (int i = 0; i < _preset.Regions.Count && i < list.Count; i++)
                {
                    var region = _preset.Regions[i];
                    var item = list[i];
                    region.Source = LayoutRegionSource.Custom;
                    region.CustomX = item.Rect.X;
                    region.CustomY = item.Rect.Y;
                    region.CustomWidth = item.Rect.Width;
                    region.CustomHeight = item.Rect.Height;
                }
            }
            RefreshRegionList();
            _regionPropertyGrid.Refresh();
        }

        private void LoadSlotOverridesGrid()
        {
            _slotOverridesGrid.Rows.Clear();
            var overridesBySlot = (_preset.SlotOverrides ?? new List<SlotOverride>()).ToDictionary(o => o.SlotIndex);
            var defaultSlots = LayoutPresetBuilder.BuildSlots(new LayoutPreset { Regions = _preset.Regions ?? new List<LayoutRegion>() });
            int totalSlots = LayoutPresetBuilder.BuildSlots(_preset).Count;
            int maxSlot = totalSlots;
            if (overridesBySlot.Count > 0)
                maxSlot = Math.Max(maxSlot, overridesBySlot.Keys.Max());
            if (maxSlot < 1) maxSlot = 1;
            for (int slot = 1; slot <= maxSlot; slot++)
            {
                var ov = overridesBySlot.TryGetValue(slot, out var o) ? o : null;
                bool overrideRect = ov?.Rect != null;
                var rect = ov?.Rect != null ? ov.Rect.ToRectangle() : (slot <= defaultSlots.Count ? defaultSlots[slot - 1].Rect : new Rectangle(0, 0, 0, 0));
                _slotOverridesGrid.Rows.Add(
                    slot,
                    overrideRect,
                    rect.X,
                    rect.Y,
                    rect.Width,
                    rect.Height,
                    ov?.Minimized ?? false
                );
            }
        }

        private void OkBtn_Click(object sender, EventArgs e)
        {
            _preset.Name = _nameTextBox.Text?.Trim() ?? "Unnamed";
            _preset.HotkeyCode = (int)_hotkeyPicker.ChosenKey;
            Win32.KeyModifiers mods = Win32.KeyModifiers.None;
            if (_hotkeyAlt.Checked) mods |= Win32.KeyModifiers.Alt;
            if (_hotkeyCtrl.Checked) mods |= Win32.KeyModifiers.Control;
            if (_hotkeyShift.Checked) mods |= Win32.KeyModifiers.Shift;
            _preset.HotkeyModifiers = (int)mods;

            _preset.SlotOverrides = new List<SlotOverride>();
            foreach (DataGridViewRow row in _slotOverridesGrid.Rows)
            {
                if (row.IsNewRow) continue;
                int slot = 0;
                if (row.Cells["Slot"].Value == null || !int.TryParse(row.Cells["Slot"].Value.ToString(), out slot) || slot < 1) continue;
                bool overrideRect = row.Cells["OverrideRect"].Value as bool? ?? false;
                bool minimized = row.Cells["Minimized"].Value as bool? ?? false;
                if (!overrideRect && !minimized) continue;
                var ov = new SlotOverride { SlotIndex = slot, Minimized = minimized };
                if (overrideRect)
                {
                    int x = 0, y = 0, w = 0, h = 0;
                    int.TryParse(row.Cells["X"].Value?.ToString(), out x);
                    int.TryParse(row.Cells["Y"].Value?.ToString(), out y);
                    int.TryParse(row.Cells["Width"].Value?.ToString(), out w);
                    int.TryParse(row.Cells["Height"].Value?.ToString(), out h);
                    ov.Rect = new LayoutRect { X = x, Y = y, Width = w, Height = h };
                }
                _preset.SlotOverrides.Add(ov);
            }
        }
    }
}
