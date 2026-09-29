using Echopad.Core;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Echopad.App.UI.Converters
{
    /// <summary>
    /// Returns Brush for PAD visuals based on:
    /// - State (Empty/Armed/Loaded/Playing)
    /// - IsEchoMode + ClipPath (for Armed/Loaded normalization)
    /// - InputSource (for Armed color choosing input1/input2)
    /// - per-pad overrides: UiActiveHex, UiRunningHex
    /// - global armed input colors: UiArmedInput1Hex, UiArmedInput2Hex
    ///
    /// ConverterParameter:
    /// "Border" | "Glow" | "Background"
    ///
    /// MultiBinding values:
    /// 0: PadState
    /// 1: ClipPath (string)
    /// 2: IsEchoMode (bool)
    /// 3: InputSource (int)
    /// 4: PadIndex (int)
    /// 5: GlobalSettings
    /// </summary>
    public sealed class PadVisualBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 6)
                return Brushes.Transparent;

            if (values[0] is not PadState state)
                return Brushes.Transparent;

            var clipPath = values[1] as string;

            if (values[2] is not bool isEchoMode)
                return Brushes.Transparent;

            int inputSource = 1;
            if (values[3] is int src)
                inputSource = src;

            if (values[4] is not int padIndex)
                return Brushes.Transparent;

            if (values[5] is not GlobalSettings gs)
                return Brushes.Transparent;

            var mode = (parameter as string)?.Trim() ?? "Border";

            // per-pad settings for overrides
            var ps = gs.GetOrCreatePad(padIndex);

            bool hasFile = !string.IsNullOrWhiteSpace(clipPath);

            // Normalize state for visuals:
            // - No file + EchoMode => Armed
            // - Has file + not playing => Loaded
            if (!hasFile)
            {
                state = isEchoMode ? PadState.Armed : PadState.Empty;
            }
            else
            {
                if (state != PadState.Playing)
                    state = PadState.Loaded;
            }

            // Recorded and playing pads use their individual state colors.
            string hex = state switch
            {
                PadState.Playing =>
                    ps.UiRunningHex ?? "#00FF6A",

                PadState.Loaded =>
                    ps.UiActiveHex ?? "#3DFF8B",

                PadState.Armed =>
                    (inputSource <= 1
                        ? gs.UiArmedInput1Hex ?? "#FF4DB8"
                        : gs.UiArmedInput2Hex ?? "#4DA3FF"),

                _ =>
                    "#5A5A5A"
            };

            if (string.Equals(mode, "Background", StringComparison.OrdinalIgnoreCase))
            {
                var surface = Echopad.App.Services.AppearanceTheme.ColorAt(gs.Appearance?.PadHue ?? 215, .25, .14);
                return state is PadState.Loaded or PadState.Playing
                    ? MakeTintedSurface(surface, hex, state == PadState.Playing
                        ? gs.Appearance?.PlayingFillIntensity ?? 75
                        : gs.Appearance?.LoadedFillIntensity ?? 55)
                    : new SolidColorBrush(surface);
            }

            if (string.Equals(mode, "Glow", StringComparison.OrdinalIgnoreCase))
                return MakeGlowBrush(hex);

            return BrushFromHex(hex);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static Brush BrushFromHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Brushes.Transparent;

            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }
            catch
            {
                return Brushes.Transparent;
            }
        }

        // Glow brush: slightly lighter
        private static Brush MakeGlowBrush(string hex)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                var up = Color.FromArgb(255,
                    (byte)Math.Min(255, c.R + 30),
                    (byte)Math.Min(255, c.G + 30),
                    (byte)Math.Min(255, c.B + 30));

                var b = new SolidColorBrush(up);
                b.Freeze();
                return b;
            }
            catch
            {
                return Brushes.Transparent;
            }
        }

        // State colors cover the whole pad, with enough contrast to distinguish a
        // saved clip from an empty/listening pad without overpowering its label.
        private static Brush MakeTintedSurface(Color surface, string hex, double intensity)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                Color Mix(double amount) => Color.FromRgb(
                    (byte)Math.Round(surface.R * (1 - amount) + c.R * amount),
                    (byte)Math.Round(surface.G * (1 - amount) + c.G * amount),
                    (byte)Math.Round(surface.B * (1 - amount) + c.B * amount));

                double amount = Math.Clamp(intensity / 100, 0, 1) * .62;
                var top = Mix(amount * 1.23);
                var mid = Mix(amount);
                var bot = Mix(amount * .82);

                var g = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1),
                    GradientStops =
                    {
                        new GradientStop(top, 0),
                        new GradientStop(mid, 0.55),
                        new GradientStop(bot, 1),
                    }
                };
                g.Freeze();
                return g;
            }
            catch
            {
                return new SolidColorBrush(surface);
            }
        }
    }
}
