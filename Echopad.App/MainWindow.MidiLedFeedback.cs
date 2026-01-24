using Echopad.Core;
using System;
using System.Diagnostics;
using System.IO;

namespace Echopad.App
{
    public partial class MainWindow
    {
        private void SyncAllPadLeds()
        {
            if (DataContext is not MainViewModel vm)
                return;

            foreach (var pad in vm.Pads)
                UpdatePadLedForCurrentState(pad);
        }

        private void UpdatePadLedForCurrentState(PadModel pad)
        {
            if (_midiOut == null)
                return;

            var ps = _globalSettings.GetOrCreatePad(pad.Index);
            if (string.IsNullOrWhiteSpace(ps.MidiTriggerDisplay))
                return;

            var bind = TryParseMidiBind(ps.MidiTriggerDisplay);
            if (!bind.HasValue)
                return;

            bool hasFile =
                !string.IsNullOrWhiteSpace(pad.ClipPath) &&
                File.Exists(pad.ClipPath);

            // NEW: proper PadState mapping
            // - No file:
            //    - EchoMode ON  => Armed (use global input color)
            //    - EchoMode OFF => Empty  (use clear)
            // - Has file:
            //    - Playing => Playing (use running)
            //    - else    => Loaded  (use active)
            PadState state =
                !hasFile
                    ? (pad.IsEchoMode ? PadState.Armed : PadState.Empty)
                    : (pad.State == PadState.Playing ? PadState.Playing : PadState.Loaded);

            SendPadLed(ps, bind.Value, state, pad); // pass pad for InputSource
        }

        private void SendPadLed(PadSettings ps, MidiBind bind, PadState state, PadModel pad)
        {
            if (_midiOut == null)
                return;

            int value = state switch
            {
                // Loaded (has clip, not playing)
                PadState.Loaded when ps.MidiLedActiveEnabled => ps.MidiLedActiveValue,

                // Playing
                PadState.Playing when ps.MidiLedRunningEnabled => ps.MidiLedRunningValue,

                // Armed (EchoMode enabled, no clip yet) -> global input color value
                PadState.Armed => (pad.InputSource <= 1)
                    ? _globalSettings.MidiArmedInput1Value
                    : _globalSettings.MidiArmedInput2Value,

                // Empty / fallback -> clear
                _ when ps.MidiLedClearEnabled => ps.MidiLedClearValue,

                _ => -1
            };

            if (value < 0)
                return;

            value = Math.Clamp(value, 0, 127);

            // bind.Channel is 1..16 (human / NAudio)
            if (bind.Kind == MidiBindKind.Cc)
                SendCc(bind.Channel, bind.Number, value);
            else if (bind.Kind == MidiBindKind.Note)
                SendNoteOn(bind.Channel, bind.Number, value);
        }

        private void SendCc(int channel1, int cc, int val)
        {
            // Convert human 1..16 -> status nibble 0..15
            int ch0 = Math.Clamp(channel1, 1, 16) - 1;

            int msg =
                (0xB0 | (ch0 & 0x0F)) |
                ((cc & 0x7F) << 8) |
                ((val & 0x7F) << 16);

            _midiOut.Send(msg);

            Debug.WriteLine($"[LED] CC ch={channel1} cc={cc} val={val}");
        }

        private void SendNoteOn(int channel1, int note, int vel)
        {
            int ch0 = Math.Clamp(channel1, 1, 16) - 1;

            int msg =
                (0x90 | (ch0 & 0x0F)) |
                ((note & 0x7F) << 8) |
                ((vel & 0x7F) << 16);

            _midiOut.Send(msg);

            Debug.WriteLine($"[LED] NOTE ch={channel1} note={note} vel={vel}");
        }
    }
}
