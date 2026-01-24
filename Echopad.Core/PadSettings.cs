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
            };
        }
    }
}
