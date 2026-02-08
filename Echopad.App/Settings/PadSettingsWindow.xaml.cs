using Echopad.App.Services;
using Echopad.Core;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;          // CompositionTarget.Rendering
using System.Windows.Threading;

// ONLY for ColorDialog (avoid namespace collisions)
using WF = System.Windows.Forms;

namespace Echopad.App.Settings
{
    public partial class PadSettingsWindow : Window
    {
        private readonly PadSettingsViewModel _vm;

        // Prevent double-learn / re-entrancy
        private bool _isLearningMidi;

        // Preview routing (monitor out)
        private readonly GlobalSettings _previewGlobal;

        public PadSettingsWindow(PadSettingsViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = _vm;

            _previewGlobal = new SettingsService().Load();

            EnsurePreviewTimerWired();

            Loaded += (_, __) =>
            {
                // IMPORTANT: Canvas must NOT steal mouse for the whole area,
                // otherwise trim legs won't receive input.
                if (PlayheadOverlay != null)
                    PlayheadOverlay.Background = null;

                // Wire waveform click handlers (no XAML changes required)
                if (WaveHost != null)
                {
                    WaveHost.PreviewMouseLeftButtonDown -= WaveHost_PreviewMouseLeftButtonDown;
                    WaveHost.PreviewMouseLeftButtonDown += WaveHost_PreviewMouseLeftButtonDown;

                    WaveHost.PreviewMouseRightButtonDown -= WaveHost_PreviewMouseRightButtonDown;
                    WaveHost.PreviewMouseRightButtonDown += WaveHost_PreviewMouseRightButtonDown;

                    WaveHost.PreviewMouseMove -= WaveHost_PreviewMouseMove;
                    WaveHost.PreviewMouseMove += WaveHost_PreviewMouseMove;

                    WaveHost.PreviewMouseRightButtonUp -= WaveHost_PreviewMouseRightButtonUp;
                    WaveHost.PreviewMouseRightButtonUp += WaveHost_PreviewMouseRightButtonUp;
                }

                // Start playhead at IN
                SafeSetPlayheadToIn();

                // ===================== SMOOTH PLAYHEAD RENDERING =====================
                CompositionTarget.Rendering -= CompositionTarget_Rendering;
                CompositionTarget.Rendering += CompositionTarget_Rendering;
                _renderHooked = true;
            };

            Unloaded += (_, __) =>
            {
                if (_renderHooked)
                {
                    CompositionTarget.Rendering -= CompositionTarget_Rendering;
                    _renderHooked = false;
                }
            };

            // Keep playhead inside when trim changes (TextBox edits etc.)
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PadSettingsViewModel.StartMs) ||
                    e.PropertyName == nameof(PadSettingsViewModel.EndMs))
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        ClampPlayheadIntoTrim();
                        UpdatePlayheadVisual(_previewPosMs);
                    }, DispatcherPriority.Background);
                }
            };
        }

        // =====================================================
        // OK / Cancel
        // =====================================================
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            _vm.Save();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        // =====================================================
        // Browse
        // =====================================================
        private void BrowseAudio_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select audio file",
                Filter = "Audio Files|*.wav;*.mp3;*.aac;*.m4a;*.wma|All Files|*.*"
            };

            if (dlg.ShowDialog(this) == true)
            {
                _vm.SetClipFromFile(dlg.FileName);

                _vm.PreviewPlayheadMs = _vm.StartMs;
                _previewPosMs = _vm.StartMs;

                _endedNaturally = false;
                _autoStopping = false;

                UpdatePlayheadVisual(_previewPosMs);
            }
        }

        // =====================================================
        // Hotkey
        // =====================================================
        private void SetPadHotkey_Click(object sender, RoutedEventArgs e)
        {
            var cap = new HotkeyCaptureWindow(_vm.PadHotkey) { Owner = this };
            if (cap.ShowDialog() == true)
                _vm.PadHotkey = string.IsNullOrWhiteSpace(cap.HotkeyText) ? null : cap.HotkeyText;
        }

        private void ClearPadHotkey_Click(object sender, RoutedEventArgs e)
        {
            _vm.PadHotkey = null;
        }

        // =====================================================
        // Trim buttons
        // =====================================================
        private void ResetTrim_Click(object sender, RoutedEventArgs e)
        {
            _vm.ResetTrim();
            _vm.PreviewPlayheadMs = _vm.StartMs;
            _previewPosMs = _vm.StartMs;

            _endedNaturally = false;
            _autoStopping = false;

            UpdatePlayheadVisual(_previewPosMs);
        }

        private void StartMs_Up_Click(object sender, RoutedEventArgs e)
        {
            _vm.NudgeStart(+_vm.TrimStepMs);
            ClampPlayheadIntoTrim();
            UpdatePlayheadVisual(_previewPosMs);
        }

        private void StartMs_Down_Click(object sender, RoutedEventArgs e)
        {
            _vm.NudgeStart(-_vm.TrimStepMs);
            ClampPlayheadIntoTrim();
            UpdatePlayheadVisual(_previewPosMs);
        }

        private void EndMs_Up_Click(object sender, RoutedEventArgs e)
        {
            _vm.NudgeEnd(+_vm.TrimStepMs);
            ClampPlayheadIntoTrim();
            UpdatePlayheadVisual(_previewPosMs);
        }

        private void EndMs_Down_Click(object sender, RoutedEventArgs e)
        {
            _vm.NudgeEnd(-_vm.TrimStepMs);
            ClampPlayheadIntoTrim();
            UpdatePlayheadVisual(_previewPosMs);
        }

        // =========================================================
        // PER-PAD MIDI LEARN
        // =========================================================
        private void LearnMidiPad_Click(object sender, RoutedEventArgs e)
        {
            if (_isLearningMidi) return;

            _isLearningMidi = true;
            _vm.IsMidiLearning = true;

            if (sender is Button b)
                b.IsEnabled = false;

            _vm.MidiTriggerRaw = "Learning...";

            var mw = Application.Current?.MainWindow;
            if (mw == null)
            {
                EndLearnUi(sender as Button);
                _vm.MidiTriggerRaw = "Learn:Failed (no MainWindow)";
                return;
            }

            Action<string> onLearned = bind =>
            {
                Dispatcher.Invoke(() =>
                {
                    _vm.MidiTriggerRaw = bind;
                    EndLearnUi(sender as Button);
                }, DispatcherPriority.Send);
            };

            Action onCanceled = () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (string.Equals(_vm.MidiTriggerRaw, "Learning...", StringComparison.OrdinalIgnoreCase))
                        _vm.MidiTriggerRaw = "Learn:Canceled";
                    EndLearnUi(sender as Button);
                }, DispatcherPriority.Send);
            };

            if (!TryInvokeMainWindowMidiLearn((Window)mw, onLearned, onCanceled))
            {
                EndLearnUi(sender as Button);
                _vm.MidiTriggerRaw = "Learn:Failed (no learn hook)";
            }
        }

        private void EndLearnUi(Button? learnButton)
        {
            _isLearningMidi = false;
            _vm.IsMidiLearning = false;

            if (learnButton != null)
                learnButton.IsEnabled = true;
        }

        private static bool TryInvokeMainWindowMidiLearn(Window mainWindow, Action<string> onLearned, Action onCanceled)
        {
            var t = mainWindow.GetType();
            string[] names = { "BeginMidiLearn", "StartMidiLearn", "BeginLearnMidi", "StartLearnMidi" };

            foreach (var name in names)
            {
                {
                    var mi = t.GetMethod(name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(Action<string>) },
                        null);

                    if (mi != null)
                    {
                        mi.Invoke(mainWindow, new object[] { onLearned });
                        return true;
                    }
                }

                {
                    var mi = t.GetMethod(name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(Action<string>), typeof(Action) },
                        null);

                    if (mi != null)
                    {
                        mi.Invoke(mainWindow, new object[] { onLearned, onCanceled });
                        return true;
                    }
                }

                {
                    var mi = t.GetMethod(name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(string), typeof(Action<string>) },
                        null);

                    if (mi != null)
                    {
                        mi.Invoke(mainWindow, new object[] { "Pad", onLearned });
                        return true;
                    }
                }

                {
                    var mi = t.GetMethod(name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(string), typeof(Action<string>), typeof(Action) },
                        null);

                    if (mi != null)
                    {
                        mi.Invoke(mainWindow, new object[] { "Pad", onLearned, onCanceled });
                        return true;
                    }
                }
            }

            return false;
        }

        // =========================================================
        // Color pickers
        // =========================================================
        private void PickActiveColor_Click(object sender, RoutedEventArgs e)
            => PickColor(hex => _vm.UiActiveHex = hex);

        private void PickRunningColor_Click(object sender, RoutedEventArgs e)
            => PickColor(hex => _vm.UiRunningHex = hex);

        private void PickColor(Action<string> apply)
        {
            using var dlg = new WF.ColorDialog { FullOpen = true };
            if (dlg.ShowDialog() == WF.DialogResult.OK)
            {
                var c = dlg.Color;
                apply($"#{c.R:X2}{c.G:X2}{c.B:X2}");
            }
        }

        // =====================================================
        // PLAYHEAD + LEGS INTERACTION
        // =====================================================

        private bool _isDraggingPlayhead;

        // right-drag legs state
        private bool _isRightDraggingLeg;
        private bool _rightDragIsInLeg;

        private const double LEG_HANDLE_RADIUS_PX = 18;
        private const double BETWEEN_MARGIN_PX = 2;

        private double WaveWidth => WaveHost?.ActualWidth ?? 0;
        private double WaveHeight => WaveHost?.ActualHeight ?? 0;

        private bool TryGetLegXs(out double inX, out double outX)
        {
            inX = 0;
            outX = 0;

            double w = WaveWidth;
            if (w <= 2) return false;

            int dur = Math.Max(1, _vm.ClipDurationMs);

            inX = (_vm.StartMs / (double)dur) * w;
            outX = (_vm.EndMs / (double)dur) * w;

            if (outX < inX)
            {
                var tmp = inX;
                inX = outX;
                outX = tmp;
            }

            return true;
        }

        private bool IsNearLeg(double x, double inX, double outX)
            => Math.Abs(x - inX) <= LEG_HANDLE_RADIUS_PX || Math.Abs(x - outX) <= LEG_HANDLE_RADIUS_PX;

        private bool IsBetweenLegs(double x, out double inX, out double outX)
        {
            if (!TryGetLegXs(out inX, out outX))
                return false;

            return x >= (inX + BETWEEN_MARGIN_PX) && x <= (outX - BETWEEN_MARGIN_PX);
        }

        private int XToMs_BetweenLegs(double x)
        {
            if (!TryGetLegXs(out var inX, out var outX))
                return _vm.StartMs;

            x = Math.Max(inX, Math.Min(outX, x));

            double spanPx = Math.Max(1, outX - inX);
            double t = (x - inX) / spanPx;

            int inMs = _vm.StartMs;
            int outMs = _vm.EndMs;
            int spanMs = Math.Max(1, outMs - inMs);

            int ms = inMs + (int)Math.Round(t * spanMs);
            if (ms < inMs) ms = inMs;
            if (ms > outMs) ms = outMs;
            return ms;
        }

        private double MsToX_BetweenLegs(double ms)
        {
            if (!TryGetLegXs(out var inX, out var outX))
                return 0;

            double inMs = _vm.StartMs;
            double outMs = _vm.EndMs;
            double spanMs = Math.Max(1, outMs - inMs);

            if (ms < inMs) ms = inMs;
            if (ms > outMs) ms = outMs;

            double t = (ms - inMs) / spanMs;
            return inX + t * (outX - inX);
        }

        private void SetPlayheadMs(int ms)
        {
            if (ms < _vm.StartMs) ms = _vm.StartMs;
            if (ms > _vm.EndMs) ms = _vm.EndMs;

            _vm.PreviewPlayheadMs = ms;
            _previewPosMs = ms;

            // cancels "ended" state
            _endedNaturally = false;
            _autoStopping = false;

            UpdatePlayheadVisual(_previewPosMs);
        }

        private void SafeSetPlayheadToIn()
        {
            try
            {
                if (_vm.PreviewPlayheadMs < _vm.StartMs || _vm.PreviewPlayheadMs > _vm.EndMs)
                    _vm.PreviewPlayheadMs = _vm.StartMs;

                _previewPosMs = _vm.PreviewPlayheadMs;
                UpdatePlayheadVisual(_previewPosMs);
            }
            catch { }
        }

        private void ClampPlayheadIntoTrim()
        {
            double ph = _previewPosMs;

            if (ph < _vm.StartMs) ph = _vm.StartMs;
            if (ph > _vm.EndMs) ph = _vm.EndMs;

            _previewPosMs = ph;

            int asInt = (int)Math.Round(ph);
            if (asInt < _vm.StartMs) asInt = _vm.StartMs;
            if (asInt > _vm.EndMs) asInt = _vm.EndMs;
            _vm.PreviewPlayheadMs = asInt;
        }

        // LEFT click between legs = move playhead (not near legs)
        private void WaveHost_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (WaveHost == null) return;

            var p = e.GetPosition(WaveHost);

            if (!IsBetweenLegs(p.X, out var inX, out var outX))
                return;

            if (IsNearLeg(p.X, inX, outX))
                return;

            if (_previewIsPlaying)
                PausePreview();

            SetPlayheadMs(XToMs_BetweenLegs(p.X));
            e.Handled = true;
        }

        // RIGHT click/drag between legs = move nearer leg
        private void WaveHost_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (WaveHost == null) return;

            var p = e.GetPosition(WaveHost);

            if (!IsBetweenLegs(p.X, out var inX, out var outX))
                return;

            _rightDragIsInLeg = Math.Abs(p.X - inX) <= Math.Abs(p.X - outX);

            _isRightDraggingLeg = true;
            WaveHost.CaptureMouse();

            ApplyRightDragLeg(XToMs_BetweenLegs(p.X));
            e.Handled = true;
        }

        private void WaveHost_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isRightDraggingLeg || WaveHost == null)
                return;

            var p = e.GetPosition(WaveHost);
            ApplyRightDragLeg(XToMs_BetweenLegs(p.X));
            e.Handled = true;
        }

        private void WaveHost_PreviewMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isRightDraggingLeg)
                return;

            _isRightDraggingLeg = false;
            try { WaveHost?.ReleaseMouseCapture(); } catch { }

            e.Handled = true;
        }

        private void ApplyRightDragLeg(int ms)
        {
            // trim edits cancel end-state
            _endedNaturally = false;
            _autoStopping = false;

            ms = Math.Max(0, Math.Min(_vm.ClipDurationMs, ms));

            if (_rightDragIsInLeg)
            {
                int newIn = Math.Min(ms, _vm.EndMs);
                _vm.StartMs = newIn;

                if (_vm.PreviewPlayheadMs < _vm.StartMs)
                    SetPlayheadMs(_vm.StartMs);
                else
                {
                    ClampPlayheadIntoTrim();
                    UpdatePlayheadVisual(_previewPosMs);
                }
            }
            else
            {
                int newOut = Math.Max(ms, _vm.StartMs);
                _vm.EndMs = newOut;

                if (_vm.PreviewPlayheadMs > _vm.EndMs)
                    SetPlayheadMs(_vm.EndMs);
                else
                {
                    ClampPlayheadIntoTrim();
                    UpdatePlayheadVisual(_previewPosMs);
                }
            }
        }

        // Overlay drag only when clicking shapes
        private void PlayheadOverlay_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource == PlayheadOverlay)
                return;

            if (_previewIsPlaying)
                PausePreview();

            _isDraggingPlayhead = true;
            try { PlayheadOverlay?.CaptureMouse(); } catch { }

            if (WaveHost == null) return;

            var p = e.GetPosition(WaveHost);
            if (!IsBetweenLegs(p.X, out _, out _))
                return;

            SetPlayheadMs(XToMs_BetweenLegs(p.X));
            e.Handled = true;
        }

        private void PlayheadOverlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDraggingPlayhead || WaveHost == null)
                return;

            var p = e.GetPosition(WaveHost);
            if (!IsBetweenLegs(p.X, out _, out _))
                return;

            SetPlayheadMs(XToMs_BetweenLegs(p.X));
            e.Handled = true;
        }

        private void PlayheadOverlay_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isDraggingPlayhead)
                return;

            _isDraggingPlayhead = false;
            try { PlayheadOverlay?.ReleaseMouseCapture(); } catch { }

            e.Handled = true;
        }

        // =====================================================
        // VISUALS: draw using DOUBLE ms (smooth)
        // =====================================================
        private void UpdatePlayheadVisual(double playheadMs)
        {
            if (WaveHost == null || PlayheadOverlay == null)
                return;

            double width = WaveWidth;
            double height = WaveHeight;

            if (width <= 2 || height <= 2)
                return;

            const double LEG_TOP = 14;
            const double LEG_BOTTOM = 14;

            double yTop = LEG_TOP;
            double yBottom = Math.Max(yTop + 10, height - LEG_BOTTOM);
            double lineH = Math.Max(2, yBottom - yTop);

            // clamp double playhead into [IN, OUT]
            if (playheadMs < _vm.StartMs) playheadMs = _vm.StartMs;
            if (playheadMs > _vm.EndMs) playheadMs = _vm.EndMs;

            double x = MsToX_BetweenLegs(playheadMs);

            double bodyLeft = x - (PlayheadBody.Width / 2.0);
            double coreLeft = x - (PlayheadCore.Width / 2.0);

            PlayheadBody.Height = lineH;
            PlayheadCore.Height = lineH;

            Canvas.SetLeft(PlayheadBody, bodyLeft);
            Canvas.SetTop(PlayheadBody, yTop);

            Canvas.SetLeft(PlayheadCore, coreLeft);
            Canvas.SetTop(PlayheadCore, yTop);

            double triLeft = x - 5;

            Canvas.SetLeft(PlayheadTopTri, triLeft);
            Canvas.SetTop(PlayheadTopTri, 0);

            Canvas.SetLeft(PlayheadBotTri, triLeft);
            Canvas.SetTop(PlayheadBotTri, height - 6);

            double hitLeft = x - 13;

            Canvas.SetLeft(PlayheadHitTop, hitLeft);
            Canvas.SetTop(PlayheadHitTop, 0);

            Canvas.SetLeft(PlayheadHitBot, hitLeft);
            Canvas.SetTop(PlayheadHitBot, height - 18);
        }

        // =====================================================
        // PREVIEW AUDIO
        // =====================================================
        private IWavePlayer? _previewOut;
        private AudioFileReader? _previewReader;

        // timer only for lightweight logic (not visual smoothness)
        private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
        private bool _previewTimerWired = false;

        // IMPORTANT: single source of truth for resume position
        private double _previewPosMs = -1;

        private bool _previewIsPlaying;

        // Smooth clock
        private readonly Stopwatch _previewClock = new Stopwatch();
        private double _clockBaseMs;

        // Segment bookkeeping
        private double _segmentEndMs;   // absolute OUT
        private double _segmentTakeMs;  // segment length from start position -> OUT

        private bool _autoStopping;
        private bool _endedNaturally;
        private bool _userPaused;

        // Some drivers fire PlaybackStopped when you call Pause(). Ignore that one callback.
        private bool _ignoreNextStoppedAfterPause;

        // Rendering hook state
        private bool _renderHooked;

        private void EnsurePreviewTimerWired()
        {
            if (_previewTimerWired)
                return;

            _previewTimer.Tick += (_, __) => PreviewTick_Slow();
            _previewTimerWired = true;
        }

        private void PreviewPlayPause_Click(object sender, RoutedEventArgs e)
        {
            var path = _vm.ClipPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            double startMs = _vm.StartMs;
            double endMs = _vm.EndMs;

            if (endMs <= startMs)
                return;

            if (_previewIsPlaying)
            {
                PausePreview();
                return;
            }

            StartOrResumePreview(path, startMs, endMs);
        }

        private void StartOrResumePreview(string path, double startMs, double endMs)
        {
            EnsurePreviewTimerWired();
            EnsurePreviewReader(path);

            _userPaused = false;
            _autoStopping = false;

            // If the last run truly ended at OUT, restart at IN
            if (_endedNaturally)
            {
                _previewPosMs = startMs;
                _endedNaturally = false;
            }

            // Resume from last known absolute position (pause/drag already updated it)
            if (_previewPosMs < 0)
                _previewPosMs = startMs;

            // Clamp into trim
            if (_previewPosMs < startMs) _previewPosMs = startMs;
            if (_previewPosMs > endMs) _previewPosMs = endMs;

            // If effectively at OUT, restart at IN (no extra click)
            if (_previewPosMs >= endMs - 1)
                _previewPosMs = startMs;

            _clockBaseMs = _previewPosMs;
            _segmentEndMs = endMs;

            var takeMs = Math.Max(0, endMs - _previewPosMs);
            if (takeMs <= 1)
            {
                _previewPosMs = startMs;
                SetPlayheadMs((int)startMs);
                _vm.PreviewPlayButtonText = "Play";
                return;
            }

            _segmentTakeMs = takeMs;

            _previewReader!.CurrentTime = TimeSpan.FromMilliseconds(_previewPosMs);

            var segment = new NAudio.Wave.SampleProviders.OffsetSampleProvider(_previewReader)
            {
                SkipOver = TimeSpan.Zero,
                Take = TimeSpan.FromMilliseconds(takeMs)
            };

            RecreatePreviewOutAlways();
            _previewOut!.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(segment));

            // Start clock before Play() to avoid first-frame snap
            _previewClock.Reset();
            _previewClock.Start();

            _previewOut.Play();
            _previewIsPlaying = true;
            _vm.PreviewPlayButtonText = "Pause";

            _vm.PreviewPlayheadMs = (int)Math.Round(_previewPosMs);
            UpdatePlayheadVisual(_previewPosMs);

            _previewTimer.Start();
        }

        private void PausePreview()
        {
            if (_previewOut == null)
                return;

            _userPaused = true;

            if (_previewClock.IsRunning)
                _previewClock.Stop();

            // Capture paused position (absolute ms)
            _previewPosMs = _clockBaseMs + _previewClock.Elapsed.TotalMilliseconds;

            if (_previewPosMs < _vm.StartMs) _previewPosMs = _vm.StartMs;
            if (_previewPosMs > _vm.EndMs) _previewPosMs = _vm.EndMs;

            _clockBaseMs = _previewPosMs;

            // Some drivers fire PlaybackStopped on Pause()
            _ignoreNextStoppedAfterPause = true;

            // Flip state BEFORE pausing
            _previewIsPlaying = false;

            try { _previewOut.Pause(); } catch { }

            _vm.PreviewPlayButtonText = "Play";
            _previewTimer.Stop();

            _vm.PreviewPlayheadMs = (int)Math.Round(_previewPosMs);
            UpdatePlayheadVisual(_previewPosMs);
        }

        // Slow tick updates VM field (optional), NOT the visuals
        private void PreviewTick_Slow()
        {
            if (!_previewIsPlaying || !_previewClock.IsRunning)
                return;

            double pos = _clockBaseMs + _previewClock.Elapsed.TotalMilliseconds;

            if (pos < _vm.StartMs) pos = _vm.StartMs;
            if (pos > _vm.EndMs) pos = _vm.EndMs;

            _previewPosMs = pos;

            int msInt = (int)Math.Round(pos);
            if (_vm.PreviewPlayheadMs != msInt)
                _vm.PreviewPlayheadMs = msInt;

            if (!_autoStopping && pos >= (_segmentEndMs - 1))
            {
                _autoStopping = true;
                try { _previewOut?.Stop(); } catch { }
            }
        }

        // Frame-driven smooth draw
        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (!_previewIsPlaying || !_previewClock.IsRunning)
                return;

            double pos = _clockBaseMs + _previewClock.Elapsed.TotalMilliseconds;

            if (pos < _vm.StartMs) pos = _vm.StartMs;
            if (pos > _vm.EndMs) pos = _vm.EndMs;

            _previewPosMs = pos;

            // Smooth visuals (double)
            UpdatePlayheadVisual(_previewPosMs);

            if (!_autoStopping && pos >= (_segmentEndMs - 1))
            {
                _autoStopping = true;
                try { _previewOut?.Stop(); } catch { }
            }
        }

        private void EnsurePreviewReader(string path)
        {
            if (_previewReader != null &&
                !string.Equals(_previewReader.FileName, path, StringComparison.OrdinalIgnoreCase))
            {
                DisposePreview();
            }

            if (_previewReader == null)
                _previewReader = new AudioFileReader(path);
        }

        private void RecreatePreviewOutAlways()
        {
            try { _previewOut?.Stop(); } catch { }
            try { _previewOut?.Dispose(); } catch { }
            _previewOut = null;

            var monitorId = _previewGlobal?.MonitorOutDeviceId;

            if (!string.IsNullOrWhiteSpace(monitorId))
            {
                try
                {
                    var enumerator = new MMDeviceEnumerator();
                    var dev = enumerator.GetDevice(monitorId);

                    _previewOut = new WasapiOut(
                        dev,
                        AudioClientShareMode.Shared,
                        useEventSync: false,
                        latency: 50);
                }
                catch
                {
                    _previewOut = new WaveOutEvent();
                }
            }
            else
            {
                _previewOut = new WaveOutEvent();
            }

            _previewOut.PlaybackStopped += (_, __) =>
            {
                // Ignore the stop callback caused by Pause() on some drivers
                if (_ignoreNextStoppedAfterPause)
                {
                    _ignoreNextStoppedAfterPause = false;
                    _userPaused = false;
                    _autoStopping = false;
                    return;
                }

                try { _previewClock.Stop(); } catch { }
                try { _previewTimer.Stop(); } catch { }

                bool paused = _userPaused;

                _previewIsPlaying = false;
                _vm.PreviewPlayButtonText = "Play";

                // If user-paused, keep position
                if (paused)
                {
                    _userPaused = false;
                    _autoStopping = false;
                    return;
                }

                // Decide if it ended near OUT (helps short files that stop slightly early)
                bool endedNearOut = false;
                try
                {
                    double playedMs = _previewClock.Elapsed.TotalMilliseconds;

                    // tolerance: 12..80ms, scales a bit with segment size
                    double tol = Math.Max(12, Math.Min(80, _segmentTakeMs * 0.08));

                    endedNearOut =
                        _autoStopping ||
                        (_segmentTakeMs > 0 && playedMs >= (_segmentTakeMs - tol));
                }
                catch { }

                _autoStopping = false;

                if (endedNearOut)
                {
                    _endedNaturally = true;

                    _previewPosMs = _vm.StartMs;
                    _clockBaseMs = _previewPosMs;

                    _vm.PreviewPlayheadMs = (int)Math.Round(_previewPosMs);
                    UpdatePlayheadVisual(_previewPosMs);
                    return;
                }

                // Otherwise: keep wherever we are (device hiccup / external stop)
                _endedNaturally = false;
                ClampPlayheadIntoTrim();
                _clockBaseMs = _previewPosMs;
                UpdatePlayheadVisual(_previewPosMs);
            };
        }

        private void DisposePreview()
        {
            try { _previewTimer.Stop(); } catch { }
            try { _previewClock.Stop(); } catch { }

            try { _previewOut?.Stop(); } catch { }
            try { _previewOut?.Dispose(); } catch { }
            _previewOut = null;

            try { _previewReader?.Dispose(); } catch { }
            _previewReader = null;

            _previewIsPlaying = false;
            _previewPosMs = -1;

            _clockBaseMs = 0;
            _segmentEndMs = 0;
            _segmentTakeMs = 0;

            _autoStopping = false;
            _endedNaturally = false;
            _userPaused = false;
            _ignoreNextStoppedAfterPause = false;

            _vm.PreviewPlayButtonText = "Play";
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_renderHooked)
            {
                CompositionTarget.Rendering -= CompositionTarget_Rendering;
                _renderHooked = false;
            }

            DisposePreview();
            base.OnClosed(e);
        }
    }
}
