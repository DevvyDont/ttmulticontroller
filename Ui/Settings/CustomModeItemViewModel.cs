using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// Editable view of one <see cref="CustomModeDefinition"/>: name, optional activation hotkey (key +
    /// Alt/Ctrl/Shift + global), mode-key-cycle membership, per-mode left/right border colours, and its
    /// bindings. The <see cref="CustomModeDefinition.Id"/> is preserved so the persisted identity is stable.
    /// </summary>
    internal sealed class CustomModeItemViewModel : INotifyPropertyChanged
    {
        private readonly string _id;
        private string _name;
        private WinFormsKeys _actKey;
        private bool _actAlt, _actCtrl, _actShift, _actGlobal;
        private bool _includeInCycle;
        private int? _leftColor;
        private int? _rightColor;

        public ObservableCollection<CustomModeBindingViewModel> Bindings { get; }

        internal CustomModeItemViewModel(CustomModeDefinition d)
        {
            _id = d.Id;
            _name = d.Name;
            _actKey = (WinFormsKeys)d.ActivationHotkeyCode;
            var mods = (Win32.KeyModifiers)d.ActivationHotkeyModifiers;
            _actAlt = (mods & Win32.KeyModifiers.Alt) != 0;
            _actCtrl = (mods & Win32.KeyModifiers.Control) != 0;
            _actShift = (mods & Win32.KeyModifiers.Shift) != 0;
            _actGlobal = d.ActivationHotkeyGlobal;
            _includeInCycle = d.ShouldIncludeInModeHotkeyCycle();
            _leftColor = d.LeftBorderColorArgb;
            _rightColor = d.RightBorderColorArgb;
            Bindings = new ObservableCollection<CustomModeBindingViewModel>(
                (d.Bindings ?? new List<CustomModeBinding>()).Select(b => new CustomModeBindingViewModel(b)));
        }

        /// <summary>Shown in the modes list.</summary>
        public string Name { get => _name; set { _name = value; Changed(); } }

        public int ActivationKeyCode { get => (int)_actKey; set { _actKey = (WinFormsKeys)value; Changed(); } }
        public bool ActAlt { get => _actAlt; set { _actAlt = value; Changed(); } }
        public bool ActCtrl { get => _actCtrl; set { _actCtrl = value; Changed(); } }
        public bool ActShift { get => _actShift; set { _actShift = value; Changed(); } }
        public bool ActGlobal { get => _actGlobal; set { _actGlobal = value; Changed(); } }
        public bool IncludeInCycle { get => _includeInCycle; set { _includeInCycle = value; Changed(); } }

        // Nullable in the model (null = use the Multi-mode default); the swatch always edits a concrete ARGB.
        public int LeftColorArgb
        {
            get => _leftColor ?? CustomModeDefinition.DefaultLeftBorderColor.ToArgb();
            set { _leftColor = value; Changed(); }
        }

        public int RightColorArgb
        {
            get => _rightColor ?? CustomModeDefinition.DefaultRightBorderColor.ToArgb();
            set { _rightColor = value; Changed(); }
        }

        internal CustomModeDefinition ToModel()
        {
            var mods = Win32.KeyModifiers.None;
            if (_actAlt) mods |= Win32.KeyModifiers.Alt;
            if (_actCtrl) mods |= Win32.KeyModifiers.Control;
            if (_actShift) mods |= Win32.KeyModifiers.Shift;

            return new CustomModeDefinition
            {
                Id = _id,
                Name = _name,
                ActivationHotkeyCode = (int)_actKey,
                ActivationHotkeyModifiers = (int)mods,
                ActivationHotkeyGlobal = _actGlobal,
                IncludeInModeHotkeyCycle = _includeInCycle ? (bool?)null : false,
                LeftBorderColorArgb = _leftColor,
                RightBorderColorArgb = _rightColor,
                Bindings = Bindings.Select(b => b.ToModel()).ToList(),
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Changed([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
