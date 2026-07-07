using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TTMulti.Ui.ViewModels;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// Inline editor for layout presets on the Layout Presets settings page, backed by a mutable
    /// <see cref="LayoutPresetFile"/> from <see cref="LayoutPresetStorage"/>. As an <see cref="ISettingsEditor"/>
    /// it writes the JSON file only on Commit (OK); Cancel leaves it untouched (a fresh editor re-reads disk).
    /// </summary>
    internal sealed class LayoutPresetsEditor : ISettingsEditor, INotifyPropertyChanged
    {
        private readonly LayoutPresetFile _file;
        private LayoutPresetItemViewModel _selected;

        public ObservableCollection<LayoutPresetItemViewModel> Presets { get; }

        /// <summary>The current monitors (plus "Custom area...") for each region's monitor dropdown.</summary>
        public IReadOnlyList<MonitorOption> Monitors { get; }

        public bool HasPresets => Presets.Count > 0;
        public bool NoPresets => Presets.Count == 0;

        internal LayoutPresetsEditor()
        {
            var file = LayoutPresetStorage.Load();
            if (file?.Presets == null)
                file = new LayoutPresetFile();
            _file = file;

            Monitors = MonitorOption.BuildList();

            Presets = new ObservableCollection<LayoutPresetItemViewModel>(
                _file.Presets.Select(p => new LayoutPresetItemViewModel(p, Monitors)));
            Presets.CollectionChanged += (s, e) => { Changed(nameof(HasPresets)); Changed(nameof(NoPresets)); };

            SelectedPreset = Presets.FirstOrDefault();
        }

        public LayoutPresetItemViewModel SelectedPreset
        {
            get => _selected;
            set { _selected = value; Changed(); }
        }

        public void AddPreset()
        {
            var preset = new LayoutPreset
            {
                Name = "New preset",
                Regions = new List<LayoutRegion>
                {
                    new LayoutRegion { Source = LayoutRegionSource.Monitor, MonitorIndex = 0, Rows = 2, Cols = 2 },
                },
                SlotOverrides = new List<SlotOverride>(),
            };
            var vm = new LayoutPresetItemViewModel(preset, Monitors);
            Presets.Add(vm);
            SelectedPreset = vm;
        }

        public void RemovePreset(LayoutPresetItemViewModel preset)
        {
            if (preset == null) return;
            int i = Presets.IndexOf(preset);
            Presets.Remove(preset);
            SelectedPreset = Presets.Count > 0 ? Presets[System.Math.Min(i, Presets.Count - 1)] : null;
        }

        public void Commit()
        {
            foreach (var vm in Presets)
                vm.SyncSlotOverrides();
            _file.Presets = Presets.Select(vm => vm.Preset).ToList();
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
