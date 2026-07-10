using System;
using System.Globalization;
using System.Windows.Data;

namespace TTMulti.Ui.Settings
{
    /// <summary>Inverts a bool both ways. Used e.g. for the "Enable Keep-Alive" checkbox over the stored
    /// <c>disableKeepAlive</c> setting, and to enable/disable a control from a boolean it should oppose.</summary>
    internal sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b ? !b : value;
    }
}
