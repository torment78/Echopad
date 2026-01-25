using Echopad.App.Services;
using Echopad.Core;
using Echopad.Core.Devices;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading; // NEW

namespace Echopad.App.Settings
{
    public sealed class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly SettingsService _settingsService;

        private readonly IAudioDeviceProvider _audioProvider;
        private readonly IMidiDeviceProvider _midiProvider;

        public GlobalSettings Settings { get; }

        public ObservableCollection<DeviceOption> AudioInputs { get; } = new();
        public ObservableCollection<DeviceOption> AudioOutputs { get; } = new();
        public ObservableCollection<DeviceOption> MidiInputs { get; } = new();
        public ObservableCollection<DeviceOption> MidiOutputs { get; } = new();

        public ObservableCollection<string> AudioFolders { get; } = new();

        // =========================================================
        // NEW: Debounced auto-save
        // =========================================================
        private readonly DispatcherTimer _autoSaveTimer;
        private bool _suppressAutoSave;

        public SettingsViewModel(
            SettingsService settingsService,
            IAudioDeviceProvider audioProvider,
            IMidiDeviceProvider midiProvider)
        {
            _settingsService = settingsService;
            _audioProvider = audioProvider;
            _midiProvider = midiProvider;

            Settings = _settingsService.Load();

            LoadDeviceLists();

            AudioFolders.Clear();
            if (Settings.AudioFolders != null)
            {
                foreach (var f in Settings.AudioFolders)
                    AudioFolders.Add(f);
            }

            // NEW: auto-save timer (debounce)
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _autoSaveTimer.Tick += (_, __) =>
            {
                _autoSaveTimer.Stop();
                if (_suppressAutoSave) return;
                Save(); // uses current VM state
            };
        }

