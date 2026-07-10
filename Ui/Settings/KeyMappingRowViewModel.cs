using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// One editable row of the Multi-Mode key-bindings grid, wrapping a <see cref="KeyMapping"/>. Exposes the
    /// three keys as int VK codes so the <c>KeyPickerBox</c> controls bind directly; <see cref="ToModel"/>
    /// rebuilds the frozen <see cref="KeyMapping"/> for serialization. Built-in rows are <see cref="IsReadOnly"/>
    /// — their title is fixed and they can't be removed, but their toon keys stay remappable (as in the old grid).
    /// </summary>
    internal sealed class KeyMappingRowViewModel : INotifyPropertyChanged
    {
        private string _title;
        private WinFormsKeys _key;
        private WinFormsKeys _left;
        private WinFormsKeys _right;

        internal KeyMappingRowViewModel(KeyMapping mapping)
        {
            _title = mapping.Title;
            _key = mapping.Key;
            _left = mapping.LeftToonKey;
            _right = mapping.RightToonKey;
            IsReadOnly = mapping.ReadOnly;
        }

        public bool IsReadOnly { get; }
        public bool IsEditable => !IsReadOnly;

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public int KeyCode
        {
            get => (int)_key;
            set { _key = (WinFormsKeys)value; OnPropertyChanged(); }
        }

        public int LeftKeyCode
        {
            get => (int)_left;
            set { _left = (WinFormsKeys)value; OnPropertyChanged(); }
        }

        public int RightKeyCode
        {
            get => (int)_right;
            set { _right = (WinFormsKeys)value; OnPropertyChanged(); }
        }

        internal KeyMapping ToModel() => new KeyMapping(_title, _key, _left, _right, IsReadOnly);

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
