namespace Echopad.Core;

public enum PadImageFit { Fit, Crop }

public sealed class PadGraphicsSettings
{
    public string? StoppedImage { get; set; }
    public string? PlayingImage { get; set; }
    public double StoppedOpacity { get; set; } = 70;
    public double PlayingOpacity { get; set; } = 70;
    public PadImageFit StoppedFit { get; set; } = PadImageFit.Crop;
    public PadImageFit PlayingFit { get; set; } = PadImageFit.Crop;
    public PadGraphicsSettings Clone() => (PadGraphicsSettings)MemberwiseClone();
}
