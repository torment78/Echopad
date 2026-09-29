namespace Echopad.Core;

/// <summary>Hue choices use fixed saturation and brightness so pads remain understated.</summary>
public sealed class AppearanceSettings
{
    public double BackgroundHue { get; set; } = 215;
    public double CardHue { get; set; } = 215;
    public double OutlineHue { get; set; } = 215;
    public double ButtonHue { get; set; } = 215;
    public double ButtonTextHue { get; set; } = 190;
    public double TitleHue { get; set; } = 190;
    public double PadHue { get; set; } = 215;
    public double PadTextHue { get; set; } = 190;
    public double LoadedFillIntensity { get; set; } = 55;
    public double PlayingFillIntensity { get; set; } = 75;
    public double OutlineIntensity { get; set; } = 100;
}
