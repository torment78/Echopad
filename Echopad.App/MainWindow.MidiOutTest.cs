using NAudio.Midi;
using System;
using System.Diagnostics;
using System.Windows;

namespace Echopad.App
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Hard MIDI OUT test: sends a single CC message with value=25.
        /// This is ONLY for verifying that MIDI OUT traffic is leaving Echopad
        /// and shows up in your MIDI router.
        /// </summary>
        public void SendHardMidiOutTest_Value25()
        {
            try
            {
                // If MIDI OUT isn't open yet, try opening it from current settings
                if (_midiOut == null)
                {
                    Debug.WriteLine("[MIDI-OUT-TEST] _midiOut is null. Calling SetupMidiDevices()...");
                    SetupMidiDevices();
                }

                if (_midiOut == null)
                {
                    Debug.WriteLine("[MIDI-OUT-TEST] STILL null after SetupMidiDevices(). No MIDI OUT selected/open.");
                    MessageBox.Show(this,
                        "MIDI OUT is not open.\n\nGo to Settings → MIDI Out and select the port you expect your router to see.",
                        "Echopad MIDI Test",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // === HARD TEST PAYLOAD ===
                // Traktor Kontrol F1 in CC mode: we'll just send something simple:
                // CC #0 on Channel 0 (MIDI Channel 1) with Value 25.
                int channel0 = 13; // 0..15 (NAudio)
                int ccNumber = 10; // 0..127 (you can change this to match your mapping)
                int value = 28;   // requested test value

                var msg = MidiMessage.ChangeControl(ccNumber, value, channel0).RawData;
                _midiOut.Send(msg);

                Debug.WriteLine($"[MIDI-OUT-TEST] SENT -> CC ch={channel0} cc={ccNumber} val={value}");

                MessageBox.Show(this,
                    $"Sent MIDI OUT test:\n\nCC ch={channel0} cc={ccNumber} val={value}\n\nNow check your MIDI router output monitor.",
                    "Echopad MIDI Test",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MIDI-OUT-TEST] ERROR: " + ex);
                MessageBox.Show(this,
                    "Failed to send MIDI OUT test:\n\n" + ex.Message,
                    "Echopad MIDI Test",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
