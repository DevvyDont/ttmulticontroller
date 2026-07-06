using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// Master-detail editor for custom modes, backed by a mutable <see cref="CustomModeFile"/> loaded from
    /// <see cref="CustomModeStorage"/>. As an <see cref="ISettingsEditor"/> it only writes the JSON file on
    /// Commit (OK); Cancel leaves the on-disk file untouched. The <c>customModeCycleWithModeHotkey</c> master
    /// flag is a normal Settings binding handled by the session, not here.
    /// </summary>
    internal sealed class CustomModesEditor : ISettingsEditor, INotifyPropertyChanged
    {
        private readonly CustomModeFile _file;
        private CustomModeItemViewModel _selectedMode;
        private CustomModeBindingViewModel _selectedBinding;

        public ObservableCollection<CustomModeItemViewModel> Modes { get; }

        /// <summary>Role choices for SendRole bindings: the Multi-Mode key titles plus "Zero Power Throw".</summary>
        public List<string> RoleTitles { get; }

        internal CustomModesEditor()
        {
            _file = CustomModeStorage.Load();
            if (_file.Modes == null)
                _file.Modes = new List<CustomModeDefinition>();

            Modes = new ObservableCollection<CustomModeItemViewModel>(
                _file.Modes.Select(m => new CustomModeItemViewModel(m)));

            RoleTitles = Properties.SerializedSettings.Default.Bindings
                .Select(b => b.Title)
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .ToList();
            if (!RoleTitles.Contains(CustomModeWellKnownRoles.ZeroPowerThrow))
                RoleTitles.Add(CustomModeWellKnownRoles.ZeroPowerThrow);

            SelectedMode = Modes.FirstOrDefault();
        }

        public CustomModeItemViewModel SelectedMode
        {
            get => _selectedMode;
            set
            {
                _selectedMode = value;
                Changed();
                SelectedBinding = value?.Bindings.FirstOrDefault();
            }
        }

        public CustomModeBindingViewModel SelectedBinding
        {
            get => _selectedBinding;
            set { _selectedBinding = value; Changed(); }
        }

        public void AddMode()
        {
            var vm = new CustomModeItemViewModel(new CustomModeDefinition());
            Modes.Add(vm);
            SelectedMode = vm;
        }

        public void RemoveMode(CustomModeItemViewModel mode)
        {
            if (mode == null)
                return;
            int i = Modes.IndexOf(mode);
            Modes.Remove(mode);
            SelectedMode = Modes.Count > 0 ? Modes[System.Math.Min(i, Modes.Count - 1)] : null;
        }

        public void AddBinding()
        {
            if (SelectedMode == null)
                return;
            var b = new CustomModeBindingViewModel(new CustomModeBinding
            {
                InputKey = (int)System.Windows.Forms.Keys.None,
                Action = CustomModeBindingAction.SendRole,
                TargetIndex = 1,
                TargetKind = CustomModeTargetKind.Single,
                RoleTitle = RoleTitles.FirstOrDefault() ?? "Forward",
            });
            SelectedMode.Bindings.Add(b);
            SelectedBinding = b;
        }

        public void RemoveBinding(CustomModeBindingViewModel binding)
        {
            if (SelectedMode == null || binding == null)
                return;
            int i = SelectedMode.Bindings.IndexOf(binding);
            SelectedMode.Bindings.Remove(binding);
            SelectedBinding = SelectedMode.Bindings.Count > 0
                ? SelectedMode.Bindings[System.Math.Min(i, SelectedMode.Bindings.Count - 1)]
                : null;
        }

        public void Commit()
        {
            _file.Modes = Modes.Select(m => m.ToModel()).ToList();
            CustomModeStorage.Save(_file);
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
