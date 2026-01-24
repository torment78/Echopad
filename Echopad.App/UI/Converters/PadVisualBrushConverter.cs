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

            // IMPORTANT: You said you DO NOT want global pad colors.
            // So only per-pad overrides for Active/Running, and global Armed input colors.
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
                return MakeTintedSurface(hex);

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

        // Background tint: subtle gradient
        private static Brush MakeTintedSurface(string hex)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);

                byte dR = (byte)(c.R * 0.20);
                byte dG = (byte)(c.G * 0.20);
                byte dB = (byte)(c.B * 0.20);

                byte tR = (byte)(c.R * 0.28);
                byte tG = (byte)(c.G * 0.28);
                byte tB = (byte)(c.B * 0.28);

                var top = Color.FromArgb(255, tR, tG, tB);
                var mid = Color.FromArgb(255, dR, dG, dB);
                var bot = Color.FromArgb(255, (byte)(dR * 0.85), (byte)(dG * 0.85), (byte)(dB * 0.85));

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
                return Brushes.Transparent;
            }
        }
    }
}
