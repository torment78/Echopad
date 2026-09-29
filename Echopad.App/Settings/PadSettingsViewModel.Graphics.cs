using System.IO;
using Echopad.App.Services;
using Echopad.Core;

namespace Echopad.App.Settings;

public sealed partial class PadSettingsViewModel
{
    private PadGraphicsSettings _graphics = new();
    public PadGraphicsSettings GraphicsPreview => _graphics.Clone();
    public Array ImageFits { get; } = Enum.GetValues(typeof(PadImageFit));
    public string StoppedImageName => ImageName(_graphics.StoppedImage);
    public string PlayingImageName => ImageName(_graphics.PlayingImage);
    private static string ImageName(string? path) => string.IsNullOrEmpty(path) ? "No PNG selected" : File.Exists(path) ? "PNG ready" : "PNG missing — choose it again";
    public double StoppedOpacity { get => _graphics.StoppedOpacity; set { _graphics.StoppedOpacity = Math.Clamp(value, 0, 100); GraphicsChanged(nameof(StoppedOpacity)); } }
    public double PlayingOpacity { get => _graphics.PlayingOpacity; set { _graphics.PlayingOpacity = Math.Clamp(value, 0, 100); GraphicsChanged(nameof(PlayingOpacity)); } }
    public PadImageFit StoppedFit { get => _graphics.StoppedFit; set { _graphics.StoppedFit = value; GraphicsChanged(nameof(StoppedFit)); } }
    public PadImageFit PlayingFit { get => _graphics.PlayingFit; set { _graphics.PlayingFit = value; GraphicsChanged(nameof(PlayingFit)); } }
    public void ImportImage(bool playing, string path)
    {
        string imported = PadImageStore.Import(path, _settings.DataDirectory);
        if (playing) _graphics.PlayingImage = imported; else _graphics.StoppedImage = imported;
        GraphicsChanged(playing ? nameof(PlayingImageName) : nameof(StoppedImageName));
    }
    public void ClearImage(bool playing)
    {
        if (playing) _graphics.PlayingImage = null; else _graphics.StoppedImage = null;
        GraphicsChanged(playing ? nameof(PlayingImageName) : nameof(StoppedImageName));
    }
    private void GraphicsChanged(string property)
    {
        OnPropertyChanged(property); OnPropertyChanged(nameof(GraphicsPreview));
    }
}