        // =========================================================
        // NEW: helper to request an auto-save
        // =========================================================
        private void RequestAutoSave()
        {
            if (_suppressAutoSave) return;

            // restart debounce timer
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        public string? Input1DeviceId
        {
            get => Settings.Input1DeviceId;
            set
            {
                if (Settings.Input1DeviceId == value) return;
                Settings.Input1DeviceId = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? Input2DeviceId
        {
            get => Settings.Input2DeviceId;
            set
            {
                if (Settings.Input2DeviceId == value) return;
                Settings.Input2DeviceId = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? MainOutDeviceId
        {
            get => Settings.MainOutDeviceId;
            set
            {
                if (Settings.MainOutDeviceId == value) return;
                Settings.MainOutDeviceId = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? MonitorOutDeviceId
        {
            get => Settings.MonitorOutDeviceId;
            set
            {
                if (Settings.MonitorOutDeviceId == value) return;
                Settings.MonitorOutDeviceId = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        // =========================================================
        // Global "ARMED" LED values (Input 1 / Input 2)
        // =========================================================
        public int MidiArmedInput1Value
        {
            get => Settings.MidiArmedInput1Value;
            set
            {
                var v = Clamp7(value);
                if (Settings.MidiArmedInput1Value == v) return;
                Settings.MidiArmedInput1Value = v;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public int MidiArmedInput2Value
        {
            get => Settings.MidiArmedInput2Value;
            set
            {
                var v = Clamp7(value);
                if (Settings.MidiArmedInput2Value == v) return;
                Settings.MidiArmedInput2Value = v;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        // =========================================================
        // Drop Folder (global listening folder)
        // =========================================================
        public bool DropFolderEnabled
        {
            get => Settings.DropFolderEnabled;
            set
            {
                if (Settings.DropFolderEnabled == value) return;
                Settings.DropFolderEnabled = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? DropWatchFolder
        {
            get => Settings.DropWatchFolder;
            set
            {
                if (Settings.DropWatchFolder == value) return;
                Settings.DropWatchFolder = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        // helpers used by SettingsWindow code-behind
        public void SetDropFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            // Avoid saving twice in the middle of batch updates
            _suppressAutoSave = true; // NEW

            DropWatchFolder = path;
            DropFolderEnabled = true;

            // ensure it's visible in the bottom list
            AddFolder(path);

            _suppressAutoSave = false; // NEW
            RequestAutoSave();         // NEW
        }

        public void ClearDropFolder()
        {
            _suppressAutoSave = true; // NEW

            DropWatchFolder = null;
            DropFolderEnabled = false;

            _suppressAutoSave = false; // NEW
            RequestAutoSave();         // NEW
        }

        public string? MidiInDeviceId
        {
            get => Settings.MidiInDeviceId;
            set
            {
                if (Settings.MidiInDeviceId == value) return;
                Settings.MidiInDeviceId = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? MidiOutDeviceId
        {
            get => Settings.MidiOutDeviceId;
            set
            {
                if (Settings.MidiOutDeviceId == value) return;
                Settings.MidiOutDeviceId = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? HotkeyToggleEdit
        {
            get => Settings.HotkeyToggleEdit;
            set
            {
                if (Settings.HotkeyToggleEdit == value) return;
                Settings.HotkeyToggleEdit = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? HotkeyOpenSettings
        {
            get => Settings.HotkeyOpenSettings;
            set
            {
                if (Settings.HotkeyOpenSettings == value) return;
                Settings.HotkeyOpenSettings = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? MidiBindToggleEdit
        {
            get => Settings.MidiBindToggleEdit;
            set
            {
                if (Settings.MidiBindToggleEdit == value) return;
                Settings.MidiBindToggleEdit = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? MidiBindOpenSettings
        {
            get => Settings.MidiBindOpenSettings;
            set
            {
                if (Settings.MidiBindOpenSettings == value) return;
                Settings.MidiBindOpenSettings = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? HotkeyTrimSelectIn
        {
            get => Settings.HotkeyTrimSelectIn;
            set
            {
                if (Settings.HotkeyTrimSelectIn == value) return;
                Settings.HotkeyTrimSelectIn = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? HotkeyTrimSelectOut
        {
            get => Settings.HotkeyTrimSelectOut;
            set
            {
                if (Settings.HotkeyTrimSelectOut == value) return;
                Settings.HotkeyTrimSelectOut = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? HotkeyTrimNudgePlus
        {
            get => Settings.HotkeyTrimNudgePlus;
            set
            {
                if (Settings.HotkeyTrimNudgePlus == value) return;
                Settings.HotkeyTrimNudgePlus = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? HotkeyTrimNudgeMinus
        {
            get => Settings.HotkeyTrimNudgeMinus;
            set
            {
                if (Settings.HotkeyTrimNudgeMinus == value) return;
                Settings.HotkeyTrimNudgeMinus = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? MidiBindTrimSelectIn
        {
            get => Settings.MidiBindTrimSelectIn;
            set
            {
                if (Settings.MidiBindTrimSelectIn == value) return;
                Settings.MidiBindTrimSelectIn = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? MidiBindTrimSelectOut
        {
            get => Settings.MidiBindTrimSelectOut;
            set
            {
                if (Settings.MidiBindTrimSelectOut == value) return;
                Settings.MidiBindTrimSelectOut = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? MidiBindTrimNudgePlus
        {
            get => Settings.MidiBindTrimNudgePlus;
            set
            {
                if (Settings.MidiBindTrimNudgePlus == value) return;
                Settings.MidiBindTrimNudgePlus = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public string? MidiBindTrimNudgeMinus
        {
            get => Settings.MidiBindTrimNudgeMinus;
            set
            {
                if (Settings.MidiBindTrimNudgeMinus == value) return;
                Settings.MidiBindTrimNudgeMinus = value;
                OnPropertyChanged();
                RequestAutoSave(); // NEW
            }
        }

        public void AddFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            foreach (var existing in AudioFolders)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            AudioFolders.Add(path);
            RequestAutoSave(); // NEW
        }

        public void RemoveFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            for (int i = AudioFolders.Count - 1; i >= 0; i--)
            {
                if (string.Equals(AudioFolders[i], path, StringComparison.OrdinalIgnoreCase))
                    AudioFolders.RemoveAt(i);
            }

            RequestAutoSave(); // NEW
        }

        public void Save()
        {
            // IMPORTANT: keep this “atomic” so autosave writes a consistent JSON
            Settings.AudioFolders = new System.Collections.Generic.List<string>(AudioFolders);
            _settingsService.Save(Settings);
        }

        private void LoadDeviceLists()
        {
            AudioInputs.Clear();
            AudioOutputs.Clear();
            MidiInputs.Clear();
            MidiOutputs.Clear();

            var inCount = 0;
            foreach (var d in _audioProvider.GetInputDevices())
            {
                inCount++;
                AudioInputs.Add(new DeviceOption(d.Id, d.Name));
            }

            var outCount = 0;
            foreach (var d in _audioProvider.GetOutputDevices())
            {
                outCount++;
                AudioOutputs.Add(new DeviceOption(d.Id, d.Name));
            }

            var midiInCount = 0;
            foreach (var d in _midiProvider.GetMidiInputs())
            {
                midiInCount++;
                MidiInputs.Add(new DeviceOption(d.Id, d.Name));
            }

            var midiOutCount = 0;
            foreach (var d in _midiProvider.GetMidiOutputs())
            {
                midiOutCount++;
                MidiOutputs.Add(new DeviceOption(d.Id, d.Name));
            }

            System.Diagnostics.Debug.WriteLine(
                $"[SettingsViewModel] Devices: AudioIn={inCount} AudioOut={outCount} MidiIn={midiInCount} MidiOut={midiOutCount}");
        }

        private static int Clamp7(int v) => Math.Clamp(v, 0, 127);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class DeviceOption
    {
        public string Id { get; }
        public string Name { get; }

        public DeviceOption(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }
}
