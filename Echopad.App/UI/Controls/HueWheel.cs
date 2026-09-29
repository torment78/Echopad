using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Echopad.App.Services;

namespace Echopad.App.UI.Controls;

// Same ring-and-swatch interaction as Event Lens, with fixed-value HSV palette colors.
public sealed class HueWheel : FrameworkElement
{
    public static readonly DependencyProperty HueProperty = DependencyProperty.Register(nameof(Hue), typeof(double), typeof(HueWheel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SwatchProperty = DependencyProperty.Register(nameof(Swatch), typeof(Brush), typeof(HueWheel),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly BitmapSource Wheel = CreateWheel();
    public double Hue { get => (double)GetValue(HueProperty); set => SetValue(HueProperty, value); }
    public Brush Swatch { get => (Brush)GetValue(SwatchProperty); set => SetValue(SwatchProperty, value); }
    public HueWheel() { Focusable = true; Cursor = Cursors.Hand; }
    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        dc.DrawImage(Wheel, new Rect((ActualWidth - size) / 2, (ActualHeight - size) / 2, size, size));
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(Swatch, new Pen(IsKeyboardFocused ? Brushes.White : Brushes.Gray, 1), center, size * .25, size * .25);
        double angle = (Hue - 90) * Math.PI / 180;
        var mark = new Point(center.X + size * .407 * Math.Cos(angle), center.Y + size * .407 * Math.Sin(angle));
        dc.DrawEllipse(Brushes.Transparent, new Pen(Brushes.Black, 5), mark, 5, 5);
        dc.DrawEllipse(Brushes.Transparent, new Pen(Brushes.White, 2), mark, 5, 5);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this); double size = Math.Min(ActualWidth, ActualHeight);
        double distance = (p - new Point(ActualWidth / 2, ActualHeight / 2)).Length;
        if (distance < size * .30 || distance > size * .5) return;
        Focus(); CaptureMouse(); SetHue(p); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured) SetHue(e.GetPosition(this)); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if (IsMouseCaptured) ReleaseMouseCapture(); }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        if (e.Key is Key.Left or Key.Down) SetCurrentValue(HueProperty, AppearanceTheme.Normalize(Hue - step));
        else if (e.Key is Key.Right or Key.Up) SetCurrentValue(HueProperty, AppearanceTheme.Normalize(Hue + step));
        else return;
        e.Handled = true;
    }
    private void SetHue(Point p) => SetCurrentValue(HueProperty, AppearanceTheme.Normalize(Math.Atan2(p.Y - ActualHeight / 2, p.X - ActualWidth / 2) * 180 / Math.PI + 90));
    private static BitmapSource CreateWheel()
    {
        const int size = 240; var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            double dx = x + .5 - size / 2d, dy = y + .5 - size / 2d, r = Math.Sqrt(dx * dx + dy * dy);
            double alpha = Math.Clamp(Math.Min(r - 79, 116 - r), 0, 1);
            if (alpha == 0) continue;
            var c = AppearanceTheme.ColorAt(Math.Atan2(dy, dx) * 180 / Math.PI + 90, .65, .88);
            int i = (y * size + x) * 4; pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = (byte)(alpha * 255);
        }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4); bmp.Freeze(); return bmp;
    }
}
