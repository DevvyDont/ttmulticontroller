using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// One-way converts a packed ARGB <see cref="int"/> setting (the same <c>Color.ToArgb()</c> value the mode
    /// border-colour settings store) into a frozen WPF <see cref="SolidColorBrush"/>. Used to tint the Keybinds
    /// page key-pickers with their mode colour (in-game / left toon / right toon).
    /// </summary>
    internal sealed class ArgbToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int argb)
            {
                var color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
