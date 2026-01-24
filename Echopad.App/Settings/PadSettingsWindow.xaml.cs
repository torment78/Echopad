using Microsoft.Win32;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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

        public PadSettingsWindow(PadSettingsViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = _vm;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            _vm.Save();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BrowseAudio_Click(object sender, RoutedEventArgs e)
        {
            // Explicit Win32 dialog (no ambiguity)
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select audio file",
                Filter = "Audio Files|*.wav;*.mp3;*.aac;*.m4a;*.wma|All Files|*.*"
            };

            if (dlg.ShowDialog(this) == true)
            {
                _vm.SetClipFromFile(dlg.FileName);
            }
        }

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

        private void ResetTrim_Click(object sender, RoutedEventArgs e)
        {
            _vm.ResetTrim();
        }

        // StartMs spin buttons
        private void StartMs_Up_Click(object sender, RoutedEventArgs e)
            => _vm.NudgeStart(+_vm.TrimStepMs);

        private void StartMs_Down_Click(object sender, RoutedEventArgs e)
            => _vm.NudgeStart(-_vm.TrimStepMs);

        // EndMs spin buttons
        private void EndMs_Up_Click(object sender, RoutedEventArgs e)
            => _vm.NudgeEnd(+_vm.TrimStepMs);

        private void EndMs_Down_Click(object sender, RoutedEventArgs e)
            => _vm.NudgeEnd(-_vm.TrimStepMs);

        // =========================================================
        // PER-PAD MIDI LEARN (REAL WIRING)
        // =========================================================
        private void LearnMidiPad_Click(object sender, RoutedEventArgs e)
        {
            if (_isLearningMidi)
                return;

            _isLearningMidi = true;
            _vm.IsMidiLearning = true;

            if (sender is Button b)
                b.IsEnabled = false;

            _vm.MidiTriggerDisplay = "Learning...";

            var mw = Application.Current?.MainWindow;
            if (mw == null)
            {
                EndLearnUi(sender as Button);
                _vm.MidiTriggerDisplay = "Learn:Failed (no MainWindow)";
                return;
            }

            Action<string> onLearned = bind =>
            {
                Dispatcher.Invoke(() =>
                {
                    _vm.MidiTriggerDisplay = bind;
                    EndLearnUi(sender as Button);
                }, DispatcherPriority.Send);
            };

            Action onCanceled = () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (string.Equals(_vm.MidiTriggerDisplay, "Learning...", StringComparison.OrdinalIgnoreCase))
                        _vm.MidiTriggerDisplay = "Learn:Canceled";
                    EndLearnUi(sender as Button);
                }, DispatcherPriority.Send);
            };

            if (!TryInvokeMainWindowMidiLearn((Window)mw, onLearned, onCanceled))
            {
                EndLearnUi(sender as Button);
                _vm.MidiTriggerDisplay = "Learn:Failed (no learn hook)";
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
                // (Action<string>)
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

                // (Action<string>, Action)
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

                // (string, Action<string>)
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

                // (string, Action<string>, Action)
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
        // PER-PAD COLOR PICKERS (ACTIVE / RUNNING)
        // =========================================================
        private void PickActiveColor_Click(object sender, RoutedEventArgs e)
        {
            PickColor(hex => _vm.UiActiveHex = hex);
        }

        private void PickRunningColor_Click(object sender, RoutedEventArgs e)
        {
            PickColor(hex => _vm.UiRunningHex = hex);
        }

        private void PickColor(Action<string> apply)
        {
            using var dlg = new WF.ColorDialog
            {
                FullOpen = true
            };

            if (dlg.ShowDialog() == WF.DialogResult.OK)
            {
                var c = dlg.Color;
                apply($"#{c.R:X2}{c.G:X2}{c.B:X2}");
            }
        }
    }
}
