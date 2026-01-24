using System;
using System.Globalization;
using System.Windows.Data;

namespace Echopad.App.Settings
{
    public sealed class IntEqualsConverter : IValueConverter
    {
        // value is your int (InputSource), parameter is "1" or "2"
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null) return false;

            if (!int.TryParse(parameter.ToString(), out var target))
                return false;

            if (value == null) return false;

            try
            {
                var current = System.Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return current == target;
            }
            catch
            {
                return false;
            }
        }

        // When RadioButton is checked -> set InputSource to parameter int
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
            {
                if (int.TryParse(parameter.ToString(), out var target))
                    return target;
            }

            // returning Binding.DoNothing prevents unchecking from overwriting the value
            return Binding.DoNothing;
        }
    }
}
