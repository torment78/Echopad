using System;

namespace Echopad.Core
{
    public sealed class PadSettings
    {
        public int Index { get; set; }

        public string? ClipPath { get; set; }

        public int StartMs { get; set; }
        public int EndMs { get; set; }

        public int InputSource { get; set; } = 1;
        public bool PreviewToMonitor { get; set; } = true;

        public bool IsEchoMode { get; set; }
        public bool IsDropFolderMode { get; set; }

        public string? PadHotkey { get; set; }
        public string? MidiTriggerDisplay { get; set; }

        // ============================
        // MIDI LED VALUES (0–127)
        // ============================

        public bool MidiLedActiveEnabled { get; set; } = true;
        public int MidiLedActiveValue { get; set; } = 25;

        public bool MidiLedRunningEnabled { get; set; } = true;
        public int MidiLedRunningValue { get; set; } = 127;

        public bool MidiLedClearEnabled { get; set; } = true;
        public int MidiLedClearValue { get; set; } = 0;

        // ============================================================
        // NEW: MIDI LED RAW OVERRIDES (HEX BYTES)
        //
        // If set (non-empty), these override the value-only send.
        // Format examples:
        //   "90 30 7F"
        //   "{STATUS} {DATA1} {VAL}"
        //   "B0 10 {VAL}"
        //
        // Tokens supported by our sender:
        //   {STATUS}  -> computed from bind kind + channel (e.g. 90 / B0 etc)
        //   {DATA1}   -> bind.Number (note or cc number)
        //   {VAL}     -> computed LED value 00..7F
        //
        // Leave null/empty to keep old velocity-only behavior.
        // ============================================================

        public string? MidiLedActiveRaw { get; set; }   // Loaded / Active
        public string? MidiLedRunningRaw { get; set; }  // Playing / Running
        public string? MidiLedArmedRaw { get; set; }    // Armed (EchoMode, no clip)
        public string? MidiLedClearRaw { get; set; }    // Empty / Clear

        // ============================
        // PER-PAD UI COLORS (HEX)
        // ============================
        // null or empty = use theme default
        public string? UiActiveHex { get; set; }   // Loaded / Armed
        public string? UiRunningHex { get; set; }  // Playing
        // Clear/Empty is always theme gray (no setting)

        public static PadSettings CreateDefault(int index)
        {
            return new PadSettings
            {
                Index = index,
                MidiLedActiveValue = 25,
                MidiLedRunningValue = 127,
                MidiLedClearValue = 0

                // NEW (optional): leave null so old behavior stays the default
                // MidiLedActiveRaw = null,
                // MidiLedRunningRaw = null,
                // MidiLedArmedRaw = null,
                // MidiLedClearRaw = null,
            };
        }
    }
}
