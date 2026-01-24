using Echopad.App.Services;
using Echopad.Core;
using Echopad.Core.Devices;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
        }

        public string? Input1DeviceId
        {
            get => Settings.Input1DeviceId;
            set { Settings.Input1DeviceId = value; OnPropertyChanged(); }
        }

        public string? Input2DeviceId
        {
            get => Settings.Input2DeviceId;
            set { Settings.Input2DeviceId = value; OnPropertyChanged(); }
        }

        public string? MainOutDeviceId
        {
            get => Settings.MainOutDeviceId;
            set { Settings.MainOutDeviceId = value; OnPropertyChanged(); }
        }

        public string? MonitorOutDeviceId
        {
            get => Settings.MonitorOutDeviceId;
            set { Settings.MonitorOutDeviceId = value; OnPropertyChanged(); }
        }
        // =========================================================
        // UI: Armed colors by input (hex)
        // =========================================================
        public string? UiArmedInput1Hex
        {
            get => Settings.UiArmedInput1Hex;
            set { Settings.UiArmedInput1Hex = value; OnPropertyChanged(); }
        }

        public string? UiArmedInput2Hex
        {
            get => Settings.UiArmedInput2Hex;
            set { Settings.UiArmedInput2Hex = value; OnPropertyChanged(); }
        }

        // =========================================================
        // NEW: Global "ARMED" LED values (Input 1 / Input 2)
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
            }
        }

        // =========================================================
        // Drop Folder (global listening folder)
        // =========================================================

        public bool DropFolderEnabled
        {
            get => Settings.DropFolderEnabled;
            set { Settings.DropFolderEnabled = value; OnPropertyChanged(); }
        }

        public string? DropWatchFolder
        {
            get => Settings.DropWatchFolder;
            set { Settings.DropWatchFolder = value; OnPropertyChanged(); }
        }

        // helpers used by SettingsWindow code-behind
        public void SetDropFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            DropWatchFolder = path;
            DropFolderEnabled = true;

            // ensure it's visible in the bottom list
            AddFolder(path);
        }

        public void ClearDropFolder()
        {
            DropWatchFolder = null;
            DropFolderEnabled = false;
        }

        public string? MidiInDeviceId
        {
            get => Settings.MidiInDeviceId;
            set { Settings.MidiInDeviceId = value; OnPropertyChanged(); }
        }

        public string? MidiOutDeviceId
        {
            get => Settings.MidiOutDeviceId;
            set { Settings.MidiOutDeviceId = value; OnPropertyChanged(); }
        }

        public string? HotkeyToggleEdit
        {
            get => Settings.HotkeyToggleEdit;
            set { Settings.HotkeyToggleEdit = value; OnPropertyChanged(); }
        }

        public string? HotkeyOpenSettings
        {
            get => Settings.HotkeyOpenSettings;
            set { Settings.HotkeyOpenSettings = value; OnPropertyChanged(); }
        }

        public string? MidiBindToggleEdit
        {
            get => Settings.MidiBindToggleEdit;
            set { Settings.MidiBindToggleEdit = value; OnPropertyChanged(); }
        }

        public string? MidiBindOpenSettings
        {
            get => Settings.MidiBindOpenSettings;
            set { Settings.MidiBindOpenSettings = value; OnPropertyChanged(); }
        }

        public string? HotkeyTrimSelectIn
        {
            get => Settings.HotkeyTrimSelectIn;
            set { Settings.HotkeyTrimSelectIn = value; OnPropertyChanged(); }
        }

        public string? HotkeyTrimSelectOut
        {
            get => Settings.HotkeyTrimSelectOut;
            set { Settings.HotkeyTrimSelectOut = value; OnPropertyChanged(); }
        }

        public string? HotkeyTrimNudgePlus
        {
            get => Settings.HotkeyTrimNudgePlus;
            set { Settings.HotkeyTrimNudgePlus = value; OnPropertyChanged(); }
        }

        public string? HotkeyTrimNudgeMinus
        {
            get => Settings.HotkeyTrimNudgeMinus;
            set { Settings.HotkeyTrimNudgeMinus = value; OnPropertyChanged(); }
        }

        public string? MidiBindTrimSelectIn
        {
            get => Settings.MidiBindTrimSelectIn;
            set { Settings.MidiBindTrimSelectIn = value; OnPropertyChanged(); }
        }

        public string? MidiBindTrimSelectOut
        {
            get => Settings.MidiBindTrimSelectOut;
            set { Settings.MidiBindTrimSelectOut = value; OnPropertyChanged(); }
        }

        public string? MidiBindTrimNudgePlus
        {
            get => Settings.MidiBindTrimNudgePlus;
            set { Settings.MidiBindTrimNudgePlus = value; OnPropertyChanged(); }
        }

        public string? MidiBindTrimNudgeMinus
        {
            get => Settings.MidiBindTrimNudgeMinus;
            set { Settings.MidiBindTrimNudgeMinus = value; OnPropertyChanged(); }
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
