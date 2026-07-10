using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace TTMulti.Ui.ViewModels
{
    /// <summary>
    /// Editable view of one <see cref="LayoutPreset"/> for the inline Layout Presets page: name, activation
    /// hotkey, the list of region rows, and (under Advanced) per-slot overrides. Edits write through to the
    /// underlying preset object; <see cref="PreviewRev"/> bumps whenever anything changes so the live diagram
    /// re-renders. Mirrors CustomModeItemViewModel.
    /// </summary>
    public sealed class LayoutPresetItemViewModel : INotifyPropertyChanged
    {
        private readonly IReadOnlyList<MonitorOption> _monitors;
        private LayoutRegionViewModel _selectedRegion;
        private int _previewRev;

        public LayoutPreset Preset { get; }
        public ObservableCollection<LayoutRegionViewModel> Regions { get; } = new ObservableCollection<LayoutRegionViewModel>();
        public ObservableCollection<SlotRow> SlotRows { get; } = new ObservableCollection<SlotRow>();

        internal LayoutPresetItemViewModel(LayoutPreset preset, IReadOnlyList<MonitorOption> monitors)
        {
            Preset = preset;
            if (Preset.Regions == null) Preset.Regions = new List<LayoutRegion>();
            if (Preset.SlotOverrides == null) Preset.SlotOverrides = new List<SlotOverride>();
            _monitors = monitors ?? new List<MonitorOption>();

            foreach (var region in Preset.Regions)
                Regions.Add(NewRegionVm(region));
            _selectedRegion = Regions.FirstOrDefault();
            RebuildSlotRows();
        }

        private LayoutRegionViewModel NewRegionVm(LayoutRegion region) =>
            new LayoutRegionViewModel(region, _monitors, OnRegionChanged);

        private void OnRegionChanged()
        {
            RebuildSlotRows();
            BumpPreview();
        }

        /// <summary>Shown in the preset selector.</summary>
        public string Name
        {
            get => Preset.Name;
            set { Preset.Name = value; Changed(); }
        }

        public int RegionCount => Regions.Count;

        // ── Activation hotkey ───────────────────────────────────────────────────────

        public int HotkeyCode
        {
            get => Preset.HotkeyCode;
            set { Preset.HotkeyCode = value; Changed(); Changed(nameof(HotkeyDisplay)); }
        }

        public bool HkAlt { get => HasMod(Win32.KeyModifiers.Alt); set { SetMod(Win32.KeyModifiers.Alt, value); } }
        public bool HkCtrl { get => HasMod(Win32.KeyModifiers.Control); set { SetMod(Win32.KeyModifiers.Control, value); } }
        public bool HkShift { get => HasMod(Win32.KeyModifiers.Shift); set { SetMod(Win32.KeyModifiers.Shift, value); } }

        private bool HasMod(Win32.KeyModifiers m) => ((Win32.KeyModifiers)Preset.HotkeyModifiers & m) != 0;
        private void SetMod(Win32.KeyModifiers m, bool on)
        {
            var mods = (Win32.KeyModifiers)Preset.HotkeyModifiers;
            mods = on ? (mods | m) : (mods & ~m);
            Preset.HotkeyModifiers = (int)mods;
            Changed();
            Changed(nameof(HotkeyDisplay));
        }

        public string HotkeyDisplay
        {
            get
            {
                if (Preset.HotkeyCode == 0) return "";
                var mods = (Win32.KeyModifiers)Preset.HotkeyModifiers;
                string p = ((mods & Win32.KeyModifiers.Alt) != 0 ? "Alt+" : "")
                    + ((mods & Win32.KeyModifiers.Control) != 0 ? "Ctrl+" : "")
                    + ((mods & Win32.KeyModifiers.Shift) != 0 ? "Shift+" : "");
                return p + (WinFormsKeys)Preset.HotkeyCode;
            }
        }

        // ── Regions ─────────────────────────────────────────────────────────────────

        public LayoutRegionViewModel SelectedRegion
        {
            get => _selectedRegion;
            set { _selectedRegion = value; Changed(); Changed(nameof(SelectedRegionIndex)); BumpPreview(); }
        }

        /// <summary>0-based index of the selected region (for the preview highlight); -1 if none.</summary>
        public int SelectedRegionIndex => _selectedRegion == null ? -1 : Regions.IndexOf(_selectedRegion);

        public void AddRegion()
        {
            var region = new LayoutRegion { Source = LayoutRegionSource.Monitor, MonitorIndex = 0, Rows = 2, Cols = 2 };
            Preset.Regions.Add(region);
            var vm = NewRegionVm(region);
            Regions.Add(vm);
            SelectedRegion = vm;
            Changed(nameof(RegionCount));
            OnRegionChanged();
        }

        public void RemoveRegion(LayoutRegionViewModel vm)
        {
            if (vm == null) return;
            Preset.Regions.Remove(vm.Region);
            Regions.Remove(vm);
            SelectedRegion = Regions.FirstOrDefault();
            Changed(nameof(RegionCount));
            OnRegionChanged();
        }

        // ── Preview trigger ─────────────────────────────────────────────────────────

        /// <summary>Increments on any change so the LayoutPreview re-renders (bind its Rev to this).</summary>
        public int PreviewRev { get => _previewRev; private set { _previewRev = value; Changed(); } }
        private void BumpPreview() => PreviewRev = _previewRev + 1;

        // ── Slot overrides (Advanced) ───────────────────────────────────────────────

        public void RebuildSlotRows()
        {
            // Sync any pending row edits into the preset first, then regenerate rows from the current slots.
            SyncSlotOverrides();
            RegenerateSlotRows();
        }

        /// <summary>Apply overrides captured from the on-screen per-window editor, then refresh the rows and
        /// preview WITHOUT the usual pre-sync (which would overwrite them from the now-stale rows).</summary>
        public void SetSlotOverridesAndRefresh(List<SlotOverride> overrides)
        {
            Preset.SlotOverrides = overrides;
            RegenerateSlotRows();
            BumpPreview();
        }

        private void RegenerateSlotRows()
        {
            foreach (var r in SlotRows) r.Changed = null;
            SlotRows.Clear();

            var overridesBySlot = (Preset.SlotOverrides ?? new List<SlotOverride>()).ToDictionary(o => o.SlotIndex);
            var defaultSlots = LayoutPresetBuilder.BuildSlots(Preset);
            int maxSlot = defaultSlots.Count;
            if (overridesBySlot.Count > 0) maxSlot = Math.Max(maxSlot, overridesBySlot.Keys.Max());

            for (int slot = 1; slot <= maxSlot; slot++)
            {
                var ov = overridesBySlot.TryGetValue(slot, out var o) ? o : null;
                var rect = ov?.Rect != null
                    ? ov.Rect.ToRectangle()
                    : (slot <= defaultSlots.Count ? defaultSlots[slot - 1].Rect : new System.Drawing.Rectangle());
                SlotRows.Add(new SlotRow
                {
                    Slot = slot,
                    OverrideRect = ov?.Rect != null,
                    X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height,
                    Minimized = ov?.Minimized ?? false,
                    Changed = () => { SyncSlotOverrides(); BumpPreview(); },
                });
            }
        }

        /// <summary>Rebuild <see cref="LayoutPreset.SlotOverrides"/> from the current grid rows.</summary>
        public void SyncSlotOverrides()
        {
            if (SlotRows.Count == 0) return;
            var overrides = new List<SlotOverride>();
            foreach (var row in SlotRows)
            {
                if (row.Slot < 1) continue;
                if (!row.OverrideRect && !row.Minimized) continue;
                var ov = new SlotOverride { SlotIndex = row.Slot, Minimized = row.Minimized ? true : (bool?)null };
                if (row.OverrideRect)
                    ov.Rect = new LayoutRect { X = row.X, Y = row.Y, Width = row.Width, Height = row.Height };
                overrides.Add(ov);
            }
            Preset.SlotOverrides = overrides;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Changed([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>One editable row of the Advanced slot-overrides grid.</summary>
        public sealed class SlotRow : INotifyPropertyChanged
        {
            internal Action Changed;
            private bool _overrideRect;
            private int _x, _y, _w, _h;
            private bool _minimized;

            public int Slot { get; set; }
            public bool OverrideRect { get => _overrideRect; set { _overrideRect = value; Raise(); } }
            public int X { get => _x; set { _x = value; Raise(); } }
            public int Y { get => _y; set { _y = value; Raise(); } }
            public int Width { get => _w; set { _w = value; Raise(); } }
            public int Height { get => _h; set { _h = value; Raise(); } }
            public bool Minimized { get => _minimized; set { _minimized = value; Raise(); } }

            private void Raise([CallerMemberName] string name = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                Changed?.Invoke();
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
