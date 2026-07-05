using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// Exposes an int modifier bitmask (a <see cref="Win32.KeyModifiers"/> value: Alt=1, Control=2, Shift=4)
    /// as three bindable bool properties, doing a read-modify-write on the underlying setting so toggling one
    /// flag preserves the others. Backs the Alt/Ctrl/Shift checkboxes for Auto-Find and Minimize-Unconnected.
    /// </summary>
    internal sealed class ModifierFlagsViewModel : INotifyPropertyChanged
    {
        private readonly Func<int> _get;
        private readonly Action<int> _set;

        internal ModifierFlagsViewModel(Func<int> get, Action<int> set)
        {
            _get = get;
            _set = set;
        }

        public bool Alt
        {
            get => HasBit((int)Win32.KeyModifiers.Alt);
            set => SetBit((int)Win32.KeyModifiers.Alt, value);
        }

        public bool Control
        {
            get => HasBit((int)Win32.KeyModifiers.Control);
            set => SetBit((int)Win32.KeyModifiers.Control, value);
        }

        public bool Shift
        {
            get => HasBit((int)Win32.KeyModifiers.Shift);
            set => SetBit((int)Win32.KeyModifiers.Shift, value);
        }

        private bool HasBit(int bit) => (_get() & bit) != 0;

        private void SetBit(int bit, bool on)
        {
            int value = _get();
            int updated = on ? (value | bit) : (value & ~bit);
            if (updated != value)
            {
                _set(updated);
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
