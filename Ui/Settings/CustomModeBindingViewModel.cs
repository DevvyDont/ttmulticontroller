using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// Editable view of one <see cref="CustomModeBinding"/> (input key + modifiers → action → target). Combo
    /// selections are exposed as indices matching the old dialog's item order; the visibility flags drive which
    /// detail fields are shown. <see cref="Summary"/> reuses the model's ToString() for the bindings list.
    /// </summary>
    internal sealed class CustomModeBindingViewModel : INotifyPropertyChanged
    {
        private WinFormsKeys _inputKey;
        private bool _alt, _ctrl, _shift;
        private int _actionIndex;
        private string _role;
        private WinFormsKeys _rawKey;
        private int _targetKindIndex;
        private int _targetIndex;
        private string _listedText;
        private bool _consume;

        internal CustomModeBindingViewModel(CustomModeBinding b)
        {
            _inputKey = (WinFormsKeys)b.InputKey;
            _alt = b.RequireAlt;
            _ctrl = b.RequireControl;
            _shift = b.RequireShift;
            _actionIndex = Clamp((int)b.Action, 0, 2);
            _role = b.RoleTitle ?? "";
            _rawKey = (WinFormsKeys)b.RawKey;
            _targetKindIndex = Clamp((int)b.TargetKind, 0, 2);
            _targetIndex = Clamp(b.TargetIndex, 1, 32);
            _listedText = (b.ListedTargetIndices != null && b.ListedTargetIndices.Count > 0)
                ? string.Join(", ", b.ListedTargetIndices)
                : "";
            _consume = b.ConsumeInput;
        }

        public int InputKeyCode
        {
            get => (int)_inputKey;
            set { _inputKey = (WinFormsKeys)value; Changed(); RefreshSummary(); }
        }

        public bool RequireAlt { get => _alt; set { _alt = value; Changed(); RefreshSummary(); } }
        public bool RequireControl { get => _ctrl; set { _ctrl = value; Changed(); RefreshSummary(); } }
        public bool RequireShift { get => _shift; set { _shift = value; Changed(); RefreshSummary(); } }

        /// <summary>0 = Send role, 1 = Send raw key, 2 = Instant click.</summary>
        public int ActionIndex
        {
            get => _actionIndex;
            set
            {
                _actionIndex = Clamp(value, 0, 2);
                Changed();
                Changed(nameof(IsSendRole));
                Changed(nameof(IsSendRawKey));
                RefreshSummary();
            }
        }

        public bool IsSendRole => _actionIndex == 0;
        public bool IsSendRawKey => _actionIndex == 1;

        public string RoleTitle { get => _role; set { _role = value ?? ""; Changed(); RefreshSummary(); } }
        public int RawKeyCode { get => (int)_rawKey; set { _rawKey = (WinFormsKeys)value; Changed(); RefreshSummary(); } }

        /// <summary>0 = One toon, 1 = All toons, 2 = Listed.</summary>
        public int TargetKindIndex
        {
            get => _targetKindIndex;
            set
            {
                _targetKindIndex = Clamp(value, 0, 2);
                Changed();
                Changed(nameof(IsSingle));
                Changed(nameof(IsListed));
                RefreshSummary();
            }
        }

        public bool IsSingle => _targetKindIndex == 0;
        public bool IsListed => _targetKindIndex == 2;

        public int TargetIndex { get => _targetIndex; set { _targetIndex = Clamp(value, 1, 32); Changed(); RefreshSummary(); } }
        public string ListedIndicesText { get => _listedText; set { _listedText = value ?? ""; Changed(); RefreshSummary(); } }
        public bool ConsumeInput { get => _consume; set { _consume = value; Changed(); } }

        /// <summary>The one-line list label — identical to the model's ToString().</summary>
        public string Summary => ToModel().ToString();

        internal CustomModeBinding ToModel() => new CustomModeBinding
        {
            InputKey = (int)_inputKey,
            RequireAlt = _alt,
            RequireControl = _ctrl,
            RequireShift = _shift,
            Action = (CustomModeBindingAction)_actionIndex,
            RoleTitle = _role,
            RawKey = (int)_rawKey,
            TargetKind = (CustomModeTargetKind)_targetKindIndex,
            TargetIndex = _targetIndex,
            ListedTargetIndices = _targetKindIndex == 2
                ? CustomModeBinding.ParseListedTargetIndices(_listedText)
                : null,
            ConsumeInput = _consume,
        };

        private static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));

        private void RefreshSummary() => Changed(nameof(Summary));

        public event PropertyChangedEventHandler PropertyChanged;

        private void Changed([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
