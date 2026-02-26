using Echopad.App.Services;
using Echopad.App.Settings;
using Echopad.App.UI.Input;
using Echopad.Audio;
using Echopad.Core;
using Echopad.Core.Controllers;
using NAudio.Gui;
using NAudio.Midi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Echopad.App
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // =====================================================
        // AUDIO
        // =====================================================
        // OLD:
        private readonly Echopad.Audio.IAudioEngine _audio = new Echopad.Audio.AudioEngine();
        // NEW: MainWindowViewModel
        public bool IsPadsInputEnabled => !Echopad.App.Services.UiInputBlocker.IsBlocked;
        // =====================================================
        // NEW: UI BLOCK SCOPE (blocks pad input while dialogs are open)
        // =====================================================
        private sealed class ActionOnDispose : IDisposable
        {
            private Action? _a;
            public ActionOnDispose(Action a) => _a = a;
            public void Dispose()
            {
                var a = _a;
                _a = null;
                try { a?.Invoke(); } catch { }
            }
        }

        private IDisposable BeginPadsUiBlock(string reason)
        {
            // If UiInputBlocker has a scope/token API, use it. If not, see note below.
            var token = Echopad.App.Services.UiInputBlocker.Block(reason);

            // Update XAML binding (IsPadsInputEnabled)
            OnPropertyChanged(nameof(IsPadsInputEnabled));

            return new ActionOnDispose(() =>
            {
                try { token.Dispose(); } catch { }
                OnPropertyChanged(nameof(IsPadsInputEnabled));
            });
        }
        // NEW:
        //private readonly Echopad.Audio.AudioEngine _audio = new Echopad.Audio.AudioEngine();
        // NEW: rolling buffer commit -> wav -> assign pad
        private readonly RollingBufferCommitService _bufferCommit = new RollingBufferCommitService();
        // =====================================================
        // INPUT TAPS (Phase 1: rolling RAM buffers for Input1/Input2)
        // =====================================================
        private InputTapEngine? _tapA;
        private InputTapEngine? _tapB;
        // =====================================================
        // FORCE PREVIEW ROUTING (monitor lane)
        // =====================================================
        private bool _padSettingsDialogOpen;   // any pad editor open
        private bool _forcePreviewInEditMode = true; // if you want edit mode ALWAYS preview

        // prevent settings dialog opening multiple times
        private bool _settingsDialogOpen;

        // NEW: prevents async-void PlayRequested re-entry (layering)
        private readonly HashSet<int> _startingPads = new();


        // =====================================================
        // PROFILES (NEW)
        // =====================================================
        private readonly ProfileService _profiles;

        private int _activeProfileIndex = 1;
        public int ActiveProfileIndex
        {
            get => _activeProfileIndex;
            private set
            {
                if (_activeProfileIndex == value) return;
                _activeProfileIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveProfileDisplay)); // NEW: keep squircle text live
            }
        }

        // XAML uses this (01..16)
        public string ActiveProfileDisplay => ActiveProfileIndex.ToString("00");

        // keep last N seconds in RAM (rolling buffer)
        private const int RollingBufferSeconds = 15;
        // =====================================================
        // SETTINGS (persisted)
        // =====================================================
        private readonly SettingsService _settingsService = new SettingsService();
        private GlobalSettings _globalSettings = new GlobalSettings();
        public SettingsService SettingsService => _settingsService;

        private readonly DropFolderWatcher _dropWatcher;

        // =====================================================
        // MIDI (single open port for app)
        // =====================================================
        private MidiIn? _midiIn;
        private MidiOut? _midiOut;
        // =====================================================
        // PROFILES: runtime modifier-hold state (MIDI)
        // =====================================================
        private bool _profileMidiModifierHeld;
        private DateTime _profileMidiModifierHeldUntilUtc = DateTime.MinValue;
        // one-shot MIDI learn callback (used by Settings windows)
        private Action<string>? _pendingMidiLearn;
        private DateTime _lastLearnUtc = DateTime.MinValue;

        // debounce for MIDI actions/pads (prevents double fire)
        private DateTime _lastMidiActionUtc = DateTime.MinValue;
        private readonly Dictionary<int, DateTime> _lastMidiPadUtc = new();

        // NEW: debounce mouse clicks per pad (prevents rapid click storms toggling state)
        private readonly Dictionary<int, DateTime> _lastMousePadUtc = new();

        // NEW: tiny action lockout per pad to avoid race with PlaybackStopped / end events
        private readonly Dictionary<int, DateTime> _padActionLockUntilUtc = new();


        // XAML needs to bind to this (and it must update when you reload)
        public GlobalSettings GlobalSettings
        {
            get => _globalSettings;
            private set
            {
                if (ReferenceEquals(_globalSettings, value)) return;
                _globalSettings = value;
                OnPropertyChanged();
            }
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // =====================================================
        // CENTRAL PAD ACTION CONTROLLER (CORE)
        // =====================================================
        private PadActionController _controller = null!;
        private readonly PadKeymap _padKeymap = new PadKeymap();

        // Playhead timers (UI-side progress)
        private readonly Dictionary<int, DispatcherTimer> _playheadTimerByPad = new();
        private readonly Dictionary<int, DateTime> _playStartUtcByPad = new();
        private readonly Dictionary<int, int> _playBaseMsByPad = new();

        // =====================================================
        // HOLD-TO-CLEAR
        // =====================================================
        private const int HoldArmMs = 1000;
        private const int HoldClearMs = 3000;

        private readonly Dictionary<int, CancellationTokenSource> _holdCtsByPadIndex = new();
        private readonly HashSet<int> _suppressNextClick = new();

        // =====================================================
        // KEYBOARD TRIM (latching target + nudge)
        // =====================================================
        private enum ActiveTrimTarget
        {
            None,
            Start,
            End
        }

        private ActiveTrimTarget _activeTrimTarget = ActiveTrimTarget.None;

        // last pad the user interacted with (click or key)
        private int _lastActivatedPadIndex = -1;

        public MainWindow()
        {
            InitializeComponent();
            _audio.PadPlaybackEnded += padIndex =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (DataContext is not MainViewModel vm) return;
                    if (padIndex < 1 || padIndex > vm.Pads.Count) return;

                    var pad = vm.Pads[padIndex - 1];

                    // Stop UI playhead timer (if any)
                    try { StopPlayhead(pad); } catch { }

                    // Normalize state based on whether a valid clip exists
                    bool hasFile = !string.IsNullOrWhiteSpace(pad.ClipPath) && File.Exists(pad.ClipPath);

                    pad.State = hasFile
                        ? PadState.Loaded
                        : (pad.IsEchoMode ? PadState.Armed : PadState.Empty);

                    // Reset playhead to start leg (optional but usually desired)
                    pad.PlayheadMs = pad.StartMs;

                    UpdatePadLedForCurrentState(pad);
                }), DispatcherPriority.Background);
            };
            var vm = new MainViewModel();
            DataContext = vm;

            GlobalSettings = _settingsService.Load();

            // NEW: profile service (single profiles.json)
            _profiles = new ProfileService(_settingsService);

            // NEW: seed profile 1 from whatever pads exist in settings.json (first-run safety)
            _profiles.EnsureSeedFromCurrentSettings(GlobalSettings);

            // NEW: read active profile index
            ActiveProfileIndex = _profiles.GetActiveProfileIndex();

            // NEW: apply that profile into settings.json + memory (so the rest of your app keeps working)
            _profiles.ApplyProfileToSettings(GlobalSettings, ActiveProfileIndex);

            // Reload global settings after apply (ensures we have what got persisted)
            GlobalSettings = _settingsService.Load();

            // hydrate runtime pad models from persisted settings at startup
            HydratePadsFromSettings(vm);

            _dropWatcher = new DropFolderWatcher(_settingsService);
            _dropWatcher.FileArrived += path =>
            {
                // Must go to UI thread because we’ll update pad models
                Dispatcher.Invoke(() => AssignDroppedFileToNextPad(path));
            };

            _controller = new PadActionController(vm.Pads);
            // NEW: engine-end finalizer (single source of truth when playback actually ends)
            _audio.PadPlaybackEnded += padIndex =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (DataContext is not MainViewModel mvm) return;
                        if (padIndex < 1 || padIndex > mvm.Pads.Count) return;

                        var pad = mvm.Pads[padIndex - 1];

                        // Release busy + stop UI playhead
                        pad.IsBusy = false;
                        StopPlayhead(pad);

                        // Finalize state based on whether clip exists
                        bool hasFile = !string.IsNullOrWhiteSpace(pad.ClipPath) && File.Exists(pad.ClipPath);

                        if (hasFile)
                            pad.State = PadState.Loaded;
                        else
                            pad.State = pad.IsEchoMode ? PadState.Armed : PadState.Empty;

                        // Snap playhead to end if we know it; otherwise keep sane
                        if (pad.EndMs > 0)
                            pad.PlayheadMs = pad.EndMs;
                        else if (pad.ClipDuration > TimeSpan.Zero)
                            pad.PlayheadMs = (int)pad.ClipDuration.TotalMilliseconds;

                        UpdatePadLedForCurrentState(pad);

                        // NEW: small lock so a click landing on the exact end-frame doesn't restart
                        LockPadAction(padIndex, 180);
                    }
                    catch { }
                }), DispatcherPriority.Background);
            };

            _controller.PadCopied += Controller_PadCopied;

            _controller.PlayRequested += async pad =>
            {
                if (pad == null)
                    return;

                // NEW: prevent async re-entry / layering
                if (_startingPads.Contains(pad.Index))
                    return;

                _startingPads.Add(pad.Index);

                try
                {
                    // Validate clip exists
                    if (string.IsNullOrWhiteSpace(pad.ClipPath) || !File.Exists(pad.ClipPath))
                    {
                        pad.IsBusy = false;
                        pad.State = pad.IsEchoMode ? PadState.Armed : PadState.Empty;
                        StopPlayhead(pad);
                        UpdatePadLedForCurrentState(pad);
                        return;
                    }

                    // IMPORTANT:
                    // Do NOT early-return just because pad.State is Playing / IsBusy,
                    // because the controller already set those BEFORE raising PlayRequested.

                    // Ensure UI is consistent (safe even if already set)
                    pad.State = PadState.Playing;
                    pad.IsBusy = true;

                    UpdatePadLedForCurrentState(pad);
                    StartPlayhead(pad);

                    bool isEdit = (DataContext as MainViewModel)?.IsEditMode == true;

                    bool preview =
                        _padSettingsDialogOpen ||
                        (_forcePreviewInEditMode && isEdit) ||
                        pad.PreviewToMonitor;

                    // Play (engine)
                    if (_audio is AudioEngine ae)
                    {
                        var out1 = BuildOut1FromSettings();
                        var out2 = BuildOut2FromSettings();

                        await ae.PlayPadAsync(pad, out1, out2, previewToMonitor: preview);
                    }
                    else
                    {
                        await _audio.PlayPadAsync(
                            pad,
                            mainOutDeviceId: _globalSettings.MainOutDeviceId,
                            monitorOutDeviceId: _globalSettings.MonitorOutDeviceId,
                            previewToMonitor: preview
                        );
                    }

                    // Don’t clear IsBusy here — your end/stop handlers should do that.
                    UpdatePadLedForCurrentState(pad);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[PlayRequested] Failed: " + ex);

                    try { StopPlayhead(pad); } catch { }

                    pad.IsBusy = false;

                    bool hasFile = !string.IsNullOrWhiteSpace(pad.ClipPath) && File.Exists(pad.ClipPath);
                    pad.State = hasFile
                        ? PadState.Loaded
                        : (pad.IsEchoMode ? PadState.Armed : PadState.Empty);

                    UpdatePadLedForCurrentState(pad);
                }
                finally
                {
                    _startingPads.Remove(pad.Index);
                }
            };


            _controller.StopRequested += pad =>
            {
                _audio.StopPad(pad);
                StopPlayhead(pad);
                pad.IsBusy = false;
                LockPadAction(pad.Index, 160);
                pad.State = (!string.IsNullOrWhiteSpace(pad.ClipPath) && File.Exists(pad.ClipPath))
                    ? PadState.Loaded
                    : PadState.Empty;

                // LED -> active or clear depending on file
                UpdatePadLedForCurrentState(pad);
            };

            _controller.CommitFromBufferRequested += async pad =>
            {
                try
                {
                    // If pad already has a clip, ignore (safety)
                    if (!string.IsNullOrWhiteSpace(pad.ClipPath))
                        return;

                    // Choose tap based on pad.InputSource (1 = A, 2 = B)
                    var tap = (pad.InputSource <= 1) ? _tapA : _tapB;

                    // No tap available yet -> do nothing
                    if (tap?.Buffer == null)
                        return;

                    // Output folder for committed clips
                    var baseDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "Echopad",
                        "Captures"
                    );

                    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var fileNoExt = $"Pad{pad.Index:00}_Echo_{stamp}";

                    pad.IsBusy = true;

                    // Write file off the UI thread
                    var result = await Task.Run(() =>
                        _bufferCommit.CommitToWav(tap.Buffer!, baseDir, fileNoExt)
                    );

                    // Assign to pad
                    pad.ClipPath = result.FilePath;
                    pad.ClipDuration = result.Duration;

                    pad.StartMs = 0;
                    pad.EndMs = (int)result.Duration.TotalMilliseconds;
                    pad.PlayheadMs = 0;

                    pad.State = PadState.Loaded;
                    pad.IsBusy = false;

                    // Persist only clip/trim/modes we are allowed to touch
                    var gs = _settingsService.Load();
                    var ps = gs.GetOrCreatePad(pad.Index);

                    ps.ClipPath = pad.ClipPath;
                    ps.StartMs = pad.StartMs;
                    ps.EndMs = pad.EndMs;

                    // NOTE: do NOT wipe MIDI/hotkeys
                    _settingsService.Save(gs);
                    _profiles.SavePadsToProfile(gs, ActiveProfileIndex);
                    GlobalSettings = gs;

                    // Update LED to “active/loaded”
                    UpdatePadLedForCurrentState(pad);
                }
                catch
                {
                    pad.IsBusy = false;
                    // Optional later: show toast/log
                }
            };


            if (vm is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += Vm_PropertyChanged;

            _controller.SetEditMode(vm.IsEditMode);

            Loaded += (_, __) =>
            {
                UpdatePadHostSquare();
                Keyboard.Focus(this);

                // Start watcher after UI is up (and settings are loaded)
                RefreshDropWatcher();

                // MIDI: open ports after settings are loaded and UI is ready
                SetupMidiDevices();

                // NEW: Start input taps (rolling RAM buffer)
                SetupInputTaps();

                // push current pad LED states out on startup
                SyncAllPadLeds();
            };

            Closing += (_, __) =>
            {
                // NEW: stop capture early so the process can exit cleanly
                TearDownInputTaps();
                TearDownMidi();
            };


            Closed += (_, __) =>
            {
                try { _dropWatcher.Stop(); } catch { }
                try { _dropWatcher.Dispose(); } catch { }

                // NEW: Stop input taps
                TearDownInputTaps();

                TearDownMidi();
            };


            SizeChanged += (_, __) => UpdatePadHostSquare();

            PreviewKeyDown += MainWindow_PreviewKeyDown;
            PreviewKeyUp += MainWindow_PreviewKeyUp;
        }
        // NEW: Persist copied clip assignment right when CTRL-paste happens
        private void Controller_PadCopied(PadModel src, PadModel dst)
        {
            try
            {
                var gs = _settingsService.Load();
                var ps = gs.GetOrCreatePad(dst.Index);

                // Save ONLY what we want copied: file + trim
                ps.ClipPath = dst.ClipPath;
                ps.StartMs = dst.StartMs;
                ps.EndMs = dst.EndMs;

               

                _settingsService.Save(gs);
                _profiles.SavePadsToProfile(gs, ActiveProfileIndex);
                GlobalSettings = gs;

                // Optional: refresh LED immediately for target
                UpdatePadLedForCurrentState(dst);
            }
            catch
            {
                // Optional later: toast/log
            }
        }
        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.IsEditMode))
                return;

            if (DataContext is MainViewModel vm)
                _controller.SetEditMode(vm.IsEditMode);
        }

        // =====================================================
        // NEW: Use REAL endpoint settings from GlobalSettings
        // =====================================================
        private OutputEndpointSettings BuildOut1FromSettings()
        {
            // OLD (hardcoded Local) - keep as comment
            // return new OutputEndpointSettings
            // {
            //     Mode = AudioEndpointMode.Local,
            //     LocalDeviceId = _globalSettings.MainOutDeviceId,
            //     Vban = new VbanTxSettings { ... }
            // };

            // NEW: use persisted endpoint
            var o = GlobalSettings.Out1 ?? new OutputEndpointSettings();

            // Safety defaults (so you never null-ref)
            o.Vban ??= new VbanTxSettings
            {
                RemoteIp = "127.0.0.1",
                Port = 6980,
                StreamName = "ECHOPAD_OUT1",
                SampleRate = 48000,
                Channels = 2,
                Float32 = true,
                FrameSamples = 256
            };

            return o;
        }

        private OutputEndpointSettings BuildOut2FromSettings()
        {
            // OLD (hardcoded Local) - keep as comment
            // return new OutputEndpointSettings
            // {
            //     Mode = AudioEndpointMode.Local,
            //     LocalDeviceId = _globalSettings.MonitorOutDeviceId,
            //     Vban = new VbanTxSettings { ... }
            // };

            // NEW: use persisted endpoint
            var o = GlobalSettings.Out2 ?? new OutputEndpointSettings();

            // Safety defaults (so you never null-ref)
            o.Vban ??= new VbanTxSettings
            {
                RemoteIp = "127.0.0.1",
                Port = 6980,
                StreamName = "ECHOPAD_OUT2",
                SampleRate = 48000,
                Channels = 2,
                Float32 = true,
                FrameSamples = 256
            };

            return o;
        }

        // NEW: ignore MIDI input briefly (prevents MIDI OUT LED feedback from triggering actions)
        private DateTime _ignoreMidiUntilUtc = DateTime.MinValue;

        private void SuppressMidiInput(int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            if (until > _ignoreMidiUntilUtc)
                _ignoreMidiUntilUtc = until;
        }
        // NEW: block pad actions for a very short window (prevents click->restart races)
        private bool IsPadActionLocked(int padIndex)
        {
            if (_padActionLockUntilUtc.TryGetValue(padIndex, out var until))
                return DateTime.UtcNow < until;

            return false;
        }

        private void LockPadAction(int padIndex, int ms = 140)
        {
            _padActionLockUntilUtc[padIndex] = DateTime.UtcNow.AddMilliseconds(ms);
        }


        // =====================================================
        // MIDI LEARN ENTRY POINT (called by SettingsWindow)
        // =====================================================
        public void BeginMidiLearn(Action<string> onLearned)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastLearnUtc).TotalMilliseconds < 150)
                return;

            _lastLearnUtc = now;

            _pendingMidiLearn = bind =>
            {
                // Always return on UI thread
                Dispatcher.BeginInvoke(() => onLearned(bind));
            };
        }
        // =====================================================
        // NEW: Live-apply settings while SettingsWindow is open
        // =====================================================
        public void ApplySettingsLive()
        {
            // Reload from disk so we always apply what is actually saved
            GlobalSettings = _settingsService.Load();

            // Re-apply everything that depends on device IDs / folder paths
            RefreshDropWatcher();
            SetupMidiDevices();
            SetupInputTaps();

            // OLD:
            // if (DataContext is MainViewModel mvm)
            //     HydratePadsFromSettings(mvm);

            // Update LEDs / visuals if needed
            SyncAllPadLeds();
        }


        // NEW: called by SettingsWindow live-apply (debounced)



        // =====================================================
        // MIDI SETUP/TEARDOWN
        // =====================================================
        private void SetupMidiDevices()
        {
            TearDownMidi();

            // Always reload latest settings for MIDI device IDs
            GlobalSettings = _settingsService.Load();

            if (string.IsNullOrWhiteSpace(GlobalSettings.MidiInDeviceId))
                return;

            if (!GlobalSettings.MidiInDeviceId.StartsWith("midi-in:", StringComparison.OrdinalIgnoreCase))
                return;

            if (!int.TryParse(GlobalSettings.MidiInDeviceId.Substring(8), out var index))
                return;

            try
            {
                _midiIn = new MidiIn(index);
                _midiIn.MessageReceived += MidiIn_MessageReceived;
                _midiIn.ErrorReceived += (_, __) => { };
                _midiIn.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MIDI] Failed to open MidiIn: " + ex.Message);
                TearDownMidi();
                return;
            }

            if (!string.IsNullOrWhiteSpace(GlobalSettings.MidiOutDeviceId) &&
                GlobalSettings.MidiOutDeviceId.StartsWith("midi-out:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(GlobalSettings.MidiOutDeviceId.Substring(9), out var outIndex))
            {
                try
                {
                    _midiOut = new MidiOut(outIndex);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[MIDI] Failed to open MidiOut: " + ex.Message);
                    _midiOut = null;
                }
            }
        }

        private void TearDownMidi()
        {
            try
            {
                if (_midiIn != null)
                {
                    _midiIn.MessageReceived -= MidiIn_MessageReceived;
                    _midiIn.Stop();
                    _midiIn.Dispose();
                }
            }
            catch { }

            try { _midiOut?.Dispose(); } catch { }

            _midiIn = null;
            _midiOut = null;

            _pendingMidiLearn = null;
        }
        private int GetArmedLedValueForPad(PadModel pad)
        {
            // InputSource: 1 or 2 (you clamp it elsewhere)
            return (pad.InputSource <= 1)
                ? Math.Clamp(GlobalSettings.MidiArmedInput1Value, 0, 127)
                : Math.Clamp(GlobalSettings.MidiArmedInput2Value, 0, 127);
        }
        // =====================================================
        // INPUT TAP SETUP/TEARDOWN (Phase 1)
        // =====================================================
        private void SetupInputTaps()
        {
            TearDownInputTaps();
            GlobalSettings = _settingsService.Load();

            _tapA = GlobalSettings.Input1.Mode == AudioEndpointMode.Vban
                ? new InputTapEngine(GlobalSettings.Input1)
                : new InputTapEngine(GlobalSettings.Input1.LocalDeviceId);

            _tapA.Start(RollingBufferSeconds);

            _tapB = GlobalSettings.Input2.Mode == AudioEndpointMode.Vban
                ? new InputTapEngine(GlobalSettings.Input2)
                : new InputTapEngine(GlobalSettings.Input2.LocalDeviceId);

            _tapB.Start(RollingBufferSeconds);
        }




        // =====================================================
        // PUBLIC: live meter readout for Settings window
        // =====================================================
        public float GetInputRms01(int inputIndex, int windowMs = 120)
        {
            var buf = (inputIndex == 2 ? _tapB : _tapA)?.Buffer;
            if (buf == null) return 0f;

            var rms = buf.GetRmsLastMs(windowMs);
            if (rms < 0f) rms = 0f;
            if (rms > 1f) rms = 1f;
            return rms;
        }

        public float GetInputDb(int inputIndex, int windowMs = 120)
        {
            var buf = (inputIndex == 2 ? _tapB : _tapA)?.Buffer;
            if (buf == null) return -90f;

            return buf.GetDbLastMs(windowMs);
        }
        public float GetInputPeak01(int inputIndex, int windowMs = 120)
        {
            var buf = (inputIndex == 2 ? _tapB : _tapA)?.Buffer;
            if (buf == null) return 0f;

            var peak = buf.GetPeakLastMs(windowMs);
            if (peak < 0f) peak = 0f;
            if (peak > 1f) peak = 1f;
            return peak;
        }

        public float GetInputPeakDb(int inputIndex, int windowMs = 120)
        {
            var buf = (inputIndex == 2 ? _tapB : _tapA)?.Buffer;
            if (buf == null) return -90f;

            return buf.GetPeakDbLastMs(windowMs);
        }


        private void TearDownInputTaps()
        {
            try { _tapA?.Dispose(); } catch { }
            try { _tapB?.Dispose(); } catch { }

            _tapA = null;
            _tapB = null;
        }

        private void MidiIn_MessageReceived(object? sender, MidiInMessageEventArgs e)
        {
            try
            {
                var ev = e.MidiEvent;
                if (ev == null) return;
                // NEW: block feedback-loop MIDI (LED echoes, etc.)
                if (DateTime.UtcNow < _ignoreMidiUntilUtc)
                    return;
                // 1) LEARN MODE (one-shot)
                var learn = _pendingMidiLearn;
                if (learn != null)
                {
                    var learned = BuildMidiLearnBindText(ev);
                    if (string.IsNullOrWhiteSpace(learned))
                        return;

                    string rawHex = BuildRawHexFromMidiEvent(ev);

                    _pendingMidiLearn = null; // clear pending BEFORE invoking

                    learn($"{learned}|RAW:{rawHex}");
                    return;
                }

                // 2) NORMAL MODE: route to actions / pad triggers
                Dispatcher.BeginInvoke(() => HandleMidiEvent(ev));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MIDI] MessageReceived error: " + ex);
            }
        }

        // =====================================================
        // MIDI BIND UTIL
        // =====================================================
        private enum MidiBindKind
        {
            Unknown,
            Note,
            Cc,
            Pc
        }

        private readonly struct MidiBind
        {
            public MidiBindKind Kind { get; }
            public int Channel { get; }   // 1-16 (NAudio is human channels)
            public int Number { get; }    // note number / CC number / patch
            public int MinValue { get; }  // trigger when incoming value >= MinValue

            public MidiBind(MidiBindKind kind, int channel, int number, int minValue)
            {
                Kind = kind;
                Channel = channel;
                Number = number;
                MinValue = minValue;
            }
        }

        private static string? BuildMidiLearnBindText(NAudio.Midi.MidiEvent ev)

        {
            // Stored as HUMAN channel 1..16
            // NOTE:ch:num:min
            // CC:ch:num:min
            // PC:ch:num:min

            switch (ev)
            {
                case NoteOnEvent noteOn:
                    if (noteOn.Velocity <= 0)
                        return null;

                    return $"NOTE:{noteOn.Channel}:{noteOn.NoteNumber}:1";

                case ControlChangeEvent cc:
                    // OLD:
                    // if (cc.ControllerValue != 127) return null;
                    // return $"CC:{cc.Channel}:{(int)cc.Controller}:127";

                    // NEW:
                    // Accept any non-zero CC as "press".
                    // - If it's 127, keep the old behavior.
                    // - Otherwise, set MinValue to the received value (so releases at 0 won't trigger).
                    if (cc.ControllerValue <= 0)
                        return null;

                    int min = cc.ControllerValue;         // learn the actual press value
                    min = Math.Clamp(min, 1, 127);

                    return $"CC:{cc.Channel}:{(int)cc.Controller}:{min}";

                    return $"CC:{cc.Channel}:{(int)cc.Controller}:127";

                case PatchChangeEvent pc:
                    return $"PC:{pc.Channel}:{pc.Patch}:1";

                default:
                    return null;
            }
        }


        private static MidiBind? TryParseMidiBind(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var parts = text.Trim().Split(':');
            if (parts.Length < 3)
                return null;

            var head = parts[0].Trim().ToUpperInvariant();

            if (!int.TryParse(parts[1], out var chStored))
                return null;

            if (!int.TryParse(parts[2], out var num))
                return null;

            int min = 1;
            if (parts.Length >= 4 && int.TryParse(parts[3], out var parsedMin))
                min = parsedMin <= 0 ? 1 : parsedMin;

            // New format: 1..16 (human, matches NAudio)
            // Legacy format: 0..15 (0-based) -> convert to 1..16
            int ch1;
            if (chStored >= 1 && chStored <= 16)
                ch1 = chStored;
            else if (chStored >= 0 && chStored <= 15)
                ch1 = chStored + 1; // legacy -> human
            else
                return null;

            if (head == "NOTE" || head == "NOTEON")
                return new MidiBind(MidiBindKind.Note, ch1, num, min);

            if (head == "CC")
                return new MidiBind(MidiBindKind.Cc, ch1, num, min);

            if (head == "PC")
                return new MidiBind(MidiBindKind.Pc, ch1, num, min);

            return null;
        }

        private static bool DoesEventMatchBind(NAudio.Midi.MidiEvent ev, MidiBind bind)
        {
            switch (bind.Kind)
            {
                case MidiBindKind.Note:
                    if (ev is NoteOnEvent onEv)
                    {
                        return onEv.Channel == bind.Channel &&
                               onEv.NoteNumber == bind.Number &&
                               onEv.Velocity >= bind.MinValue;
                    }
                    return false;

                case MidiBindKind.Cc:
                    if (ev is ControlChangeEvent cc)
                    {
                        return cc.Channel == bind.Channel &&
                               (int)cc.Controller == bind.Number &&
                               cc.ControllerValue >= bind.MinValue;
                    }
                    return false;

                case MidiBindKind.Pc:
                    if (ev is PatchChangeEvent pc)
                    {
                        return pc.Channel == bind.Channel &&
                               pc.Patch == bind.Number;
                    }
                    return false;

                default:
                    return false;
            }
        }
        // =====================================================
        // PROFILE MIDI: press/release detection helpers
        // =====================================================
        private static bool IsSameControl(NAudio.Midi.MidiEvent ev, MidiBind bind)
        {
            // Matches the same "control" (channel + note/cc/pc number), ignoring value/velocity.
            switch (bind.Kind)
            {
                case MidiBindKind.Note:
                    if (ev is NoteEvent ne)
                        return ne.Channel == bind.Channel && ne.NoteNumber == bind.Number;
                    return false;

                case MidiBindKind.Cc:
                    if (ev is ControlChangeEvent cc)
                        return cc.Channel == bind.Channel && (int)cc.Controller == bind.Number;
                    return false;

                case MidiBindKind.Pc:
                    if (ev is PatchChangeEvent pc)
                        return pc.Channel == bind.Channel && pc.Patch == bind.Number;
                    return false;

                default:
                    return false;
            }
        }
        // =====================================================
        // NEW: Persist current settings.json pad map into active profile
        // (profiles.json becomes the real source of truth on profile switches)
        // =====================================================
        public void PersistPadsToActiveProfile()
        {
            try
            {
                var gs = _settingsService.Load();

                // OLD:
                // _profiles.SavePadsToProfile(gs, ActiveProfileIndex);

                // NEW: save pads into the active profile
                _profiles.SavePadsToProfile(gs, ActiveProfileIndex);

                // NEW: If we are editing Profile 1 and lock mode is enabled,
                // then auto-fill the ProfileSwitch slots immediately.
                if (ActiveProfileIndex == 1)
                {
                    bool lockMidi = gs.ProfileSwitch?.PadsMidiSameAsProfile1 == true;
                    bool lockMidiAndHotkeys = gs.ProfileSwitch?.PadsMidiAndHotkeysSameAsProfile1 == true;

                    // If both true in dirty JSON, prefer stronger mode
                    if (lockMidi && lockMidiAndHotkeys)
                        lockMidi = false;

                    if (lockMidi || lockMidiAndHotkeys)
                    {
                        _profiles.SyncProfileSwitchSlotsFromProfile1Pads(
                            gs,
                            includeHotkeys: lockMidiAndHotkeys
                        );

                        // Persist updated ProfileSwitch slot binds
                        _settingsService.Save(gs);

                        // Keep runtime snapshot fresh
                        GlobalSettings = gs;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Profile] PersistPadsToActiveProfile failed: " + ex.Message);
            }
        }


        private static bool IsPress(NAudio.Midi.MidiEvent ev, MidiBind bind)
        {
            // “Press” means value/velocity meets threshold.
            switch (bind.Kind)
            {
                case MidiBindKind.Note:
                    if (ev is NoteOnEvent onEv)
                        return onEv.Channel == bind.Channel &&
                               onEv.NoteNumber == bind.Number &&
                               onEv.Velocity >= bind.MinValue;
                    return false;

                case MidiBindKind.Cc:
                    if (ev is ControlChangeEvent cc)
                        return cc.Channel == bind.Channel &&
                               (int)cc.Controller == bind.Number &&
                               cc.ControllerValue >= bind.MinValue;
                    return false;

                case MidiBindKind.Pc:
                    // Program changes are one-shot presses.
                    if (ev is PatchChangeEvent pc)
                        return pc.Channel == bind.Channel && pc.Patch == bind.Number;
                    return false;

                default:
                    return false;
            }
        }

        private static bool IsRelease(NAudio.Midi.MidiEvent ev, MidiBind bind)
        {
            // Release is best-effort: NOTE velocity 0, CC value 0.
            switch (bind.Kind)
            {
                case MidiBindKind.Note:
                    if (ev is NoteOnEvent onEv)
                        return onEv.Channel == bind.Channel &&
                               onEv.NoteNumber == bind.Number &&
                               onEv.Velocity <= 0;
                    return false;

                case MidiBindKind.Cc:
                    if (ev is ControlChangeEvent cc)
                        return cc.Channel == bind.Channel &&
                               (int)cc.Controller == bind.Number &&
                               cc.ControllerValue <= 0;
                    return false;

                default:
                    return false;
            }
        }

        // =====================================================
        // MIDI ROUTING
        // =====================================================
        private void HandleMidiEvent(NAudio.Midi.MidiEvent ev)
        {
            // NEW: profile switching (modifier + slot)
            if (TryHandleProfileMidiSwitch(ev))
                return;

            // Existing global actions
            if (TryHandleGlobalMidiActions(ev))
                return;

            // Existing pad triggers
            if (TryHandlePadMidiTriggers(ev))
                return;

            // Optional fallback mapping (note 36 -> pad 1)
            if (ev is NoteOnEvent onEv && onEv.Velocity > 0)
            {
                int padIndex = (onEv.NoteNumber - 36) + 1;
                TriggerPadFromMidi(padIndex);
            }
        }

        private bool TryHandleGlobalMidiActions(NAudio.Midi.MidiEvent ev)
        {
            var now = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(_globalSettings.MidiBindToggleEdit))
            {
                var b = TryParseMidiBind(_globalSettings.MidiBindToggleEdit);
                if (b.HasValue && DoesEventMatchBind(ev, b.Value))
                {
                    _lastMidiActionUtc = now;

                    if (DataContext is MainViewModel vm)
                        vm.IsEditMode = !vm.IsEditMode;

                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(_globalSettings.MidiBindOpenSettings))
            {
                var b = TryParseMidiBind(_globalSettings.MidiBindOpenSettings);
                if (b.HasValue && DoesEventMatchBind(ev, b.Value))
                {
                    if ((now - _lastMidiActionUtc).TotalMilliseconds < 600)
                        return true;

                    _lastMidiActionUtc = now;
                    OpenSettingsWindow();
                    return true;
                }
            }

            return false;
        }

        private bool TryHandlePadMidiTriggers(NAudio.Midi.MidiEvent ev)
        {
            if (_globalSettings.Pads == null || _globalSettings.Pads.Count == 0)
                return false;

            foreach (var kv in _globalSettings.Pads)
            {
                var padIndex = kv.Key;
                var ps = kv.Value;
                if (ps == null) continue;

                if (string.IsNullOrWhiteSpace(ps.MidiTriggerDisplay))
                    continue;

                var b = TryParseMidiBind(ps.MidiTriggerDisplay);
                if (!b.HasValue)
                    continue;

                if (DoesEventMatchBind(ev, b.Value))
                {
                    // NEW: block pad triggers while dialogs are open
                    if (!IsPadsInputEnabled)
                        return true; // consume so it doesn't leak

                    TriggerPadFromMidi(padIndex);
                    return true;
                }
            }

            return false;
        }
        // =====================================================
        // PROFILES: MIDI switching (modifier-hold + slot MIDI bind)
        // =====================================================
        private bool TryHandleProfileMidiSwitch(NAudio.Midi.MidiEvent ev)
        {
            var ps = _globalSettings.ProfileSwitch;
            if (ps == null || ps.Slots == null || ps.Slots.Count == 0)
                return false;

            // 1) Update modifier held state (press/release)
            if (!string.IsNullOrWhiteSpace(ps.MidiModifierBind))
            {
                var modBind = TryParseMidiBind(ps.MidiModifierBind);
                if (modBind.HasValue && IsSameControl(ev, modBind.Value))
                {
                    if (IsPress(ev, modBind.Value))
                    {
                        _profileMidiModifierHeld = true;

                        // Safety timeout (in case we never see a release)
                        _profileMidiModifierHeldUntilUtc = DateTime.UtcNow.AddSeconds(3);
                    }
                    else if (IsRelease(ev, modBind.Value))
                    {
                        _profileMidiModifierHeld = false;
                        _profileMidiModifierHeldUntilUtc = DateTime.MinValue;
                    }

                    return true; // consume modifier events
                }
            }

            // Safety timeout fallback
            if (_profileMidiModifierHeld && DateTime.UtcNow > _profileMidiModifierHeldUntilUtc)
                _profileMidiModifierHeld = false;

            if (!_profileMidiModifierHeld)
                return false;

            // 2) While modifier held, slot MIDI bind press => switch profile
            for (int i = 0; i < ps.Slots.Count; i++)
            {
                var slot = ps.Slots[i];
                if (slot == null) continue;

                if (string.IsNullOrWhiteSpace(slot.MidiBind))
                    continue;

                var slotBind = TryParseMidiBind(slot.MidiBind);
                if (!slotBind.HasValue)
                    continue;

                if (IsPress(ev, slotBind.Value))
                {
                    int targetProfile = i + 1;
                    SwitchToProfile(targetProfile);
                    return true;
                }
            }

            return false;
        }


        private void TriggerPadFromMidi(int padIndex)
        {
            if (padIndex < 1 || padIndex > 16)
                return;

            var now = DateTime.UtcNow;
            if (_lastMidiPadUtc.TryGetValue(padIndex, out var last) &&
                (now - last).TotalMilliseconds < 180)
            {
                return;
            }
            _lastMidiPadUtc[padIndex] = now;
            if (!IsPadsInputEnabled)
                return;
            RememberLastActivatedPad(padIndex);
            _controller.ActivatePad(padIndex);
        }

        // =====================================================
        // HYDRATE PADS FROM PERSISTED SETTINGS
        // =====================================================
        // =====================================================
        // HYDRATE PADS FROM PERSISTED SETTINGS
        // (SAFE VERSION – does NOT overwrite runtime Playing state)
        // =====================================================
        private void HydratePadsFromSettings(MainViewModel vm)
        {
            if (vm?.Pads == null)
                return;

            GlobalSettings = _settingsService.Load();

            foreach (var pad in vm.Pads)
            {
                var ps = _globalSettings.GetOrCreatePad(pad.Index);

                // -------------------------------------------------
                // Core persisted values
                // -------------------------------------------------
                pad.ClipPath = ps.ClipPath;
                pad.StartMs = ps.StartMs;
                pad.EndMs = ps.EndMs;
                pad.PadName = ps.PadName;
                pad.InputSource = ps.InputSource <= 0 ? 1 : ps.InputSource;
                pad.PreviewToMonitor = ps.PreviewToMonitor;

                pad.IsDropFolderMode = ps.IsDropFolderMode;
                pad.IsEchoMode = ps.IsEchoMode;

                // Safety: mutual exclusion
                if (pad.IsDropFolderMode && pad.IsEchoMode)
                {
                    pad.IsEchoMode = false;
                    ps.IsEchoMode = false;
                }

                bool hasFile =
                    !string.IsNullOrWhiteSpace(pad.ClipPath) &&
                    File.Exists(pad.ClipPath);

                if (hasFile)
                {
                    pad.ClipDuration = SafeReadDuration(pad.ClipPath);

                    int total = (int)pad.ClipDuration.TotalMilliseconds;

                    if (total > 0)
                    {
                        if (pad.EndMs <= 0 || pad.EndMs > total)
                            pad.EndMs = total;

                        if (pad.StartMs < 0)
                            pad.StartMs = 0;

                        if (pad.StartMs > pad.EndMs)
                            pad.StartMs = pad.EndMs;
                    }

                    // NEVER override Playing
                    if (pad.State != PadState.Playing &&
                        pad.State != PadState.Loaded)
                    {
                        pad.State = PadState.Loaded;
                    }
                }
                else
                {
                    pad.ClipDuration = TimeSpan.Zero;

                    if (pad.State != PadState.Playing)
                    {
                        pad.State = pad.IsEchoMode
                            ? PadState.Armed
                            : PadState.Empty;
                    }
                }

                // Only snap playhead if NOT playing
                if (pad.State != PadState.Playing)
                    pad.PlayheadMs = pad.StartMs;
            }
        }


        private void BtnProfileSquircle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                return;

            e.Handled = true;

            // NEW: if locked, do nothing (SwitchToProfile also gates, this just avoids extra work)
            if (IsProfileSwitchBlocked())
                return;

            int next = ActiveProfileIndex + 1;
            if (next > 16) next = 1;

            SwitchToProfile(next);
        }
        // =====================================================
        // NEW: Global gate - block profile switching while edit windows are open
        // =====================================================
        private bool IsProfileSwitchBlocked()
        {
            // Safety belt: you already guard pad clicks with _padSettingsDialogOpen
            // but profile switching must also respect it.
            if (_padSettingsDialogOpen)
                return true;

            // Primary: window-based global lock (PadSettingsWindow / ProfileManagerWindow)
            if (DataContext is MainViewModel vm && vm.IsProfileSwitchLocked)
                return true;

            return false;
        }

        private void SwitchToProfile(int newIndex)
        {
            if (IsProfileSwitchBlocked())
            {
                Debug.WriteLine("[Profile] Switch blocked: editor window open / lock active.");
                return;
            }

            newIndex = Math.Clamp(newIndex, 1, 16);

            if (newIndex == ActiveProfileIndex)
                return;

            try
            {
                SuppressMidiInput(900);

                // =====================================================
                // 1. HARD STOP EVERYTHING (no layered playback)
                // =====================================================
                if (DataContext is MainViewModel mvm)
                {
                    foreach (var pad in mvm.Pads)
                    {
                        try { _audio.StopPad(pad); } catch { }
                        try { StopPlayhead(pad); } catch { }

                        pad.IsBusy = false;

                        bool hasFile =
                            !string.IsNullOrWhiteSpace(pad.ClipPath) &&
                            File.Exists(pad.ClipPath);

                        pad.State = hasFile
                            ? PadState.Loaded
                            : (pad.IsEchoMode ? PadState.Armed : PadState.Empty);

                        pad.PlayheadMs = pad.StartMs;
                    }
                }

                // =====================================================
                // 2. Save CURRENT profile pads
                // =====================================================
                var currentGs = _settingsService.Load();
                _profiles.SavePadsToProfile(currentGs, ActiveProfileIndex);

                // =====================================================
                // 3. Set new active profile
                // =====================================================
                _profiles.SetActiveProfileIndex(newIndex);
                ActiveProfileIndex = newIndex;

                // =====================================================
                // 4. Apply target profile to settings.json
                // =====================================================
                var gs = _settingsService.Load();
                _profiles.ApplyProfileToSettings(gs, ActiveProfileIndex);

                // =====================================================
                // 5. Apply Profile 1 overlay if enabled
                // =====================================================
                bool lockMidi = gs.ProfileSwitch?.PadsMidiSameAsProfile1 == true;
                bool lockMidiAndHotkeys = gs.ProfileSwitch?.PadsMidiAndHotkeysSameAsProfile1 == true;

                if (lockMidi && lockMidiAndHotkeys)
                    lockMidi = false;

                if (lockMidi || lockMidiAndHotkeys)
                {
                    _profiles.OverlayPadMapFromProfile1(
                        gs,
                        includeHotkeys: lockMidiAndHotkeys
                    );
                }

                _settingsService.Save(gs);

                // =====================================================
                // 6. Reload runtime memory
                // =====================================================
                GlobalSettings = _settingsService.Load();

                if (DataContext is MainViewModel mvm2)
                    HydratePadsFromSettings(mvm2);

                SuppressMidiInput(500);

                // =====================================================
                // 7. Re-sync LEDs
                // =====================================================
                SyncAllPadLeds();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Profile] Switch failed: " + ex.Message);
            }
        }



        private void ApplyProfile1PadMidiLockIfEnabled(GlobalSettings gs)
        {
            var mode = gs.ProfileSwitch?.MidiLinkMode ?? ProfileMidiLinkMode.PerProfile;
            if (mode == ProfileMidiLinkMode.PerProfile)
                return;

            // Load Profile 1 pads from profiles.json via ProfileService
            var profile1Pads = _profiles.GetPadsForProfile(1); // YOU MAY NOT HAVE THIS YET
            if (profile1Pads == null || profile1Pads.Count == 0)
                return;

            gs.Pads ??= new Dictionary<int, PadSettings>();

            foreach (var kv in profile1Pads)
            {
                int padIndex = kv.Key;
                var src = kv.Value;
                if (src == null) continue;

                var dst = gs.GetOrCreatePad(padIndex);

                // Always lock MIDI trigger
                dst.MidiTriggerDisplay = src.MidiTriggerDisplay;

                // Optionally lock hotkey too
                if (mode == ProfileMidiLinkMode.PadsMidiAndHotkeysSameAsProfile1)
                    dst.PadHotkey = src.PadHotkey;
            }
        }

        private void OpenProfileManagerWindow()
        {
            try
            {
                // NEW: create the real VM + Window
                var vm = new Echopad.App.Settings.ProfileManagerViewModel(this, _settingsService);
                var win = new Echopad.App.Settings.ProfileManagerWindow(vm)
                {
                    Owner = this
                };
                using var _padBlock = BeginPadsUiBlock("ProfileManagerWindow");
                using (Echopad.App.Services.UiInputBlocker.Block("ProfileManagerWindow"))
                {
                    win.ShowDialog();
                }

                // Optional: after closing, refresh the squircle number + anything else
                // If your squircle binds to ActiveProfileIndex, this is enough:
                // OnPropertyChanged(nameof(ActiveProfileIndex));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    ex.Message,
                    "Profile manager error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }




        private void BtnProfileSquircle_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                return;

            e.Handled = true;

            OpenProfileManagerWindow();
        }
        private void BtnProfileSquircle_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                return;

            e.Handled = true;

            OpenProfileManagerWindow();
        }


        // =====================================================
        // DROP FOLDER DEFAULT
        // =====================================================
        private static string GetDefaultDropFolder()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(baseDir, "Echopad", "Drop");
        }

        // =====================================================
        // DROP FOLDER WATCHER (FIXED + DEFAULT PATH)
        // =====================================================
        private void RefreshDropWatcher()
        {
            var gs = _settingsService.Load();

            // If enabled but no folder chosen -> create default + persist
            if (gs.DropFolderEnabled && string.IsNullOrWhiteSpace(gs.DropWatchFolder))
            {
                var def = GetDefaultDropFolder();

                try
                {
                    Directory.CreateDirectory(def);
                    gs.DropWatchFolder = def;

                    // Also add to AudioFolders so it shows in the Settings window list
                    gs.AudioFolders ??= new List<string>();
                    if (!gs.AudioFolders.Any(f => string.Equals(f, def, StringComparison.OrdinalIgnoreCase)))
                        gs.AudioFolders.Add(def);

                    _settingsService.Save(gs);
                    
                }
                catch
                {
                    // If we can't create it, disable watching so we don't spam errors
                    gs.DropFolderEnabled = false;
                    gs.DropWatchFolder = null;
                    _settingsService.Save(gs);
                    
                }
            }

            GlobalSettings = gs;

            if (!gs.DropFolderEnabled)
            {
                _dropWatcher.Stop();
                return;
            }

            if (string.IsNullOrWhiteSpace(gs.DropWatchFolder))
            {
                _dropWatcher.Stop();
                return;
            }

            try { Directory.CreateDirectory(gs.DropWatchFolder); }
            catch
            {
                _dropWatcher.Stop();
                return;
            }

            _dropWatcher.Start(gs.DropWatchFolder);
        }

        private void AssignDroppedFileToNextPad(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            if (!File.Exists(filePath))
                return;

            if (DataContext is not MainViewModel vm)
                return;

            var target = vm.Pads
                .Where(p => p.IsDropFolderMode)
                .OrderBy(p => p.Index)
                .FirstOrDefault(p =>
                    p.State == PadState.Empty ||
                    string.IsNullOrWhiteSpace(p.ClipPath) ||
                    !File.Exists(p.ClipPath));

            if (target == null)
                return;

            target.ClipPath = filePath;

            target.ClipDuration = SafeReadDuration(filePath);
            var totalMs = (int)target.ClipDuration.TotalMilliseconds;

            target.StartMs = 0;
            target.EndMs = totalMs > 0 ? totalMs : 0;
            target.PlayheadMs = target.StartMs;
            target.State = PadState.Loaded;

            var gs = _settingsService.Load();
            var ps = gs.GetOrCreatePad(target.Index);

            ps.ClipPath = target.ClipPath;
            ps.StartMs = target.StartMs;
            ps.EndMs = target.EndMs;

            ps.IsDropFolderMode = true;

            _settingsService.Save(gs);
            GlobalSettings = gs;
            _profiles.SavePadsToProfile(gs, ActiveProfileIndex);

            // LED -> active (loaded)
            UpdatePadLedForCurrentState(target);
        }

        private static TimeSpan SafeReadDuration(string path)
        {
            try
            {
                using var r = new AudioFileReader(path);
                return r.TotalTime;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Duration read failed: {ex.Message}");
                return TimeSpan.Zero;
            }
        }

        // =====================================================
        // SETTINGS WINDOWS
        // =====================================================
        private void OpenSettingsWindow()
        {
            if (_settingsDialogOpen)
                return;

            _settingsDialogOpen = true;
            using var _padBlock = BeginPadsUiBlock("SettingsWindow");
            try
            {
                var audioProvider = new Echopad.Audio.AudioDeviceProvider();
                var midiProvider = new Echopad.Midi.MidiDeviceProvider();

                var vm = new SettingsViewModel(_settingsService, audioProvider, midiProvider);
                var win = new SettingsWindow(vm) { Owner = this };
                // NEW: reload and re-assign so WPF converter receives updated settings
                GlobalSettings = _settingsService.Load();
                OnPropertyChanged(nameof(GlobalSettings)); // if MainWindow implements INotifyPropertyChanged
                using (Echopad.App.Services.UiInputBlocker.Block("SettingsWindow"))
                {
                    win.ShowDialog();
                }

                if (DataContext is MainViewModel mvm)
                    HydratePadsFromSettings(mvm);

                RefreshDropWatcher();

                // MIDI device selection may have changed
                SetupMidiDevices();

                // NEW: input devices may have changed
                SetupInputTaps();

                // reload so binds/pads are current
                GlobalSettings = _settingsService.Load();

                // re-sync LEDs after settings change
                SyncAllPadLeds();
            }
            finally
            {
                _settingsDialogOpen = false;
            }
        }

        private void OpenPadSettingsWindow(PadModel pad)
        {
            // Guard: prevent re-entrancy
            if (_padSettingsDialogOpen)
                return;

            _padSettingsDialogOpen = true;
            using var _padBlock = BeginPadsUiBlock("PadSettingsWindow");
            try
            {
                // =====================================================
                // HARD STOP before opening settings (prevents layering)
                // =====================================================
                try { _audio.StopPad(pad); } catch { }
                try { StopPlayhead(pad); } catch { }

                // normalize UI state after stop
                pad.State = (!string.IsNullOrWhiteSpace(pad.ClipPath) && File.Exists(pad.ClipPath))
                    ? PadState.Loaded
                    : (pad.IsEchoMode ? PadState.Armed : PadState.Empty);

                UpdatePadLedForCurrentState(pad);

                // =====================================================
                // Open dialog
                // =====================================================
                var vm = new PadSettingsViewModel(pad, _settingsService);
                var win = new PadSettingsWindow(vm) { Owner = this };

                // IMPORTANT:
                // - OK should set DialogResult=true in the window
                // - Cancel/Close should be false/null
                var ok = win.ShowDialog() == true;

                // Block Echo commit briefly after closing (prevents re-commit on mouse-up)
                _controller.BlockEchoCommit(pad.Index, 650);

                // NEW: swallow the NEXT pad click after dialog closes
                // (this prevents the click that closed the dialog from re-triggering the pad grid)
                _suppressNextClick.Add(pad.Index);

                if (ok)
                {
                    // Persist pad changes into active profile too (your helper already exists)
                    PersistPadsToActiveProfile();
                }

                if (DataContext is MainViewModel mvm)
                    HydratePadsFromSettings(mvm);

                // reload global binds/pad triggers after pad settings change
                GlobalSettings = _settingsService.Load();

                // refresh LED for this pad
                UpdatePadLedForCurrentState(pad);
            }
            finally
            {
                // IMPORTANT: do NOT drop the guard at DispatcherPriority.Input
                // Background/ContextIdle prevents click-through much more reliably.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _padSettingsDialogOpen = false;
                }), DispatcherPriority.Background);
            }
        }







        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsWindow();
        }

        // =====================================================
        // HOTKEY STRING
        // =====================================================
        private static string? BuildHotkeyText(KeyEventArgs e)
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return null;

            var mods = Keyboard.Modifiers;
            var key = (e.Key == Key.System) ? e.SystemKey : e.Key;

            var sb = new StringBuilder();
            if (mods.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
            if (mods.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
            if (mods.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
            if (mods.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");

            sb.Append(key.ToString());
            return sb.ToString();
        }
        // NEW: returns only the key name (no modifiers) for profile-slot style binds like "F1"
        private static string? BuildKeyOnlyText(KeyEventArgs e)
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return null;

            var key = (e.Key == Key.System) ? e.SystemKey : e.Key;
            return key.ToString();
        }

        private static string NormalizeHot(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return s.Replace(" ", "").Trim().ToUpperInvariant();
        }

        private bool TryHandleHotkeys(KeyEventArgs e)
        {
            var hot = BuildHotkeyText(e);
            if (string.IsNullOrWhiteSpace(hot))
                return false;

            // Profile hotkeys first
            if (TryHandleProfileHotkeys(e))
                return true;

            if (!string.IsNullOrWhiteSpace(_globalSettings.HotkeyToggleEdit) &&
                string.Equals(hot, _globalSettings.HotkeyToggleEdit, StringComparison.OrdinalIgnoreCase))
            {
                if (DataContext is MainViewModel vm)
                    vm.IsEditMode = !vm.IsEditMode;

                e.Handled = true;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(_globalSettings.HotkeyOpenSettings) &&
                string.Equals(hot, _globalSettings.HotkeyOpenSettings, StringComparison.OrdinalIgnoreCase))
            {
                OpenSettingsWindow();
                e.Handled = true;
                return true;
            }

            if (_globalSettings.Pads != null)
            {
                foreach (var kv in _globalSettings.Pads)
                {
                    var padIndex = kv.Key;
                    var ps = kv.Value;
                    if (ps == null) continue;

                    if (!string.IsNullOrWhiteSpace(ps.PadHotkey) &&
                        string.Equals(hot, ps.PadHotkey, StringComparison.OrdinalIgnoreCase))
                    {
                        // Block pad hotkeys while dialogs open
                        if (!IsPadsInputEnabled)
                        {
                            e.Handled = true;
                            return true;
                        }

                        RememberLastActivatedPad(padIndex);
                        _controller.ActivatePad(padIndex);
                        e.Handled = true;
                        return true;
                    }
                }
            }

            return false;
        }
        // =====================================================
        // PROFILES: Hotkey switching
        // - Supports full binds: "Ctrl+Shift+F1"
        // - Supports key-only binds: "F1" when HotkeyModifier is held (e.g. "Ctrl+Shift")
        // =====================================================
        private bool TryHandleProfileHotkeys(KeyEventArgs e)
        {
            var ps = _globalSettings.ProfileSwitch;
            if (ps == null || ps.Slots == null || ps.Slots.Count == 0)
                return false;

            var full = NormalizeHot(BuildHotkeyText(e));      // e.g. CTRL+SHIFT+F1
            var keyOnly = NormalizeHot(BuildKeyOnlyText(e));  // e.g. F1

            if (string.IsNullOrWhiteSpace(full) && string.IsNullOrWhiteSpace(keyOnly))
                return false;

            var modNeed = NormalizeHot(ps.HotkeyModifier); // e.g. CTRL+SHIFT
            var modsNow = NormalizeHot(Keyboard.Modifiers switch
            {
                ModifierKeys.None => "",
                _ => (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") +
                     (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") +
                     (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") +
                     (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) ? "Win+" : "")
            });

            // remove trailing '+'
            if (modsNow.EndsWith("+", StringComparison.Ordinal))
                modsNow = modsNow[..^1];

            bool modifierHeld = !string.IsNullOrWhiteSpace(modNeed) && modsNow == modNeed;

            for (int i = 0; i < ps.Slots.Count; i++)
            {
                var slot = ps.Slots[i];
                if (slot == null) continue;

                var bind = NormalizeHot(slot.HotkeyBind);
                if (string.IsNullOrWhiteSpace(bind))
                    continue;

                // If slot bind contains '+' it's a full bind; match full.
                if (bind.Contains("+"))
                {
                    if (bind == full)
                    {
                        SwitchToProfile(i + 1);
                        e.Handled = true;
                        return true;
                    }
                }
                else
                {
                    // key-only bind requires modifierHeld (HotkeyModifier)
                    if (modifierHeld && bind == keyOnly)
                    {
                        SwitchToProfile(i + 1);
                        e.Handled = true;
                        return true;
                    }
                }
            }

            return false;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
                _controller.SetCopyHeld(true);

            if (e.IsRepeat)
                return;

            if (TryHandleProfileHotkey(e))
                return;

            if (TryHandleHotkeys(e))
                return;

            if (_padKeymap.TryGetPadNumber(e.Key, out int padNumber))
            {

                if (!IsPadsInputEnabled)
                {
                    e.Handled = true;
                    return;
                }
                RememberLastActivatedPad(padNumber);
                _controller.ActivatePad(padNumber);
                e.Handled = true;
            }
        }
        private bool TryHandleProfileHotkey(KeyEventArgs e)
        {
            var ps = GlobalSettings.ProfileSwitch;
            if (ps == null)
                return false;

            var hot = BuildHotkeyText(e);
            if (string.IsNullOrWhiteSpace(hot))
                return false;

            // Modifier must match exactly (Ctrl / Ctrl+Shift / etc.)
            if (!string.IsNullOrWhiteSpace(ps.HotkeyModifier))
            {
                var mods = Keyboard.Modifiers.ToString().Replace(", ", "+");
                if (!string.Equals(mods, ps.HotkeyModifier, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            for (int i = 0; i < ps.Slots.Count && i < 16; i++)
            {
                if (string.Equals(ps.Slots[i].HotkeyBind, hot, StringComparison.OrdinalIgnoreCase))
                {
                    SwitchToProfile(i + 1);
                    e.Handled = true;
                    return true;
                }
            }

            return false;
        }

        private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
                _controller.SetCopyHeld(false);
        }

        private void PadButton_Click(object sender, RoutedEventArgs e)
        {
            // DEBUG: prove click actually reaches here
            Debug.WriteLine("[MOUSE] PadButton_Click fired");

            // Guard: if a pad settings dialog is open, ignore any pad clicks (prevents OK/close bleed)
            if (_padSettingsDialogOpen)
            {
                Debug.WriteLine("[MOUSE] BLOCKED: _padSettingsDialogOpen == true");
                return;
            }

            if (sender is not Button btn)
            {
                Debug.WriteLine("[MOUSE] BLOCKED: sender is not Button");
                return;
            }

            if (btn.Tag is not PadModel pad)
            {
                Debug.WriteLine("[MOUSE] BLOCKED: btn.Tag is not PadModel");
                return;
            }

            Debug.WriteLine($"[MOUSE] Pad={pad.Index} State={pad.State} Busy={pad.IsBusy} HasClip={(string.IsNullOrWhiteSpace(pad.ClipPath) ? "NO" : "YES")}");

            // Guard: hold-to-clear suppress
            if (_suppressNextClick.Remove(pad.Index))
            {
                Debug.WriteLine("[MOUSE] BLOCKED: _suppressNextClick removed pad");
                return;
            }

            var now = DateTime.UtcNow;

            if (_lastMousePadUtc.TryGetValue(pad.Index, out var last) &&
                (now - last).TotalMilliseconds < 170)
            {
                Debug.WriteLine("[MOUSE] BLOCKED: mouse debounce 170ms");
                return;
            }
            _lastMousePadUtc[pad.Index] = now;

           // if (IsPadActionLocked(pad.Index))
            //{
              //  Debug.WriteLine("[MOUSE] BLOCKED: IsPadActionLocked == true");
                ///return;
            //}

            Debug.WriteLine("[MOUSE] PASS: calling ActivatePad()");
           //LockPadAction(pad.Index, 160);

            RememberLastActivatedPad(pad.Index);
            _controller.ActivatePad(pad.Index);
        }





        private void PadButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // NEW: block while pad settings already open
            if (_padSettingsDialogOpen)
                return;

            if (sender is not Button btn) return;
            if (btn.Tag is not PadModel pad) return;

            if (DataContext is MainViewModel vm && vm.IsEditMode)
            {
                OpenPadSettingsWindow(pad);
                e.Handled = true;
            }
        }


        private void PadButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not PadModel pad) return;

            StartHoldTimer(pad);
        }

        private void PadButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not PadModel pad) return;

            CancelHoldTimer(pad);
        }

        private void PadButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not PadModel pad) return;

            CancelHoldTimer(pad);
        }

        private void StartHoldTimer(PadModel pad)
        {
            CancelHoldTimer(pad);

            var cts = new CancellationTokenSource();
            _holdCtsByPadIndex[pad.Index] = cts;

            _ = HoldWorkerAsync(pad, cts.Token);
        }

        private void CancelHoldTimer(PadModel pad)
        {
            if (_holdCtsByPadIndex.TryGetValue(pad.Index, out var cts))
            {
                try { cts.Cancel(); } catch { }
                try { cts.Dispose(); } catch { }
                _holdCtsByPadIndex.Remove(pad.Index);
            }

            pad.IsHoldArmed = false;
        }

        private async Task HoldWorkerAsync(PadModel pad, CancellationToken ct)
        {
            try
            {
                await Task.Delay(HoldArmMs, ct);
                pad.IsHoldArmed = true;

                int remaining = Math.Max(0, HoldClearMs - HoldArmMs);
                await Task.Delay(remaining, ct);

                _suppressNextClick.Add(pad.Index);

                _audio.StopPad(pad);
                StopPlayhead(pad);
                pad.PlayheadMs = 0;

                _controller.ClearPad(pad.Index);
                // NEW: runtime clear (immediate)
                pad.PadName = null;
                // IMPORTANT: Do NOT wipe MIDI settings OR modes. Only clear clip/trim.
                var gs = _settingsService.Load();
                var ps = gs.GetOrCreatePad(pad.Index);

                ps.ClipPath = null;
                ps.StartMs = 0;
                ps.EndMs = 0;
                ps.PadName = null;
                // KEEP THESE AS-IS (do not touch):
                // ps.IsEchoMode
                // ps.IsDropFolderMode

                _settingsService.Save(gs);
                _profiles.SavePadsToProfile(gs, ActiveProfileIndex);
                GlobalSettings = gs;

                if (DataContext is MainViewModel mvm)
                    HydratePadsFromSettings(mvm);

                // LED -> clear/off
                UpdatePadLedForCurrentState(pad);
            }
            catch (TaskCanceledException)
            {
                pad.IsHoldArmed = false;
            }
        }

        private void StartPlayhead(PadModel pad)
        {
            StopPlayhead(pad);

            _playBaseMsByPad[pad.Index] = pad.StartMs;
            _playStartUtcByPad[pad.Index] = DateTime.UtcNow;

            pad.PlayheadMs = pad.StartMs;

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };

            timer.Tick += (_, __) =>
            {
                if (pad.State != PadState.Playing)
                {
                    StopPlayhead(pad);

                    // LED -> active when playback ends
                    UpdatePadLedForCurrentState(pad);
                    return;
                }

                var baseMs = _playBaseMsByPad.TryGetValue(pad.Index, out var b) ? b : 0;
                var t0 = _playStartUtcByPad.TryGetValue(pad.Index, out var dt) ? dt : DateTime.UtcNow;

                var elapsedMs = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                var playhead = baseMs + elapsedMs;

                int clipEndMs =
                    pad.EndMs > 0
                        ? pad.EndMs
                        : (int)pad.ClipDuration.TotalMilliseconds;

                if (clipEndMs > 0 && playhead >= clipEndMs)
                {
                    pad.PlayheadMs = clipEndMs;
                    StopPlayhead(pad);
                    return;
                }

                pad.PlayheadMs = playhead;
            };

            _playheadTimerByPad[pad.Index] = timer;
            timer.Start();
        }

        private void StopPlayhead(PadModel pad)
        {
            if (_playheadTimerByPad.TryGetValue(pad.Index, out var timer))
            {
                timer.Stop();
                _playheadTimerByPad.Remove(pad.Index);
            }

            _playStartUtcByPad.Remove(pad.Index);
            _playBaseMsByPad.Remove(pad.Index);
        }

        private void UpdatePadHostSquare()
        {
            if (PadHost == null || PadGutter == null)
                return;

            if (PadGutter.ActualWidth <= 0 || PadGutter.ActualHeight <= 0)
                return;

            var s = Math.Min(PadGutter.ActualWidth, PadGutter.ActualHeight);

            const double safety = 40;
            s = Math.Max(s - safety, 520);

            PadHost.Width = s;
            PadHost.Height = s;
        }

        private void RememberLastActivatedPad(int padIndex)
        {
            if (padIndex < 1 || padIndex > 16) return;
            _lastActivatedPadIndex = padIndex;
        }

        private PadModel? TryGetLastActivatedPad()
        {
            if (_lastActivatedPadIndex < 1)
                return null;

            if (DataContext is not MainViewModel vm)
                return null;

            int i = _lastActivatedPadIndex - 1;
            if (i < 0 || i >= vm.Pads.Count)
                return null;

            return vm.Pads[i];
        }

        private void PersistKeyboardTrim(PadModel pad)
        {
            var gs = _settingsService.Load();
            gs.Pads ??= new Dictionary<int, PadSettings>();

            var ps = gs.GetOrCreatePad(pad.Index);
            ps.StartMs = pad.StartMs;
            ps.EndMs = pad.EndMs;

            _settingsService.Save(gs);
            GlobalSettings = _settingsService.Load();
            _profiles.SavePadsToProfile(gs, ActiveProfileIndex);
        }
    }
}
