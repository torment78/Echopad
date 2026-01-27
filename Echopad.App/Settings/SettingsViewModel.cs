using Echopad.App.Services;
using Echopad.Core;
using Echopad.Core.Devices;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

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
        // Debounced auto-save
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

            // NEW: ensure legacy <-> endpoint compatibility is always applied
            Settings.EnsureCompatibility();

            LoadDeviceLists();

            AudioFolders.Clear();
            if (Settings.AudioFolders != null)
            {
                foreach (var f in Settings.AudioFolders)
                    AudioFolders.Add(f);
            }

            // auto-save timer (debounce)
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
        // helper to request an auto-save
        // =========================================================
        private void RequestAutoSave()
        {
            if (_suppressAutoSave) return;

            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        // =========================================================
        // Per-channel mode (Local / VBAN)
        // =========================================================
        public AudioEndpointMode Input1Mode
        {
            get => Settings.Input1.Mode;
            set
            {
                if (Settings.Input1.Mode == value) return;
                Settings.Input1.Mode = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public AudioEndpointMode Input2Mode
        {
            get => Settings.Input2.Mode;
            set
            {
                if (Settings.Input2.Mode == value) return;
                Settings.Input2.Mode = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public AudioEndpointMode Out1Mode
        {
            get => Settings.Out1.Mode;
            set
            {
                if (Settings.Out1.Mode == value) return;
                Settings.Out1.Mode = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public AudioEndpointMode Out2Mode
        {
            get => Settings.Out2.Mode;
            set
            {
                if (Settings.Out2.Mode == value) return;
                Settings.Out2.Mode = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        // =========================================================
        // Audio devices (Local mode)
        // Keep legacy fields in sync for older JSON/tools
        // =========================================================
        public string? Input1DeviceId
        {
            get => Settings.Input1.LocalDeviceId;
            set
            {
                if (Settings.Input1.LocalDeviceId == value) return;
                Settings.Input1.LocalDeviceId = value;
                Settings.Input1DeviceId = value; // legacy sync
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public string? Input2DeviceId
        {
            get => Settings.Input2.LocalDeviceId;
            set
            {
                if (Settings.Input2.LocalDeviceId == value) return;
                Settings.Input2.LocalDeviceId = value;
                Settings.Input2DeviceId = value; // legacy sync
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public string? MainOutDeviceId
        {
            get => Settings.Out1.LocalDeviceId;
            set
            {
                if (Settings.Out1.LocalDeviceId == value) return;
                Settings.Out1.LocalDeviceId = value;
                Settings.MainOutDeviceId = value; // legacy sync
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public string? MonitorOutDeviceId
        {
            get => Settings.Out2.LocalDeviceId;
            set
            {
                if (Settings.Out2.LocalDeviceId == value) return;
                Settings.Out2.LocalDeviceId = value;
                Settings.MonitorOutDeviceId = value; // legacy sync
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        // =========================================================
        // VBAN RX fields (Input1 / Input2)
        // Null-safe (treat null as "")
        // =========================================================
        public string Input1VbanIp
        {
            get => Settings.Input1.Vban.RemoteIp ?? "";
            set
            {
                value ??= "";
                if ((Settings.Input1.Vban.RemoteIp ?? "") == value) return;
                Settings.Input1.Vban.RemoteIp = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public int Input1VbanPort
        {
            get => Settings.Input1.Vban.Port;
            set
            {
                if (Settings.Input1.Vban.Port == value) return;
                Settings.Input1.Vban.Port = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public string Input1VbanStream
        {
            get => Settings.Input1.Vban.StreamName ?? "";
            set
            {
                value ??= "";
                if ((Settings.Input1.Vban.StreamName ?? "") == value) return;
                Settings.Input1.Vban.StreamName = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public string Input2VbanIp
        {
            get => Settings.Input2.Vban.RemoteIp ?? "";
            set
            {
                value ??= "";
                if ((Settings.Input2.Vban.RemoteIp ?? "") == value) return;
                Settings.Input2.Vban.RemoteIp = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public int Input2VbanPort
        {
            get => Settings.Input2.Vban.Port;
            set
            {
                if (Settings.Input2.Vban.Port == value) return;
                Settings.Input2.Vban.Port = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public string Input2VbanStream
        {
            get => Settings.Input2.Vban.StreamName ?? "";
            set
            {
                value ??= "";
                if ((Settings.Input2.Vban.StreamName ?? "") == value) return;
                Settings.Input2.Vban.StreamName = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        // =========================================================
        // VBAN TX fields (Out1 / Out2)
        // Null-safe (treat null as "")
        // =========================================================
        public string Out1VbanIp
        {
            get => Settings.Out1.Vban.RemoteIp ?? "";
            set
            {
                value ??= "";
                if ((Settings.Out1.Vban.RemoteIp ?? "") == value) return;
                Settings.Out1.Vban.RemoteIp = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public int Out1VbanPort
        {
            get => Settings.Out1.Vban.Port;
            set
            {
                if (Settings.Out1.Vban.Port == value) return;
                Settings.Out1.Vban.Port = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public string Out1VbanStream
        {
            get => Settings.Out1.Vban.StreamName ?? "";
            set
            {
                value ??= "";
                if ((Settings.Out1.Vban.StreamName ?? "") == value) return;
                Settings.Out1.Vban.StreamName = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public string Out2VbanIp
        {
            get => Settings.Out2.Vban.RemoteIp ?? "";
            set
            {
                value ??= "";
                if ((Settings.Out2.Vban.RemoteIp ?? "") == value) return;
                Settings.Out2.Vban.RemoteIp = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public int Out2VbanPort
        {
            get => Settings.Out2.Vban.Port;
            set
            {
                if (Settings.Out2.Vban.Port == value) return;
                Settings.Out2.Vban.Port = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public string Out2VbanStream
        {
            get => Settings.Out2.Vban.StreamName ?? "";
            set
            {
                value ??= "";
                if ((Settings.Out2.Vban.StreamName ?? "") == value) return;
                Settings.Out2.Vban.StreamName = value;
                OnPropertyChanged();
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
            }
        }

        // helpers used by SettingsWindow code-behind
        public void SetDropFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            _suppressAutoSave = true;

            DropWatchFolder = path;
            DropFolderEnabled = true;

            AddFolder(path);

            _suppressAutoSave = false;
            RequestAutoSave();
        }

        public void ClearDropFolder()
        {
            _suppressAutoSave = true;

            DropWatchFolder = null;
            DropFolderEnabled = false;

            _suppressAutoSave = false;
            RequestAutoSave();
        }

        public string? MidiInDeviceId
        {
            get => Settings.MidiInDeviceId;
            set
            {
                if (Settings.MidiInDeviceId == value) return;
                Settings.MidiInDeviceId = value;
                OnPropertyChanged();
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
                RequestAutoSave();
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
            RequestAutoSave();
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

            RequestAutoSave();
        }

        public void Save()
        {
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
