using System.Windows;
using System.Windows.Media;
using Echopad.App.Services;
using Echopad.Core;

namespace Echopad.App.UI.Controls;

public sealed class PadArtwork : FrameworkElement
{
    public static readonly DependencyProperty GraphicsProperty = DependencyProperty.Register(nameof(Graphics), typeof(PadGraphicsSettings), typeof(PadArtwork), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(PadState), typeof(PadArtwork), new FrameworkPropertyMetadata(PadState.Loaded, FrameworkPropertyMetadataOptions.AffectsRender));
    public PadGraphicsSettings? Graphics { get => (PadGraphicsSettings?)GetValue(GraphicsProperty); set => SetValue(GraphicsProperty, value); }
    public PadState State { get => (PadState)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var graphics = Graphics;
        if (graphics == null || ActualWidth <= 0 || ActualHeight <= 0) return;
        bool playing = State == PadState.Playing;
        var image = PadImageStore.Load(playing ? graphics.PlayingImage : graphics.StoppedImage);
        if (image == null) return;
        double sx = ActualWidth / image.Width, sy = ActualHeight / image.Height;
        bool crop = (playing ? graphics.PlayingFit : graphics.StoppedFit) == PadImageFit.Crop;
        double scale = crop ? Math.Max(sx, sy) : Math.Min(sx, sy);
        double width = image.Width * scale, height = image.Height * scale;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize), 13, 13));
        double opacity = playing ? graphics.PlayingOpacity : graphics.StoppedOpacity;
        dc.PushOpacity(double.IsFinite(opacity) ? Math.Clamp(opacity / 100, 0, 1) : 0.7);
        dc.DrawImage(image, new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height));
        dc.Pop(); dc.Pop();
    }
}
