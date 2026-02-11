using Echopad.App.Services;
using Echopad.Core;
using NAudio.Wave;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Echopad.App.Settings
{
    public sealed class PadSettingsViewModel : INotifyPropertyChanged
    {
        private readonly PadModel _pad;
        private readonly SettingsService _settings;
        private GlobalSettings _global;
        // Expose monitor output device for preview playback (Out2)
        public string? MonitorOutDeviceId => _global.MonitorOutDeviceId;

        public GlobalSettings GlobalSettings => _global;
        private readonly PadSettings _padSettings;
        public int ClipDurationMs => (int)_pad.ClipDuration.TotalMilliseconds;
        public PadSettingsViewModel(PadModel pad, SettingsService settings)
        {
            _pad = pad;
            _settings = settings;

            _global = _settings.Load();
            _padSettings = _global.GetOrCreatePad(_pad.Index);
            ApplyFromSettings(_padSettings);

            // Prefer runtime trim if pad already loaded
            if (!string.IsNullOrWhiteSpace(_pad.ClipPath))
            {
                ClipPath = _pad.ClipPath;

                if (_pad.StartMs != _padSettings.StartMs || _pad.EndMs != _padSettings.EndMs)

                {
                    StartMs = _pad.StartMs;
                    EndMs = _pad.EndMs;
                }

                if (_pad.ClipDuration > TimeSpan.Zero)
                    DurationText = FormatDuration(_pad.ClipDuration);
                else if (File.Exists(ClipPath))
                {
                    _pad.ClipDuration = ReadDuration(ClipPath);
                    DurationText = FormatDuration(_pad.ClipDuration);
                }

                Clamp();
            }

            
            BuildPadColorChoices();
            PreviewPlayButtonText = "Play";

        }

        // =====================================================
        // AUDIO FILE
        // =====================================================
        private string? _clipPath;
        public string? ClipPath
        {
            get => _clipPath;
            set
            {
                if (_clipPath == value) return;
                _clipPath = value;
                OnPropertyChanged();
            }
        }

        private string _durationText = "Length: (unknown)";
        public string DurationText
        {
            get => _durationText;
            private set
            {
                if (_durationText == value) return;
                _durationText = value;
                OnPropertyChanged();
            }
        }

        // =====================================================
        // TRIM (ABSOLUTE MS)
        // =====================================================
        private int _startMs;
        private int _endMs;
        public int StartMs
        {
            get => _startMs;
            set
            {
                if (_startMs == value) return;
                _startMs = value;
                Clamp();
                OnPropertyChanged();
            }
        }

        public int EndMs
        {
            get => _endMs;
            set
            {
                if (_endMs == value) return;
                _endMs = value;
                Clamp();
                OnPropertyChanged();
            }
        }



        public int TrimStepMs { get; set; } = 10;

        // =====================================================
        // INPUT ROUTING
        // =====================================================
        private int _inputSource = 1;
        public int InputSource
        {
            get => _inputSource;
            set
            {
                value = value < 1 ? 1 : (value > 2 ? 2 : value);
                if (_inputSource == value) return;
                _inputSource = value;
                OnPropertyChanged();
            }
        }

        private bool _previewToMonitor;
        public bool PreviewToMonitor
        {
            get => _previewToMonitor;
            set
            {
                if (_previewToMonitor == value) return;
                _previewToMonitor = value;
                OnPropertyChanged();
            }
        }

        // =====================================================
        // INPUT BINDINGS
        // =====================================================
        private string? _padHotkey;
        public string? PadHotkey
        {
            get => _padHotkey;
            set
            {
                if (_padHotkey == value) return;
                _padHotkey = value;
                OnPropertyChanged();
            }
        }

        public string? MidiTriggerRaw
        {
            get => _padSettings.MidiTriggerDisplay;
            set
            {
                if (_padSettings.MidiTriggerDisplay == value)
                    return;

                _padSettings.MidiTriggerDisplay = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MidiTriggerDisplay));
            }
        }

        
        // READ-ONLY formatted display for UI
        public string MidiTriggerDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_padSettings.MidiTriggerDisplay))
                    return "";

                var parts = _padSettings.MidiTriggerDisplay.Split("|RAW:", StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 2)
                    return $"{parts[0]}  [{parts[1]}]";

                return parts[0];
            }
        }



        // This is referenced by your PadSettingsWindow XAML (Listening... trigger).
        private bool _isMidiLearning;
        public bool IsMidiLearning
        {
            get => _isMidiLearning;
            set
            {
                if (_isMidiLearning == value) return;
                _isMidiLearning = value;
                OnPropertyChanged();
            }
        }

        // =====================================================
        // MIDI LED SETTINGS (NUMERIC 0–127)
        // =====================================================
        private bool _midiLedActiveEnabled;
        public bool MidiLedActiveEnabled
        {
            get => _midiLedActiveEnabled;
            set
            {
                if (_midiLedActiveEnabled == value) return;
                _midiLedActiveEnabled = value;
                OnPropertyChanged();
            }
        }

        private int _midiLedActiveValue = 25;
        public int MidiLedActiveValue
        {
            get => _midiLedActiveValue;
            set
            {
                value = Clamp7(value);
                if (_midiLedActiveValue == value) return;
                _midiLedActiveValue = value;
                OnPropertyChanged();
            }
        }
        public string MidiLedActiveEntry
        {
            get => !string.IsNullOrWhiteSpace(_padSettings.MidiLedActiveRaw)
                ? _padSettings.MidiLedActiveRaw!
                : _midiLedActiveValue.ToString();
            set => ApplyLedEntry(value, kind: "Active");
        }
        private bool _midiLedRunningEnabled;
        public bool MidiLedRunningEnabled
        {
            get => _midiLedRunningEnabled;
            set
            {
                if (_midiLedRunningEnabled == value) return;
                _midiLedRunningEnabled = value;
                OnPropertyChanged();
            }
        }

        private int _midiLedRunningValue = 127;
        public int MidiLedRunningValue
        {
            get => _midiLedRunningValue;
            set
            {
                value = Clamp7(value);
                if (_midiLedRunningValue == value) return;
                _midiLedRunningValue = value;
                OnPropertyChanged();
            }
        }
        public string MidiLedRunningEntry
        {
            get => !string.IsNullOrWhiteSpace(_padSettings.MidiLedRunningRaw)
                ? _padSettings.MidiLedRunningRaw!
                : _midiLedRunningValue.ToString();
            set => ApplyLedEntry(value, kind: "Running");
        }
        public string MidiLedClearEntry
        {
            get => !string.IsNullOrWhiteSpace(_padSettings.MidiLedClearRaw)
                ? _padSettings.MidiLedClearRaw!
                : _midiLedClearValue.ToString();
            set => ApplyLedEntry(value, kind: "Clear");
        }

        private bool _midiLedClearEnabled;
        public bool MidiLedClearEnabled
        {
            get => _midiLedClearEnabled;
            set
            {
                if (_midiLedClearEnabled == value) return;
                _midiLedClearEnabled = value;
                OnPropertyChanged();
            }
        }

        private int _midiLedClearValue = 0;
        public int MidiLedClearValue
        {
            get => _midiLedClearValue;
            set
            {
                value = Clamp7(value);
                if (_midiLedClearValue == value) return;
                _midiLedClearValue = value;
                OnPropertyChanged();
            }
        }

        // =====================================================
        // MODES
        // =====================================================
        private bool _isEchoMode;
        public bool IsEchoMode
        {
            get => _isEchoMode;
            set
            {
                if (_isEchoMode == value) return;
                _isEchoMode = value;

                // NEW: mutual exclusion (Echo wins)
                if (_isEchoMode && _isDropFolderMode)
                {
                    // turn Drop off if Echo enabled
                    _isDropFolderMode = false;
                    OnPropertyChanged(nameof(IsDropFolderMode));
                }

                OnPropertyChanged();
            }
        }

        private bool _isDropFolderMode;
        public bool IsDropFolderMode
        {
            get => _isDropFolderMode;
            set
            {
                if (_isDropFolderMode == value) return;
                _isDropFolderMode = value;

                // NEW: mutual exclusion (Drop wins)
                if (_isDropFolderMode && _isEchoMode)
                {
                    _isEchoMode = false;
                    OnPropertyChanged(nameof(IsEchoMode));
                }

                OnPropertyChanged();
            }
        }


        // =====================================================
        // PER-PAD UI COLORS (HEX)  ✅ (Added)
        // =====================================================
        private string? _uiActiveHex;
        public string? UiActiveHex
        {
            get => _uiActiveHex;
            set
            {
                var v = NormalizeHex(value);
                if (_uiActiveHex == v) return;
                _uiActiveHex = v;
                OnPropertyChanged();
            }
        }

        private string? _uiRunningHex;
        public string? UiRunningHex
        {
            get => _uiRunningHex;
            set
            {
                var v = NormalizeHex(value);
                if (_uiRunningHex == v) return;
                _uiRunningHex = v;
                OnPropertyChanged();
            }
        }

        // This exists only so your current ComboBox-based XAML compiles.
        // We’ll delete this later when we switch to proper color picker UI.
        public ObservableCollection<PadColorChoice> PadColorChoices { get; } = new();

        // =====================================================
        // ACTIONS
        // =====================================================
        public void SetClipFromFile(string path)
        {
            ClipPath = path;

            _pad.ClipDuration = ReadDuration(path);
            DurationText = FormatDuration(_pad.ClipDuration);

            // NEW:
            OnPropertyChanged(nameof(ClipDurationMs));

            var total = (int)_pad.ClipDuration.TotalMilliseconds;
            StartMs = 0;
            EndMs = total;

            Clamp();
            ApplyToPadModel();
        }

        // =====================================================
        // PREVIEW UI
        // =====================================================
        private string _previewPlayButtonText = "Play";
        public string PreviewPlayButtonText
        {
            get => _previewPlayButtonText;
            set
            {
                if (_previewPlayButtonText == value) return;
                _previewPlayButtonText = value;
                OnPropertyChanged();
            }
        }

        private int _previewPlayheadMs;
        public int PreviewPlayheadMs
        {
            get => _previewPlayheadMs;
            set
            {
                if (_previewPlayheadMs == value) return;
                _previewPlayheadMs = value;
                OnPropertyChanged();
            }
        }
        public void ResetTrim()
        {
            StartMs = 0;
            EndMs = (int)_pad.ClipDuration.TotalMilliseconds;
            Clamp();
            ApplyToPadModel();
        }

        public void NudgeStart(int delta)
        {
            StartMs += delta;
            Clamp();
            ApplyToPadModel();
        }

        public void NudgeEnd(int delta)
        {
            EndMs += delta;
            Clamp();
            ApplyToPadModel();
        }

        public void Save()
        {
            ApplyToPadModel();

            var ps = _global.GetOrCreatePad(_pad.Index);

            ps.ClipPath = ClipPath;
            ps.StartMs = StartMs;
            ps.EndMs = EndMs;

            ps.InputSource = InputSource;
            ps.PreviewToMonitor = PreviewToMonitor;

            ps.PadHotkey = PadHotkey;
            ps.MidiTriggerDisplay = MidiTriggerRaw;
            ps.MidiLedActiveEnabled = MidiLedActiveEnabled;
            ps.MidiLedActiveValue = MidiLedActiveValue;
            ps.MidiLedRunningEnabled = MidiLedRunningEnabled;
            ps.MidiLedRunningValue = MidiLedRunningValue;
            ps.MidiLedClearEnabled = MidiLedClearEnabled;
            ps.MidiLedClearValue = MidiLedClearValue;
            // NEW: persist raw overrides too (if you want them saved)
            ps.MidiLedActiveRaw = _padSettings.MidiLedActiveRaw;
            ps.MidiLedRunningRaw = _padSettings.MidiLedRunningRaw;
            ps.MidiLedClearRaw = _padSettings.MidiLedClearRaw;
            // ps.MidiLedArmedRaw = _padSettings.MidiLedArmedRaw; // when you implement armed entry

            ps.IsEchoMode = IsEchoMode;
            ps.IsDropFolderMode = IsDropFolderMode;

            // ✅ per-pad color overrides
            ps.UiActiveHex = UiActiveHex;
            ps.UiRunningHex = UiRunningHex;

            _settings.Save(_global);
        }

        // =====================================================
        // INTERNAL
        // =====================================================
        private void ApplyFromSettings(PadSettings ps)
        {
            ClipPath = ps.ClipPath;
            StartMs = ps.StartMs;
            EndMs = ps.EndMs;

            InputSource = ps.InputSource;
            PreviewToMonitor = ps.PreviewToMonitor;

            PadHotkey = ps.PadHotkey;
            MidiTriggerRaw = ps.MidiTriggerDisplay;

            MidiLedActiveEnabled = ps.MidiLedActiveEnabled;
            MidiLedActiveValue = ps.MidiLedActiveValue;
            MidiLedRunningEnabled = ps.MidiLedRunningEnabled;
            MidiLedRunningValue = ps.MidiLedRunningValue;
            MidiLedClearEnabled = ps.MidiLedClearEnabled;
            MidiLedClearValue = ps.MidiLedClearValue;

            IsEchoMode = ps.IsEchoMode;
            IsDropFolderMode = ps.IsDropFolderMode;

            // ✅ per-pad color overrides
            UiActiveHex = ps.UiActiveHex;
            UiRunningHex = ps.UiRunningHex;
        }

        private void ApplyToPadModel()
        {
            _pad.ClipPath = ClipPath;
            _pad.StartMs = StartMs;
            _pad.EndMs = EndMs;
            _pad.InputSource = InputSource;
            _pad.PreviewToMonitor = PreviewToMonitor;
            _pad.IsEchoMode = IsEchoMode;
            _pad.IsDropFolderMode = IsDropFolderMode;

            // Keep pad state sane. (Per pad, runtime)
            if (string.IsNullOrWhiteSpace(ClipPath))
                _pad.State = IsEchoMode ? PadState.Armed : PadState.Empty;
            else
                _pad.State = PadState.Loaded;
        }

        // NEW
        private bool _isClamping;

        private void Clamp()
        {
            if (_isClamping) return;
            _isClamping = true;
            try
            {
                var total = (int)_pad.ClipDuration.TotalMilliseconds;
                if (total <= 0) return;

                // clamp using fields (NOT properties)
                var newStart = Math.Clamp(_startMs, 0, total);
                var newEnd = Math.Clamp(_endMs <= 0 ? total : _endMs, 0, total);

                if (newStart > newEnd)
                    newStart = newEnd;

                bool startChanged = newStart != _startMs;
                bool endChanged = newEnd != _endMs;

                _startMs = newStart;
                _endMs = newEnd;

                // Raise after fields updated
                if (startChanged) OnPropertyChanged(nameof(StartMs));
                if (endChanged) OnPropertyChanged(nameof(EndMs));
            }
            finally
            {
                _isClamping = false;
            }
        }

        private static int Clamp7(int v) => Math.Clamp(v, 0, 127);
        private void ApplyLedEntry(string? text, string kind)
        {
            text ??= "";
            text = text.Trim();

            // blank -> clear raw override (keep numeric)
            if (text.Length == 0)
            {
                SetRaw(kind, null);
                OnPropertyChanged(GetEntryName(kind));
                return;
            }

            // If numeric, treat as value and clear raw override
            if (int.TryParse(text, out var n))
            {
                n = Clamp7(n);

                switch (kind)
                {
                    case "Active":
                        MidiLedActiveValue = n;
                        _padSettings.MidiLedActiveRaw = null;
                        OnPropertyChanged(nameof(MidiLedActiveValue));
                        break;

                    case "Running":
                        MidiLedRunningValue = n;
                        _padSettings.MidiLedRunningRaw = null;
                        OnPropertyChanged(nameof(MidiLedRunningValue));
                        break;

                    case "Clear":
                        MidiLedClearValue = n;
                        _padSettings.MidiLedClearRaw = null;
                        OnPropertyChanged(nameof(MidiLedClearValue));
                        break;
                }

                OnPropertyChanged(GetEntryName(kind));
                return;
            }

            // Otherwise treat as RAW (hex/tokens)
            SetRaw(kind, text);
            OnPropertyChanged(GetEntryName(kind));
        }

        private void SetRaw(string kind, string? raw)
        {
            switch (kind)
            {
                case "Active": _padSettings.MidiLedActiveRaw = raw; break;
                case "Running": _padSettings.MidiLedRunningRaw = raw; break;
                case "Clear": _padSettings.MidiLedClearRaw = raw; break;
            }
        }

        private static string GetEntryName(string kind) => kind switch
        {
            "Active" => nameof(MidiLedActiveEntry),
            "Running" => nameof(MidiLedRunningEntry),
            "Clear" => nameof(MidiLedClearEntry),
            _ => ""
        };
        private static TimeSpan ReadDuration(string path)
        {
            try { using var r = new AudioFileReader(path); return r.TotalTime; }
            catch { return TimeSpan.Zero; }
        }

        private static string FormatDuration(TimeSpan t) =>
            t <= TimeSpan.Zero ? "Length: (unknown)" :
            t.TotalHours >= 1 ? $"Length: {t:hh\\:mm\\:ss}" :
            $"Length: {t:mm\\:ss}";

        private static string? NormalizeHex(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();

            if (!value.StartsWith("#"))
                value = "#" + value;

            return Regex.IsMatch(value, "^#([0-9A-Fa-f]{6})$")
                ? value.ToUpperInvariant()
                : null;
        }

        private void BuildPadColorChoices()
        {
            PadColorChoices.Clear();

            // Default entry (so your current dropdown can represent "use global / none")
            PadColorChoices.Add(new PadColorChoice("(Default)", null));

            // A few sane presets (optional)
            PadColorChoices.Add(new PadColorChoice("Green", "#00FF6A"));
            PadColorChoices.Add(new PadColorChoice("Mint", "#3DFF8B"));
            PadColorChoices.Add(new PadColorChoice("Blue", "#4DA3FF"));
            PadColorChoices.Add(new PadColorChoice("Pink", "#FF4DB8"));
            PadColorChoices.Add(new PadColorChoice("Yellow", "#FFD24A"));
            PadColorChoices.Add(new PadColorChoice("Red", "#FF3B3B"));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        

    }


    // Small helper class for the current ComboBox based UI.
    // We will delete this when we switch to a proper color picker UI.
    public sealed class PadColorChoice
    {
        public string Name { get; }
        public string? Hex { get; }

        public PadColorChoice(string name, string? hex)
        {
            Name = name;
            Hex = hex;
        }

        public override string ToString() => Name;
    }
}
