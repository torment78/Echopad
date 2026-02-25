using Echopad.App.Services;
using Echopad.Core;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Echopad.App.Settings
{
    public sealed class ProfileManagerViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly SettingsService _settingsService;

        // We need MainWindow access for MIDI learn
        private readonly MainWindow _main;

        // NEW: access profiles.json so we can read Profile 1 pad binds for seeding
        private readonly ProfileService _profiles;
        private IDisposable? _profileSwitchLock;
        public GlobalSettings Settings { get; private set; }

        public ObservableCollection<ProfileSlotRowViewModel> Slots { get; } = new();

        private string _statusText = "Ready.";
        public string StatusText
        {
            get => _statusText;
            set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); }
        }

        // -------------------------------------------------
        // GLOBAL FIELDS
        // -------------------------------------------------
        private bool _padsFullManual = true; // NEW default = manual unless settings says otherwise
        public bool PadsFullManual
        {
            get => _padsFullManual;
            set
            {
                if (_padsFullManual == value) return;

                _padsFullManual = value;

                if (value)
                {
                    // manual means no linking
                    _padsMidiSameAsProfile1 = false;
                    _padsMidiAndHotkeysSameAsProfile1 = false;
                    OnPropertyChanged(nameof(PadsMidiSameAsProfile1));
                    OnPropertyChanged(nameof(PadsMidiAndHotkeysSameAsProfile1));
                }

                OnPropertyChanged();
                // manual => no seeding, just save
                RequestAutoSave();
            }
        }
        private bool _padsMidiSameAsProfile1;
        public bool PadsMidiSameAsProfile1
        {
            get => _padsMidiSameAsProfile1;
            set
            {
                if (_padsMidiSameAsProfile1 == value) return;

                _padsMidiSameAsProfile1 = value;

                // radio behavior (mutual exclusion)
                if (value)
                {
                    _padsMidiAndHotkeysSameAsProfile1 = false;
                    OnPropertyChanged(nameof(PadsMidiAndHotkeysSameAsProfile1));

                    // NEW:
                    _padsFullManual = false;
                    OnPropertyChanged(nameof(PadsFullManual));
                }

                OnPropertyChanged();

                // NEW: make the UI reflect lock mode
                SyncProfileSlotsFromProfile1PadsIfLocked();
                RequestAutoSave();
            }
        }

        private bool _padsMidiAndHotkeysSameAsProfile1;
        public bool PadsMidiAndHotkeysSameAsProfile1
        {
            get => _padsMidiAndHotkeysSameAsProfile1;
            set
            {
                if (_padsMidiAndHotkeysSameAsProfile1 == value) return;

                _padsMidiAndHotkeysSameAsProfile1 = value;

                // radio behavior (mutual exclusion)
                if (value)
                {
                    _padsMidiSameAsProfile1 = false;
                    OnPropertyChanged(nameof(PadsMidiSameAsProfile1));

                    // NEW:
                    _padsFullManual = false;
                    OnPropertyChanged(nameof(PadsFullManual));
                }

                OnPropertyChanged();

                // NEW: make the UI reflect lock mode
                SyncProfileSlotsFromProfile1PadsIfLocked();
                RequestAutoSave();
            }
        }

        private int _activeProfileIndex = 1;
        public int ActiveProfileIndex
        {
            get => _activeProfileIndex;
            set
            {
                var v = Math.Clamp(value, 1, 16);
                if (_activeProfileIndex == v) return;
                _activeProfileIndex = v;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        private string? _midiModifierBind;
        public string? MidiModifierBind
        {
            get => _midiModifierBind;
            set
            {
                if (_midiModifierBind == value) return;
                _midiModifierBind = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        private string? _hotkeyModifier = "Ctrl+Shift";
        public string? HotkeyModifier
        {
            get => _hotkeyModifier;
            set
            {
                if (_hotkeyModifier == value) return;
                _hotkeyModifier = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        // -------------------------------------------------
        // COMMANDS
        // -------------------------------------------------
        public ICommand SaveCommand { get; }
        public ICommand LearnMidiModifierCommand { get; }

        // simple autosave debounce (cheap)
        private DateTime _lastAutoSaveUtc = DateTime.MinValue;

        // prevents IndexOutOfRange during ctor hydration
        private bool _isInitializing;

        public ProfileManagerViewModel(MainWindow main, SettingsService settingsService)
        {
            _main = main;
            _settingsService = settingsService;


            try
            {
                if (_profileSwitchLock == null && _main.DataContext is MainViewModel mvm)
                    _profileSwitchLock = mvm.AcquireProfileSwitchLock("ProfileManagerWindow");
            }
            catch { }

            // NEW
            _profiles = new ProfileService(_settingsService);

            // NEW
            _profiles = new ProfileService(_settingsService);

            _isInitializing = true;

            Settings = _settingsService.Load();
            Settings.ProfileSwitch ??= new ProfileSwitchSettings();
            Settings.ProfileSwitch.EnsureSlots();

            // hydrate fields (do NOT autosave while initializing)
            _activeProfileIndex = Math.Clamp(Settings.ProfileSwitch.ActiveProfileIndex, 1, 16);
            _midiModifierBind = Settings.ProfileSwitch.MidiModifierBind;

            _hotkeyModifier = string.IsNullOrWhiteSpace(Settings.ProfileSwitch.HotkeyModifier)
                ? "Ctrl+Shift"
                : Settings.ProfileSwitch.HotkeyModifier;

            _padsMidiSameAsProfile1 = Settings.ProfileSwitch.PadsMidiSameAsProfile1;
            _padsMidiAndHotkeysSameAsProfile1 = Settings.ProfileSwitch.PadsMidiAndHotkeysSameAsProfile1;

            // NEW: normalize dirty state (prefer stronger lock if both are true)
            if (_padsMidiSameAsProfile1 && _padsMidiAndHotkeysSameAsProfile1)
                _padsMidiSameAsProfile1 = false; // keep "MIDI+Hotkeys" as winner

            // NEW: if neither lock is enabled, we're in full manual
            _padsFullManual = !_padsMidiSameAsProfile1 && !_padsMidiAndHotkeysSameAsProfile1;

            OnPropertyChanged(nameof(ActiveProfileIndex));
            OnPropertyChanged(nameof(MidiModifierBind));
            OnPropertyChanged(nameof(HotkeyModifier));
            OnPropertyChanged(nameof(PadsMidiSameAsProfile1));
            OnPropertyChanged(nameof(PadsMidiAndHotkeysSameAsProfile1));
            OnPropertyChanged(nameof(PadsFullManual));
            // slots rows
            Slots.Clear();

            for (int i = 1; i <= 16; i++)
            {
                if (Settings.ProfileSwitch.Slots.Count < i)
                    Settings.ProfileSwitch.EnsureSlots();

                var bind = Settings.ProfileSwitch.Slots[i - 1];

                var row = new ProfileSlotRowViewModel(
                    index: i,
                    saveNow: SaveInternal,
                    beginMidiLearn: onLearned =>
                    {
                        _main.BeginMidiLearn(onLearned);
                    },
                    status: s => StatusText = s,
                    isSaveSuppressed: () => _isInitializing
                );

                row.SetInitialValues(
                midiBind: bind.MidiBind,
                hotkeyBind: bind.HotkeyBind,
                name: string.IsNullOrWhiteSpace(bind.Name) ? $"Profile {i:00}" : bind.Name
                );

                Slots.Add(row);
            }

            SaveCommand = new RelayCommand(_ => SaveInternal(), _ => true);

            LearnMidiModifierCommand = new RelayCommand(_ =>
            {
                StatusText = "Listening for MIDI modifier…";
                _main.BeginMidiLearn(bind =>
                {
                    MidiModifierBind = bind;
                    StatusText = $"MIDI modifier set: {bind}";
                    RequestAutoSave();
                });
            }, _ => true);

            // NEW: after UI rows exist, auto-seed from Profile 1 pads if lock is enabled
            SyncProfileSlotsFromProfile1PadsIfLocked();

            _isInitializing = false;


        }

        public void RequestAutoSave()
        {
            if (_isInitializing)
                return;

            var now = DateTime.UtcNow;
            if ((now - _lastAutoSaveUtc).TotalMilliseconds < 200)
                return;

            _lastAutoSaveUtc = now;
            SaveInternal();
        }

        // =========================================================
        // NEW: Seed ProfileSwitch slot binds from Profile 1 pad binds
        // - Only when lock is enabled
        // - Only fills EMPTY slot binds (never overwrites user-set slot binds)
        // - Mapping: Pad 01 -> Slot 01, ... Pad 16 -> Slot 16
        // =========================================================
        private void SyncProfileSlotsFromProfile1PadsIfLocked()
        {
            if (PadsFullManual)
                return;
            if (_isInitializing)
                return;

            bool lockMidi = PadsMidiSameAsProfile1;
            bool lockMidiAndHotkeys = PadsMidiAndHotkeysSameAsProfile1;

            if (lockMidi && lockMidiAndHotkeys)
                lockMidi = false;

            if (!lockMidi && !lockMidiAndHotkeys)
                return;

            try
            {
                var p1Pads = _profiles.GetPadsForProfile(1);
                if (p1Pads == null || p1Pads.Count == 0)
                    return;

                // Fill empty UI rows from Profile 1 pad binds
                for (int i = 1; i <= 16 && i <= Slots.Count; i++)
                {
                    if (!p1Pads.TryGetValue(i, out var ps) || ps == null)
                        continue;

                    var row = Slots[i - 1];

                    // Slot MIDI bind: if empty, use Profile 1 pad MIDI trigger
                    if (string.IsNullOrWhiteSpace(row.MidiBind) &&
                        !string.IsNullOrWhiteSpace(ps.MidiTriggerDisplay))
                    {
                        row.MidiBind = ps.MidiTriggerDisplay;
                    }

                    // Slot Hotkey bind: only if includeHotkeys AND slot hotkey is empty
                    if (lockMidiAndHotkeys &&
                        string.IsNullOrWhiteSpace(row.HotkeyBind) &&
                        !string.IsNullOrWhiteSpace(ps.PadHotkey))
                    {
                        row.HotkeyBind = ps.PadHotkey;
                    }
                }

                StatusText = "Synced slots from Profile 1 pad binds.";
            }
            catch
            {
                // keep silent (no crash)
            }
        }

        private void SaveInternal()
        {
            if (_isInitializing)
                return;

            // reload latest (merge safe)
            var gs = _settingsService.Load();
            gs.ProfileSwitch ??= new ProfileSwitchSettings();
            gs.ProfileSwitch.EnsureSlots();

            gs.ProfileSwitch.ActiveProfileIndex = Math.Clamp(ActiveProfileIndex, 1, 16);
            gs.ProfileSwitch.MidiModifierBind = MidiModifierBind;
            gs.ProfileSwitch.HotkeyModifier = HotkeyModifier;

            if (PadsFullManual)
            {
                gs.ProfileSwitch.PadsMidiSameAsProfile1 = false;
                gs.ProfileSwitch.PadsMidiAndHotkeysSameAsProfile1 = false;
            }
            else
            {
                gs.ProfileSwitch.PadsMidiSameAsProfile1 = PadsMidiSameAsProfile1;
                gs.ProfileSwitch.PadsMidiAndHotkeysSameAsProfile1 = PadsMidiAndHotkeysSameAsProfile1;
            }

            // NEW: keep UI aligned when lock mode is on
            // (fills any empty slot binds before persisting)
            SyncProfileSlotsFromProfile1PadsIfLocked();

            int slotCount = Math.Min(16, gs.ProfileSwitch.Slots.Count);
            int rowCount = Math.Min(16, Slots.Count);
            int count = Math.Min(slotCount, rowCount);

            for (int i = 0; i < count; i++)
            {
                var row = Slots[i];
                var slot = gs.ProfileSwitch.Slots[i];

                slot.MidiBind = row.MidiBind;
                slot.HotkeyBind = row.HotkeyBind;
            }

            _settingsService.Save(gs);

Settings.ProfileSwitch = gs.ProfileSwitch;

// =========================================================
// NEW: Keep local snapshot aligned with what was persisted
// (prevents radio UI getting out of sync)
// =========================================================
_padsMidiSameAsProfile1 = gs.ProfileSwitch.PadsMidiSameAsProfile1;
_padsMidiAndHotkeysSameAsProfile1 = gs.ProfileSwitch.PadsMidiAndHotkeysSameAsProfile1;
_padsFullManual = !_padsMidiSameAsProfile1 && !_padsMidiAndHotkeysSameAsProfile1;

OnPropertyChanged(nameof(PadsMidiSameAsProfile1));
OnPropertyChanged(nameof(PadsMidiAndHotkeysSameAsProfile1));
OnPropertyChanged(nameof(PadsFullManual));

StatusText = "Saved.";
        }

        // =========================================================
        // NEW: Release profile-switch lock
        // =========================================================
        public void Dispose()
        {
            try
            {
                _profileSwitchLock?.Dispose();
                _profileSwitchLock = null;
            }
            catch { }
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public sealed class ProfileSlotRowViewModel : INotifyPropertyChanged
    {
        private readonly Action _saveNow;
        private readonly Action<Action<string>> _beginMidiLearn;
        private readonly Action<string> _setStatus;
        private readonly Func<bool> _isSaveSuppressed;

        public int Index { get; }
        public string IndexText => Index.ToString("00");

        private string? _name;
        public string? Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                OnPropertyChanged();
            }
        }

        private string? _midiBind;
        public string? MidiBind
        {
            get => _midiBind;
            set
            {
                if (_midiBind == value) return;
                _midiBind = value;
                OnPropertyChanged();
                if (!_isSaveSuppressed()) _saveNow();
            }
        }

        private string? _hotkeyBind;
        public string? HotkeyBind
        {
            get => _hotkeyBind;
            set
            {
                if (_hotkeyBind == value) return;
                _hotkeyBind = value;
                OnPropertyChanged();
                if (!_isSaveSuppressed()) _saveNow();
            }
        }

        public ICommand LearnMidiCommand { get; }

        public ProfileSlotRowViewModel(
            int index,
            Action saveNow,
            Action<Action<string>> beginMidiLearn,
            Action<string> status,
            Func<bool> isSaveSuppressed)
        {
            Index = index;
            _saveNow = saveNow;
            _beginMidiLearn = beginMidiLearn;
            _setStatus = status;
            _isSaveSuppressed = isSaveSuppressed;

            LearnMidiCommand = new RelayCommand(_ =>
            {
                _setStatus($"Listening for MIDI… (Slot {Index})");
                _beginMidiLearn(bind =>
                {
                    MidiBind = bind;
                    _setStatus($"MIDI set for Slot {Index}: {bind}");
                });
            }, _ => true);
        }

        public void SetInitialValues(string? midiBind, string? hotkeyBind, string? name)
        {
            _midiBind = midiBind;
            _hotkeyBind = hotkeyBind;
            _name = name;

            OnPropertyChanged(nameof(MidiBind));
            OnPropertyChanged(nameof(HotkeyBind));
            OnPropertyChanged(nameof(Name));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool> _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
