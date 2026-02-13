using Echopad.Core;
using System;
using System.Diagnostics;
using System.Globalization; // NEW
using System.IO;

namespace Echopad.App
{
    public partial class MainWindow
    {
        private void SyncAllPadLeds()
        {
            SuppressMidiInput(250);
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

            PadState state =
                !hasFile
                    ? (pad.IsEchoMode ? PadState.Armed : PadState.Empty)
                    : (pad.State == PadState.Playing ? PadState.Playing : PadState.Loaded);

            SendPadLed(ps, bind.Value, state, pad);
        }

        private void SendPadLed(PadSettings ps, MidiBind bind, PadState state, PadModel pad)
        {
            if (_midiOut == null)
                return;

            int value = state switch
            {
                PadState.Loaded when ps.MidiLedActiveEnabled => ps.MidiLedActiveValue,
                PadState.Playing when ps.MidiLedRunningEnabled => ps.MidiLedRunningValue,

                PadState.Armed => (pad.InputSource <= 1)
                    ? _globalSettings.MidiArmedInput1Value
                    : _globalSettings.MidiArmedInput2Value,

                _ when ps.MidiLedClearEnabled => ps.MidiLedClearValue,
                _ => -1
            };

            if (value < 0)
                return;

            value = Math.Clamp(value, 0, 127);

            // ============================================================
            // NEW: optional RAW HEX override per state (does NOT break old)
            //
            // Expected new PadSettings strings (we’ll add these in settings):
            //   ps.MidiLedActiveRaw   e.g. "90 30 {VAL}"
            //   ps.MidiLedRunningRaw  e.g. "90 30 7F"
            //   ps.MidiLedClearRaw    e.g. "90 30 00"
            //
            // If set (non-empty), it sends raw bytes and returns early.
            // ============================================================

            string raw = state switch
            {
                PadState.Loaded => ps.MidiLedActiveRaw,   // NEW setting
                PadState.Playing => ps.MidiLedRunningRaw,  // NEW setting
                PadState.Armed => ps.MidiLedArmedRaw,    // NEW setting (optional)
                _ => ps.MidiLedClearRaw     // NEW setting
            };

            if (!string.IsNullOrWhiteSpace(raw))
            {
                // If user typed a simple number (0–127), treat it as VALUE
                if (int.TryParse(raw, out var numeric))
                {
                    value = Math.Clamp(numeric, 0, 127);
                    goto SEND_OLD;
                }

                int status = bind.Kind == MidiBindKind.Cc
                    ? (0xB0 | ((Math.Clamp(bind.Channel, 1, 16) - 1) & 0x0F))
                    : (0x90 | ((Math.Clamp(bind.Channel, 1, 16) - 1) & 0x0F));

                string expanded = raw
                    .Replace("{STATUS}", status.ToString("X2"))
                    .Replace("{DATA1}", (bind.Number & 0x7F).ToString("X2"))
                    .Replace("{VAL}", (value & 0x7F).ToString("X2"));

                SendRawHex(expanded);
                return;
            }


        SEND_OLD:
            if (bind.Kind == MidiBindKind.Cc)
                SendCc(bind.Channel, bind.Number, value);
            else if (bind.Kind == MidiBindKind.Note)
                SendNoteOn(bind.Channel, bind.Number, value);
        }

        private void SendCc(int channel1, int cc, int val)
        {
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

        // ============================================================
        // NEW: RAW MIDI SENDER
        //  - Accepts "90 30 7F" or "0x90,0x30,0x7F"
        //  - 3 bytes -> uses _midiOut.Send(int)
        //  - >3 bytes -> tries SendBuffer(byte[]) if available (NAudio)
        // ============================================================

        private void SendRawHex(string hex)
        {
            if (_midiOut == null)
                return;

            if (!TryParseHexBytes(hex, out var bytes) || bytes.Length == 0)
                return;

            if (bytes.Length == 3)
            {
                int msg = (bytes[0] & 0xFF) | ((bytes[1] & 0xFF) << 8) | ((bytes[2] & 0xFF) << 16);
                _midiOut.Send(msg);
                return;
            }

            // Try SendBuffer(byte[]) for SysEx / variable length (NAudio MidiOut supports this)
            try
            {
                // Using reflection so we don't hard-couple if _midiOut is a wrapper type.
                var mi = _midiOut.GetType().GetMethod("SendBuffer", new[] { typeof(byte[]) });
                if (mi != null)
                {
                    mi.Invoke(_midiOut, new object[] { bytes });
                    return;
                }

                Debug.WriteLine("[LED] RAW >3 bytes but midiOut has no SendBuffer(byte[])");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LED] RAW SendBuffer failed: {ex.Message}");
            }
        }
        private static string BuildRawHexFromMidiEvent(NAudio.Midi.MidiEvent ev)
        {
            try
            {
                int raw = ev.GetAsShortMessage();

                byte b1 = (byte)(raw & 0xFF);
                byte b2 = (byte)((raw >> 8) & 0xFF);
                byte b3 = (byte)((raw >> 16) & 0xFF);

                return $"{b1:X2} {b2:X2} {b3:X2}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryParseHexBytes(string text, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Split by whitespace and commas
            var parts = text
                .Replace(",", " ")
                .Trim()
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return false;

            var tmp = new byte[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();

                if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    p = p.Substring(2);

                // Allow "90" or "7F"
                if (!byte.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    return false;

                tmp[i] = b;
            }

            bytes = tmp;
            return true;
        }
    }
}
