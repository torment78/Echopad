using System;
using System.ComponentModel; // NEW
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Echopad.App.Settings
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _vm;
        private System.Windows.Threading.DispatcherTimer? _vuTimer;

        // NEW: debounce live-apply so device re-init doesn't happen 10x per second
        private System.Windows.Threading.DispatcherTimer? _applyTimer;

        public SettingsWindow(SettingsViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = _vm;

            Loaded += (_, __) =>
            {
                StartVuTimer();
                HookLiveApply(); // NEW
            };

            Closed += (_, __) =>
            {
                StopVuTimer();
                UnhookLiveApply(); // NEW
            };
        }

        // =========================================================
        // NEW: Live apply wiring
        // =========================================================
        private void HookLiveApply()
        {
            UnhookLiveApply();

            _applyTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _applyTimer.Tick += ApplyTimer_Tick;

            _vm.PropertyChanged += Vm_PropertyChanged;
        }

        private void ApplyTimer_Tick(object? sender, EventArgs e)
        {
            _applyTimer?.Stop();
            ApplyToOwnerNow();
        }

        private void UnhookLiveApply()
        {
            try
            {
                if (_applyTimer != null)
                {
                    _applyTimer.Stop();
                    _applyTimer.Tick -= ApplyTimer_Tick;
                }
            }
            catch { }

            _applyTimer = null;

            try { _vm.PropertyChanged -= Vm_PropertyChanged; } catch { }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Only live-apply for settings that affect engines/devices/watcher.
            // Everything still autosaves in SettingsViewModel already.
            switch (e.PropertyName)
            {
                // Local device IDs
                case nameof(SettingsViewModel.Input1DeviceId):
                case nameof(SettingsViewModel.Input2DeviceId):
                case nameof(SettingsViewModel.MainOutDeviceId):
                case nameof(SettingsViewModel.MonitorOutDeviceId):

                // NEW: per-channel mode (Local / VBAN)
                case nameof(SettingsViewModel.Input1Mode):
                case nameof(SettingsViewModel.Input2Mode):
                case nameof(SettingsViewModel.Out1Mode):
                case nameof(SettingsViewModel.Out2Mode):

                // NEW: VBAN RX fields (Input1/Input2)
                case nameof(SettingsViewModel.Input1VbanIp):
                case nameof(SettingsViewModel.Input1VbanPort):
                case nameof(SettingsViewModel.Input1VbanStream):

                case nameof(SettingsViewModel.Input2VbanIp):
                case nameof(SettingsViewModel.Input2VbanPort):
                case nameof(SettingsViewModel.Input2VbanStream):

                // NEW: VBAN TX fields (Out1/Out2)
                case nameof(SettingsViewModel.Out1VbanIp):
                case nameof(SettingsViewModel.Out1VbanPort):
                case nameof(SettingsViewModel.Out1VbanStream):

                case nameof(SettingsViewModel.Out2VbanIp):
                case nameof(SettingsViewModel.Out2VbanPort):
                case nameof(SettingsViewModel.Out2VbanStream):

                // MIDI + Drop watcher
                case nameof(SettingsViewModel.MidiInDeviceId):
                case nameof(SettingsViewModel.MidiOutDeviceId):
                case nameof(SettingsViewModel.DropFolderEnabled):
                case nameof(SettingsViewModel.DropWatchFolder):
                    RequestApplyToOwner();
                    break;
            }
        }

        private void RequestApplyToOwner()
        {
            if (_applyTimer == null)
                return;

            _applyTimer.Stop();
            _applyTimer.Start();
        }

        private void ApplyToOwnerNow()
        {
            if (Owner is not MainWindow mw)
                return;

            _vm.Save(); // NEW: save immediately
            mw.ApplySettingsLive();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            EnsureDropFolderDefaultIfEnabled();
            EnsureDropFolderExistsIfEnabled();

            _vm.Save();

            // NEW: ensure final apply before closing (in case last click is still debounced)
            ApplyToOwnerNow();

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void StartVuTimer()
        {
            StopVuTimer();

            _vuTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(80) // ~12.5 fps
            };

            _vuTimer.Tick += (_, __) =>
            {
                if (Owner is not MainWindow mw)
                    return;

                // Input 1
                var p1 = mw.GetInputPeak01(1);
                var db1 = mw.GetInputPeakDb(1);
                if (VuInput1 != null) VuInput1.Value = p1;
                if (VuInput1Db != null) VuInput1Db.Text = db1 <= -89 ? "-inf" : $"{db1:0} dB";

                // Input 2
                var p2 = mw.GetInputPeak01(2);
                var db2 = mw.GetInputPeakDb(2);
                if (VuInput2 != null) VuInput2.Value = p2;
                if (VuInput2Db != null) VuInput2Db.Text = db2 <= -89 ? "-inf" : $"{db2:0} dB";
            };

            _vuTimer.Start();
        }

        private void StopVuTimer()
        {
            try
            {
                if (_vuTimer != null)
                {
                    _vuTimer.Stop();
                    _vuTimer = null;
                }
            }
            catch { }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose an audio folder (import/drop pool)",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder",
                Filter = "Folders|*.none"
            };

            if (dlg.ShowDialog(this) == true)
            {
                var path = Path.GetDirectoryName(dlg.FileName);

                if (!string.IsNullOrWhiteSpace(path))
                    _vm.AddFolder(path);
            }
        }

        private void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (FoldersList.SelectedItem is string path)
            {
                _vm.RemoveFolder(path);
            }
        }

        private void UseSelectedAsDropFolder_Click(object sender, RoutedEventArgs e)
        {
            if (FoldersList.SelectedItem is not string selected || string.IsNullOrWhiteSpace(selected))
            {
                MessageBox.Show(this, "Select a folder from the list first.", "Echopad",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _vm.SetDropFolder(selected);
            EnsureDropFolderExistsIfEnabled();

            // NEW: Apply immediately
            RequestApplyToOwner();
        }

        private void BrowseDropWatchFolder_Click(object sender, RoutedEventArgs e)
        {
            var selected = PickFolder("Choose Drop Folder (watch folder)");
            if (string.IsNullOrWhiteSpace(selected))
                return;

            _vm.SetDropFolder(selected);
            EnsureDropFolderExistsIfEnabled();

            // NEW: Apply immediately
            RequestApplyToOwner();
        }

        private void OpenDropWatchFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = _vm.DropWatchFolder;

            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(this, "No Drop Folder is set yet.", "Echopad",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open folder:\n{ex.Message}", "Echopad",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearDropWatchFolder_Click(object sender, RoutedEventArgs e)
        {
            _vm.ClearDropFolder();

            // NEW: Apply immediately
            RequestApplyToOwner();
        }

        private void CreateDefaultDropWatchFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = GetDefaultDropFolder();

            try
            {
                Directory.CreateDirectory(path);
                _vm.SetDropFolder(path);

                // NEW: Apply immediately
                RequestApplyToOwner();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not create folder:\n{ex.Message}", "Echopad",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnsureDropFolderDefaultIfEnabled()
        {
            if (!_vm.DropFolderEnabled)
                return;

            if (!string.IsNullOrWhiteSpace(_vm.DropWatchFolder))
                return;

            var path = GetDefaultDropFolder();

            try
            {
                Directory.CreateDirectory(path);
                _vm.SetDropFolder(path);
            }
            catch
            {
                _vm.ClearDropFolder();
            }
        }

        private void EnsureDropFolderExistsIfEnabled()
        {
            try
            {
                if (_vm.DropFolderEnabled && !string.IsNullOrWhiteSpace(_vm.DropWatchFolder))
                    Directory.CreateDirectory(_vm.DropWatchFolder);
            }
            catch
            {
                // don’t crash settings window on folder permission issues
            }
        }

        private static string GetDefaultDropFolder()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(baseDir, "Echopad", "Drop");
        }

        private string? PickFolder(string title)
        {
            var dlg = new OpenFileDialog
            {
                Title = title,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder",
                Filter = "Folders|*.none"
            };

            if (dlg.ShowDialog(this) == true)
                return Path.GetDirectoryName(dlg.FileName);

            return null;
        }

        private void SetHotkey_ToggleEdit_Click(object sender, RoutedEventArgs e)
        {
            var cap = new HotkeyCaptureWindow { Owner = this };
            if (cap.ShowDialog() == true)
                _vm.HotkeyToggleEdit = cap.HotkeyText;
        }

        private void SetHotkey_OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var cap = new HotkeyCaptureWindow { Owner = this };
            if (cap.ShowDialog() == true)
                _vm.HotkeyOpenSettings = cap.HotkeyText;
        }

        private void LearnMidi_ToggleEdit_Click(object sender, RoutedEventArgs e)
        {
            _vm.MidiBindToggleEdit = "Learning…";
            if (Owner is MainWindow mw)
            {
                mw.BeginMidiLearn(bind =>
                {
                    _vm.MidiBindToggleEdit = bind;
                });
            }
        }

        private void LearnMidi_OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            _vm.MidiBindOpenSettings = "Learning…";
            if (Owner is MainWindow mw)
            {
                mw.BeginMidiLearn(bind =>
                {
                    _vm.MidiBindOpenSettings = bind;
                });
            }
        }

        private void SetHotkey_TrimSelectIn_Click(object sender, RoutedEventArgs e)
        {
            var cap = new HotkeyCaptureWindow { Owner = this };
            if (cap.ShowDialog() == true)
                _vm.HotkeyTrimSelectIn = cap.HotkeyText;
        }

        private void SetHotkey_TrimSelectOut_Click(object sender, RoutedEventArgs e)
        {
            var cap = new HotkeyCaptureWindow { Owner = this };
            if (cap.ShowDialog() == true)
                _vm.HotkeyTrimSelectOut = cap.HotkeyText;
        }

        private void SetHotkey_TrimPlus_Click(object sender, RoutedEventArgs e)
        {
            var cap = new HotkeyCaptureWindow { Owner = this };
            if (cap.ShowDialog() == true)
                _vm.HotkeyTrimNudgePlus = cap.HotkeyText;
        }

        private void SetHotkey_TrimMinus_Click(object sender, RoutedEventArgs e)
        {
            var cap = new HotkeyCaptureWindow { Owner = this };
            if (cap.ShowDialog() == true)
                _vm.HotkeyTrimNudgeMinus = cap.HotkeyText;
        }

        private void LearnMidi_TrimSelectIn_Click(object sender, RoutedEventArgs e)
        {
            _vm.MidiBindTrimSelectIn = "Learning…";
            if (Owner is MainWindow mw)
            {
                mw.BeginMidiLearn(bind =>
                {
                    _vm.MidiBindTrimSelectIn = bind;
                });
            }
        }

        private void LearnMidi_TrimSelectOut_Click(object sender, RoutedEventArgs e)
        {
            _vm.MidiBindTrimSelectOut = "Learning…";
            if (Owner is MainWindow mw)
            {
                mw.BeginMidiLearn(bind =>
                {
                    _vm.MidiBindTrimSelectOut = bind;
                });
            }
        }

        private void LearnMidi_TrimPlus_Click(object sender, RoutedEventArgs e)
        {
            _vm.MidiBindTrimNudgePlus = "Learning…";
            if (Owner is MainWindow mw)
            {
                mw.BeginMidiLearn(bind =>
                {
                    _vm.MidiBindTrimNudgePlus = bind;
                });
            }
        }

        private void TestMidiOut25_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is MainWindow mw)
            {
                mw.SendHardMidiOutTest_Value25();
            }
            else
            {
                MessageBox.Show(this,
                    "Settings window has no MainWindow Owner.\nCannot access MIDI OUT test.",
                    "Echopad MIDI Test",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void LearnMidi_TrimMinus_Click(object sender, RoutedEventArgs e)
        {
            _vm.MidiBindTrimNudgeMinus = "Learning…";
            if (Owner is MainWindow mw)
            {
                mw.BeginMidiLearn(bind =>
                {
                    _vm.MidiBindTrimNudgeMinus = bind;
                });
            }
        }
    }
}
