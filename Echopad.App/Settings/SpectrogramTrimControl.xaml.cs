using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Echopad.App.Settings
{
    public partial class SpectrogramTrimControl : UserControl
    {
        // ==============================
        // Dependency Properties
        // ==============================
        public static readonly DependencyProperty AudioPathProperty =
            DependencyProperty.Register(nameof(AudioPath), typeof(string), typeof(SpectrogramTrimControl),
                new PropertyMetadata(null, OnAudioChanged));

        public static readonly DependencyProperty DurationMsProperty =
            DependencyProperty.Register(nameof(DurationMs), typeof(int), typeof(SpectrogramTrimControl),
                new PropertyMetadata(0, OnAudioChanged));

        public static readonly DependencyProperty StartMsProperty =
            DependencyProperty.Register(nameof(StartMs), typeof(int), typeof(SpectrogramTrimControl),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTrimChanged));

        public static readonly DependencyProperty EndMsProperty =
            DependencyProperty.Register(nameof(EndMs), typeof(int), typeof(SpectrogramTrimControl),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTrimChanged));

        public string? AudioPath
        {
            get => (string?)GetValue(AudioPathProperty);
            set => SetValue(AudioPathProperty, value);
        }

        public int DurationMs
        {
            get => (int)GetValue(DurationMsProperty);
            set => SetValue(DurationMsProperty, value);
        }

        public int StartMs
        {
            get => (int)GetValue(StartMsProperty);
            set => SetValue(StartMsProperty, value);
        }

        public int EndMs
        {
            get => (int)GetValue(EndMsProperty);
            set => SetValue(EndMsProperty, value);
        }

        // ==============================
        // Internal cached spectrogram data
        // ==============================
        private (float min, float max)[]? _waveform;
        
        private bool _dragIn;
        private bool _dragOut;

        public SpectrogramTrimControl()
        {
            InitializeComponent();

            Loaded += (_, __) => { RebuildIfNeeded(); UpdateOverlay(); };
            SizeChanged += (_, __) => { RedrawBitmap(); UpdateOverlay(); };

            Overlay.MouseLeftButtonDown += Overlay_MouseLeftButtonDown;
            Overlay.MouseMove += Overlay_MouseMove;
            Overlay.MouseLeftButtonUp += Overlay_MouseLeftButtonUp;

            // Overlay visuals
            var cutBrush = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)); // darken cut area
            CutLeft.Fill = cutBrush;
            CutRight.Fill = cutBrush;

            InLine.Stroke = Brushes.White;
            InLine.StrokeThickness = 2;
            InLine.SnapsToDevicePixels = true;

            OutLine.Stroke = Brushes.White;
            OutLine.StrokeThickness = 2;
            OutLine.SnapsToDevicePixels = true;
        }

        // ==============================
        // Events
        // ==============================
        private static void OnAudioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (SpectrogramTrimControl)d;
            c.RebuildIfNeeded();
        }

        private static void OnTrimChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (SpectrogramTrimControl)d;
            c.UpdateOverlay();
        }

        // ==============================
        // Build / Render
        // ==============================
        private void RebuildIfNeeded()
        {
            if (!IsLoaded) return;

            if (string.IsNullOrWhiteSpace(AudioPath) || DurationMs <= 0)
            {
                _waveform = null;
                Img.Source = null;
                UpdateOverlay();
                return;
            }

            var path = AudioPath!;

            // Kick off a lightweight build
            _ = Task.Run(() =>
            {
                try
                {
                    int columns = Math.Max(64, (int)Math.Round(ActualWidth)); // bars roughly match control width
                    var wf = BuildWaveform(path, columns);

                    Dispatcher.Invoke(() =>
                    {
                        _waveform = wf;   // (float min, float max)[]
                        RedrawBitmap();
                        UpdateOverlay();
                    });
                }
                catch
                {
                    Dispatcher.Invoke(() =>
                    {
                        _waveform = null;

                        // Debug fallback so you KNOW the Image layer works
                        Img.Source = BuildDebugPattern(
                            Math.Max(2, (int)Math.Round(ActualWidth)),
                            Math.Max(2, (int)Math.Round(ActualHeight)));

                        UpdateOverlay();
                    });
                }
            });
        }


        private void RedrawBitmap()
        {
            if (_waveform == null || ActualWidth < 10 || ActualHeight < 10)
            {
                Img.Source = null;
                return;
            }

            int w = Math.Max(2, (int)ActualWidth);
            int h = Math.Max(2, (int)ActualHeight);
            int midY = h / 2;

            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            int stride = w * 4;
            byte[] pixels = new byte[h * stride];

            int bars = _waveform.Length;
            int barWidth = 2;   // tweak: 1–3
            int gap = 1;
            int step = barWidth + gap;

            for (int x = 0; x < w; x += step)
            {
                int i = (int)((x / (double)w) * bars);
                if (i < 0 || i >= bars) continue;

                var (min, max) = _waveform[i];

                int yTop = midY - (int)(max * midY);
                int yBot = midY - (int)(min * midY);

                yTop = Math.Clamp(yTop, 0, h - 1);
                yBot = Math.Clamp(yBot, 0, h - 1);

                for (int xx = x; xx < Math.Min(w, x + barWidth); xx++)
                {
                    for (int y = yTop; y <= yBot; y++)
                    {
                        int idx = y * stride + xx * 4;
                        pixels[idx + 0] = 0;     // B
                        pixels[idx + 1] = 220;   // G
                        pixels[idx + 2] = 255;   // R (bright yellow)
                        pixels[idx + 3] = 255;   // A
                    }
                }
            }

            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
            Img.Source = wb;
        }



        // ==============================
        // Overlay (handles + dark regions)
        // ==============================
        private void UpdateOverlay()
        {
            if (!IsLoaded || ActualWidth <= 1 || ActualHeight <= 1 || DurationMs <= 0)
            {
                CutLeft.Width = 0;
                CutRight.Width = 0;
                return;
            }

            double w = ActualWidth;
            double h = ActualHeight;

            int start = Math.Clamp(StartMs, 0, DurationMs);
            int end = Math.Clamp(EndMs <= 0 ? DurationMs : EndMs, 0, DurationMs);
            if (end < start) end = start;

            double xIn = (start / (double)DurationMs) * w;
            double xOut = (end / (double)DurationMs) * w;

            Canvas.SetLeft(CutLeft, 0);
            Canvas.SetTop(CutLeft, 0);
            CutLeft.Width = Math.Max(0, xIn);
            CutLeft.Height = h;

            Canvas.SetLeft(CutRight, xOut);
            Canvas.SetTop(CutRight, 0);
            CutRight.Width = Math.Max(0, w - xOut);
            CutRight.Height = h;

            InLine.X1 = InLine.X2 = xIn;
            InLine.Y1 = 0;
            InLine.Y2 = h;

            OutLine.X1 = OutLine.X2 = xOut;
            OutLine.Y1 = 0;
            OutLine.Y2 = h;
        }

        // ==============================
        // Mouse interaction
        // ==============================
        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DurationMs <= 0) return;

            var pos = e.GetPosition(Overlay);
            double x = pos.X;

            double w = ActualWidth;
            if (w <= 1) return;

            double xIn = (StartMs / (double)DurationMs) * w;
            double xOut = (EndMs / (double)DurationMs) * w;

            double dIn = Math.Abs(x - xIn);
            double dOut = Math.Abs(x - xOut);

            _dragIn = dIn <= 10 && dIn <= dOut;
            _dragOut = dOut <= 10 && dOut < dIn;

            if (!_dragIn && !_dragOut)
            {
                if (dIn <= dOut) _dragIn = true;
                else _dragOut = true;
            }

            Overlay.CaptureMouse();
            ApplyXToHandle(x);
            e.Handled = true;
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragIn && !_dragOut) return;
            var pos = e.GetPosition(Overlay);
            ApplyXToHandle(pos.X);
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragIn = false;
            _dragOut = false;
            Overlay.ReleaseMouseCapture();
        }

        private void ApplyXToHandle(double x)
        {
            double w = ActualWidth;
            if (w <= 1 || DurationMs <= 0) return;

            x = Math.Clamp(x, 0, w);
            int ms = (int)Math.Round((x / w) * DurationMs);

            int start = StartMs;
            int end = EndMs <= 0 ? DurationMs : EndMs;

            if (_dragIn)
            {
                start = Math.Clamp(ms, 0, end);
                StartMs = start;
            }
            else if (_dragOut)
            {
                end = Math.Clamp(ms, start, DurationMs);
                EndMs = end;
            }

            UpdateOverlay();
        }

        // ==============================
        // Lightweight spectrogram builder
        // ==============================
        private static (float min, float max)[] BuildWaveform(string path, int columns)
        {
            using var reader = new AudioFileReader(path);

            int channels = reader.WaveFormat.Channels;

            // Estimate total float samples (interleaved). AudioFileReader is float-based.
            long totalFloatSamples = reader.Length / sizeof(float);
            long totalFrames = totalFloatSamples / Math.Max(1, channels);

            long framesPerColumn = Math.Max(1, totalFrames / Math.Max(1, columns));

            var result = new (float min, float max)[columns];

            float[] buffer = new float[4096 * channels];

            long frameIndexInColumn = 0;
            int col = 0;

            float min = 1f;
            float max = -1f;

            while (true)
            {
                int read = reader.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;

                int framesRead = read / channels;

                for (int i = 0; i < framesRead; i++)
                {
                    float s = 0f;
                    int baseIdx = i * channels;
                    for (int c = 0; c < channels; c++)
                        s += buffer[baseIdx + c];
                    s /= channels;

                    if (s < min) min = s;
                    if (s > max) max = s;

                    frameIndexInColumn++;

                    if (frameIndexInColumn >= framesPerColumn)
                    {
                        if (col < columns)
                            result[col] = (min, max);

                        col++;
                        if (col >= columns)
                            return result;

                        frameIndexInColumn = 0;
                        min = 1f;
                        max = -1f;
                    }
                }
            }

            // Fill remaining columns with the last seen values (or silence if none)
            for (; col < columns; col++)
                result[col] = (min == 1f && max == -1f) ? (0f, 0f) : (min, max);

            return result;
        }


        // ==============================
        // Debug pattern (visible if build fails)
        // ==============================
        private static WriteableBitmap BuildDebugPattern(int w, int h)
        {
            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            int stride = w * 4;
            byte[] pixels = new byte[h * stride];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool stripe = ((x + y) / 10) % 2 == 0;
                    byte r = stripe ? (byte)255 : (byte)40;
                    byte g = stripe ? (byte)220 : (byte)40;
                    byte b = stripe ? (byte)0 : (byte)40;

                    int idx = (y * stride) + (x * 4);
                    pixels[idx + 0] = b;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = r;
                    pixels[idx + 3] = 255;
                }
            }

            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
            return wb;
        }
    }
}
