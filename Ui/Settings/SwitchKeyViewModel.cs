using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// Backs the Switching-Mode "switch" binding, which stores EITHER a mouse-button VK (1=Left, 2=Right,
    /// 4=Middle) OR a keyboard virtual-key code in the single <c>switchingModeSwitchKeyCode</c> setting —
    /// exactly the mapping the old dialog's combo + KeyPicker used. Combo index 0/1/2 = the mouse buttons,
    /// 3 = "Keyboard Key…" (the picker value).
    /// </summary>
    internal sealed class SwitchKeyViewModel : INotifyPropertyChanged
    {
        private int _keyboardKeyCode;

        internal SwitchKeyViewModel()
        {
            int stored = Properties.Settings.Default.switchingModeSwitchKeyCode;
            // Remember any keyboard value so switching to a mouse button and back doesn't lose it.
            _keyboardKeyCode = (stored == 1 || stored == 2 || stored == 4) ? 0 : stored;
        }

        /// <summary>0 = Left, 1 = Right, 2 = Middle mouse, 3 = Keyboard Key.</summary>
        public int ComboIndex
        {
            get
            {
                switch (Properties.Settings.Default.switchingModeSwitchKeyCode)
                {
                    case 1: return 0;
                    case 2: return 1;
                    case 4: return 2;
                    default: return 3;
                }
            }
            set
            {
                switch (value)
                {
                    case 0: Properties.Settings.Default.switchingModeSwitchKeyCode = 1; break;
                    case 1: Properties.Settings.Default.switchingModeSwitchKeyCode = 2; break;
                    case 2: Properties.Settings.Default.switchingModeSwitchKeyCode = 4; break;
                    default: Properties.Settings.Default.switchingModeSwitchKeyCode = _keyboardKeyCode; break;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsKeyboard));
            }
        }

        public bool IsKeyboard => ComboIndex == 3;

        /// <summary>The keyboard key VK, used only while <see cref="IsKeyboard"/>; bound to the KeyPickerBox.</summary>
        public int KeyboardKeyCode
        {
            get => _keyboardKeyCode;
            set
            {
                _keyboardKeyCode = value;
                if (IsKeyboard)
                    Properties.Settings.Default.switchingModeSwitchKeyCode = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
