using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// Given a fill brush, returns a face colour that reads against it: near-black on light colours, white on
    /// dark ones (perceived-brightness threshold). Used to colour the cat's eyes/nose/whiskers so they stay
    /// visible whatever Multi-mode colour the user picked.
    /// </summary>
    public sealed class LuminanceContrastConverter : IValueConverter
    {
        private static readonly SolidColorBrush Dark = Freeze(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20));
        private static readonly SolidColorBrush Light = Freeze(System.Windows.Media.Colors.White);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var color = (value as SolidColorBrush)?.Color ?? System.Windows.Media.Colors.Gray;

            // Perceived brightness (0..1). Light cat → dark features; dark cat → white features.
            double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            return brightness > 0.6 ? Dark : Light;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static SolidColorBrush Freeze(System.Windows.Media.Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
