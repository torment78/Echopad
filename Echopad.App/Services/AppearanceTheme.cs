using System.Windows;
using System.Windows.Media;
using Echopad.Core;

namespace Echopad.App.Services;

public static class AppearanceTheme
{
    public static double Normalize(double h) => double.IsFinite(h) ? (h % 360 + 360) % 360 : 215;
    public static Color ColorAt(double h, double saturation, double value)
    {
        h = Normalize(h) / 60;
        double c = value * saturation, x = c * (1 - Math.Abs(h % 2 - 1)), m = value - c;
        var (r, g, b) = h switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    public static void Apply(AppearanceSettings a)
    {
        if (Application.Current is null) return;
        void Set(string key, double hue, double s, double v)
        {
            var brush = new SolidColorBrush(ColorAt(hue, s, v)); brush.Freeze(); Application.Current.Resources[key] = brush;
        }
        foreach (var key in new[] { "BgBrush", "AppBackgroundBrush" }) Set(key, a.BackgroundHue, .24, .085);
        foreach (var key in new[] { "PanelBrush", "CardBackgroundBrush" }) Set(key, a.CardHue, .20, .135);
        foreach (var key in new[] { "ControlBorderBrush", "CardBorderBrush" }) Set(key, a.OutlineHue, .23, .26);
        Set("ControlBgBrush", a.ButtonHue, .25, .19);
        Set("ControlHoverBrush", a.ButtonHue, .25, .25);
        Set("ControlPressedBrush", a.ButtonHue, .25, .14);
        Set("HeaderButtonBrush", a.ButtonHue, .25, .19);
        Set("HeaderButtonTextBrush", a.ButtonTextHue, .18, .94);
        Set("TitleBrush", a.TitleHue, .36, .95);
        Set("PadTextBrush", a.PadTextHue, .18, .94);
        Set("PadBaseBrush", a.PadHue, .25, .14);
        Application.Current.Resources["PadOutlineOpacity"] = Math.Clamp(a.OutlineIntensity / 100, 0, 1);
    }
}
