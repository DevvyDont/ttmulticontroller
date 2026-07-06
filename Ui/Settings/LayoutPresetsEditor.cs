using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Json;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// Manages the layout-preset list, backed by a mutable <see cref="LayoutPresetFile"/> from
    /// <see cref="LayoutPresetStorage"/>. Individual presets are still edited in the WinForms
    /// LayoutPresetEditorForm (opened from the code-behind) until R9 replaces it. As an
    /// <see cref="ISettingsEditor"/> it writes the JSON file only on Commit (OK); Cancel leaves it untouched.
    /// </summary>
    internal sealed class LayoutPresetsEditor : ISettingsEditor, INotifyPropertyChanged
    {
        private readonly LayoutPresetFile _file;
        private LayoutPreset _selected;

        public ObservableCollection<LayoutPreset> Presets { get; }

        internal LayoutPresetsEditor()
        {
            var file = LayoutPresetStorage.Load();
            if (file?.Presets == null)
                file = new LayoutPresetFile();
            _file = file;
            Presets = new ObservableCollection<LayoutPreset>(_file.Presets);
            SelectedPreset = Presets.FirstOrDefault();
        }

        public LayoutPreset SelectedPreset
        {
            get => _selected;
            set { _selected = value; Changed(); }
        }

        /// <summary>A brand-new preset with the same default single 2×2 monitor region as the old dialog.</summary>
        internal static LayoutPreset NewDefault() => new LayoutPreset
        {
            Name = "New Preset",
            Regions = new List<LayoutRegion>
            {
                new LayoutRegion { Source = LayoutRegionSource.Monitor, MonitorIndex = 0, Rows = 2, Cols = 2 }
            },
            SlotOverrides = new List<SlotOverride>(),
        };

        /// <summary>Deep clone via the DataContract serializer so editing a copy can't mutate the original on Cancel.</summary>
        internal static LayoutPreset Clone(LayoutPreset preset)
        {
            var serializer = new DataContractJsonSerializer(typeof(LayoutPreset));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, preset);
                ms.Position = 0;
                return (LayoutPreset)serializer.ReadObject(ms);
            }
        }

        internal void Add(LayoutPreset preset)
        {
            Presets.Add(preset);
            SelectedPreset = preset;
        }

        internal void Replace(LayoutPreset oldPreset, LayoutPreset newPreset)
        {
            int i = Presets.IndexOf(oldPreset);
            if (i >= 0)
            {
                Presets[i] = newPreset;
                SelectedPreset = newPreset;
            }
        }

        internal void Remove(LayoutPreset preset)
        {
            if (preset == null)
                return;
            int i = Presets.IndexOf(preset);
            Presets.Remove(preset);
            SelectedPreset = Presets.Count > 0 ? Presets[System.Math.Min(i, Presets.Count - 1)] : null;
        }

        public void Commit()
        {
            _file.Presets = Presets.ToList();
            LayoutPresetStorage.Save(_file);
        }

        public void Discard()
        {
            // The JSON file is only written on Commit, so there is nothing to undo.
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Changed([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
