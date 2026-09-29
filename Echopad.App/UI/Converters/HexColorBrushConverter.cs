using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Echopad.App.UI.Converters;

public sealed class HexColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value as string ?? "")); }
        catch { return Brushes.Transparent; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BindingLabelConverter : IValueConverter
{
    // Old learned bindings may include diagnostic RAW bytes. Keep these in storage
    // and the tooltip, but show the complete actionable binding in the field.
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value as string ?? "").Split('|')[0].Trim();
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
