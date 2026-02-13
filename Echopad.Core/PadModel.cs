using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Echopad.Core
{
    public sealed class PadModel : INotifyPropertyChanged
    {
        public int Index { get; }

        public PadModel(int index) => Index = index;
        public bool HasPadName => !string.IsNullOrWhiteSpace(PadName);
        // ======================================================
        // State
        // ======================================================
        private PadState _state = PadState.Empty;
        public PadState State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                OnPropertyChanged();
            }
        }
        private string? _padName;
        public string? PadName
        {
            get => _padName;
            set
            {
                if (_padName == value) return;
                _padName = value;
                OnPropertyChanged(); // whatever your pad model uses
            }
        }

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

        private TimeSpan _clipDuration = TimeSpan.Zero;
        public TimeSpan ClipDuration
        {
            get => _clipDuration;
            set
            {
                if (_clipDuration == value) return;
                _clipDuration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalSecText));
            }
        }

        // Trim from start (ms)
        private int _startMs;
        public int StartMs
        {
            get => _startMs;
            set
            {
                if (_startMs == value) return;
                _startMs = value;
                OnPropertyChanged();
            }
        }

        // Absolute end position (ms) (your “global end time” model)
        private int _endMs;
        public int EndMs
        {
            get => _endMs;
            set
            {
                if (_endMs == value) return;
                _endMs = value;
                OnPropertyChanged();
            }
        }

        // ======================================================
        // Per-pad routing + preview
        // ======================================================
        private int _inputSource = 1;
        public int InputSource
        {
            get => _inputSource;
            set
            {
                var v = value < 1 ? 1 : (value > 2 ? 2 : value);
                if (_inputSource == v) return;
                _inputSource = v;
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

        // ======================================================
        // NEW: Drop Folder mode (pad is eligible for auto-assign)
        // ======================================================
        private bool _isDropFolderMode;
        public bool IsDropFolderMode
        {
            get => _isDropFolderMode;
            set
            {
                if (_isDropFolderMode == value) return;
                _isDropFolderMode = value;
                OnPropertyChanged();
            }
        }

        // ======================================================
        // Visual state markers used by controller
        // ======================================================
        private ClipMod _clipMod = ClipMod.None;
        public ClipMod ClipMod
        {
            get => _clipMod;
            set
            {
                if (_clipMod == value) return;
                _clipMod = value;
                OnPropertyChanged();
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged();
            }
        }

        private bool _isHoldArmed;
        public bool IsHoldArmed
        {
            get => _isHoldArmed;
            set
            {
                if (_isHoldArmed == value) return;
                _isHoldArmed = value;
                OnPropertyChanged();
            }
        }

        // ======================================================
        // NEW: Echo mode (capture last buffer, etc. - future wiring)
        // ======================================================
        private bool _isEchoMode;
        public bool IsEchoMode
        {
            get => _isEchoMode;
            set
            {
                if (_isEchoMode == value) return;
                _isEchoMode = value;
                OnPropertyChanged();
            }
        }


        // ======================================================
        // Playhead (absolute file timeline, milliseconds)
        // ======================================================
        private int _playheadMs;
        public int PlayheadMs
        {
            get => _playheadMs;
            set
            {
                if (_playheadMs == value) return;
                _playheadMs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayheadSecText));
            }
        }

        // ======================================================
        // Display strings
        // ======================================================
        public string TotalSecText
        {
            get
            {
                if (ClipDuration == TimeSpan.Zero) return "";
                var s = (int)Math.Ceiling(ClipDuration.TotalSeconds);
                return $"{s}s";
            }
        }

        public string PlayheadSecText
        {
            get
            {
                var s = PlayheadMs / 1000.0;
                return $"{s:0.0}s";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
