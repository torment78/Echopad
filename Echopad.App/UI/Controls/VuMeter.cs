using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Echopad.App.UI.Controls;

/// <summary>Logarithmic, segmented peak meter. Peak hold and decay run on the UI timer.</summary>
public sealed class VuMeter : FrameworkElement
{
    public static readonly DependencyProperty LevelDbProperty = DependencyProperty.Register(nameof(LevelDb), typeof(double), typeof(VuMeter), new FrameworkPropertyMetadata(-60d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PeakDbProperty = DependencyProperty.Register(nameof(PeakDb), typeof(double), typeof(VuMeter), new FrameworkPropertyMetadata(-60d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double LevelDb { get => (double)GetValue(LevelDbProperty); set => SetValue(LevelDbProperty, value); }
    public double PeakDb { get => (double)GetValue(PeakDbProperty); set => SetValue(PeakDbProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        double top = 8, height = Math.Max(10, ActualHeight - 30), width = 20, left = 2;
        double step = height / 30;
        for (int n = 0; n < 30; n++)
        {
            double db = -60 + n * 2;
            var color = db >= -4 ? Color.FromRgb(242, 100, 100) : db >= -12 ? Color.FromRgb(230, 190, 80) : Color.FromRgb(80, 198, 150);
            var brush = new SolidColorBrush(color) { Opacity = LevelDb > db ? 1 : .12 };
            dc.DrawRoundedRectangle(brush, null, new Rect(left, top + height - (n + 1) * step, width, Math.Max(1, step - 2)), 1, 1);
        }
        double peak = Math.Clamp((PeakDb + 60) / 60, 0, 1);
        if (peak > 0) dc.DrawRectangle(Brushes.White, null, new Rect(left, top + height - peak * height, width, 1));
        foreach (int db in new[] { 0, -12, -24, -36, -48, -60 })
        {
            var text = new FormattedText(db.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(28, top + height * (-db / 60d) - 6));
        }
        var label = LevelDb <= -59.9 ? "−∞" : LevelDb.ToString("0", CultureInfo.InvariantCulture);
        dc.DrawText(new FormattedText(label + " dB", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(0, ActualHeight - 15));
    }
}
