using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// WPF port of the WinForms KeyPicker. Click to arm, then the next key press is captured and stored; any key
    /// can be bound (including Esc, Delete, Backspace, Tab, Enter). Click the armed button again to cancel, or
    /// double-click to clear the binding to "Disabled". Left/right modifier variants are folded to the generic
    /// key (LControl/RControl to Ctrl, etc.) so the binding matches a real modifier press. The stored value
    /// (<see cref="KeyCode"/>) is the Win32 virtual-key code, which equals the numeric value of
    /// <see cref="System.Windows.Forms.Keys"/> (byte-compatible with the persisted settings). 0 = disabled.
    /// </summary>
    public partial class KeyPickerBox : UserControl
    {
        private bool _armed;

        public KeyPickerBox()
        {
            InitializeComponent();
            LostKeyboardFocus += (s, e) => { if (_armed) Disarm(); UpdateText(); };
        }

        public static readonly DependencyProperty KeyCodeProperty =
            DependencyProperty.Register(nameof(KeyCode), typeof(int), typeof(KeyPickerBox),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    (d, e) => ((KeyPickerBox)d).UpdateText()));

        /// <summary>The captured key as a Win32 virtual-key code (== (int)System.Windows.Forms.Keys). 0 = none.</summary>
        public int KeyCode
        {
            get => (int)GetValue(KeyCodeProperty);
            set => SetValue(KeyCodeProperty, value);
        }

        private void PickerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_armed)
                Disarm();
            else
                Arm();
        }

        // Double-click clears the binding (Delete/Backspace are captured as real keys now, so they can be bound).
        // Handled on the tunneling preview so the second click doesn't fall through to PickerButton_Click and
        // immediately re-arm.
        private void PickerButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                _armed = false;
                KeyCode = 0;
                UpdateText();
                e.Handled = true;
            }
        }

        private void Arm()
        {
            _armed = true;
            pickerButton.Content = "Press a key…";
            pickerButton.Focus();
            Keyboard.Focus(pickerButton);
        }

        private void Disarm()
        {
            _armed = false;
            UpdateText();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (!_armed)
            {
                base.OnPreviewKeyDown(e);
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Any key is bindable now (Esc / Delete / Backspace / Tab / Enter included). Cancel by clicking the
            // armed button again or clicking away; clear with a double-click. Fold left/right modifier variants to
            // the generic key so the binding matches a real Ctrl/Shift/Alt press.
            int vk = (int)KeyRemap.NormalizeModifier((WinFormsKeys)KeyInterop.VirtualKeyFromKey(key));
            if (vk != 0)
            {
                KeyCode = vk;
                Disarm();
            }
            e.Handled = true;
        }

        private void UpdateText()
        {
            pickerButton.Content = KeyCode == 0 ? "Disabled" : Describe((WinFormsKeys)KeyCode);
        }

        // Display-only friendly names, mirroring the WinForms KeyPicker's alternateKeyTexts table. The stored
        // value is always the raw virtual-key code regardless of the label.
        private static readonly Dictionary<WinFormsKeys, string> FriendlyNames = new Dictionary<WinFormsKeys, string>
        {
            { WinFormsKeys.Oemtilde, "Tilde" },
            { WinFormsKeys.D0, "0" }, { WinFormsKeys.D1, "1" }, { WinFormsKeys.D2, "2" },
            { WinFormsKeys.D3, "3" }, { WinFormsKeys.D4, "4" }, { WinFormsKeys.D5, "5" },
            { WinFormsKeys.D6, "6" }, { WinFormsKeys.D7, "7" }, { WinFormsKeys.D8, "8" }, { WinFormsKeys.D9, "9" },
            { WinFormsKeys.Left, "LeftArrow" }, { WinFormsKeys.Right, "RightArrow" },
            { WinFormsKeys.Up, "UpArrow" }, { WinFormsKeys.Down, "DownArrow" },
            { WinFormsKeys.Menu, "Alt" }, { WinFormsKeys.ControlKey, "Ctrl" }, { WinFormsKeys.ShiftKey, "Shift" },
            { WinFormsKeys.Next, "PageDown" }, { WinFormsKeys.Prior, "PageUp" },
            { WinFormsKeys.Oem1, "Semicolon" }, { WinFormsKeys.Oem5, "Backslash" },
            { WinFormsKeys.Oem6, "RightBracket" }, { WinFormsKeys.Oem7, "Quote" },
            { WinFormsKeys.Oemcomma, "Comma" }, { WinFormsKeys.OemPeriod, "Period" },
            { WinFormsKeys.OemMinus, "Minus" }, { WinFormsKeys.Oemplus, "Equals" },
            { WinFormsKeys.OemQuestion, "Slash" }, { WinFormsKeys.OemOpenBrackets, "LeftBracket" },
        };

        private static string Describe(WinFormsKeys key) =>
            FriendlyNames.TryGetValue(key, out string name) ? name : key.ToString();
    }
}
