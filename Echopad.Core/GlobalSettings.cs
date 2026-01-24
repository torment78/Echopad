using System.Collections.Generic;

namespace Echopad.Core
{
    public sealed class GlobalSettings
    {
        public string? Input1DeviceId { get; set; }
        public string? Input2DeviceId { get; set; }
        public string? MainOutDeviceId { get; set; }
        public string? MonitorOutDeviceId { get; set; }

        public string? MidiInDeviceId { get; set; }
        public string? MidiOutDeviceId { get; set; }

        // Global UI colors (fallbacks)
        public string? UiArmedInput1Hex { get; set; } = "#FF4DB8"; // armed from Input 1
        public string? UiArmedInput2Hex { get; set; } = "#4DA3FF"; // armed from Input 2
        public string? UiActiveHex { get; set; } = "#3DFF8B"; // loaded
        public string? UiRunningHex { get; set; } = "#00FF6A"; // playing
        public string? UiClearHex { get; set; } = "#5A5A5A"; // empty


        public string? DropWatchFolder { get; set; }   // Dedicated folder path
        public bool DropFolderEnabled { get; set; } = true;

        public List<string> AudioFolders { get; set; } = new();

        public string? HotkeyToggleEdit { get; set; }
        public string? HotkeyOpenSettings { get; set; }

        public string? MidiBindToggleEdit { get; set; }
        public string? MidiBindOpenSettings { get; set; }

        public string? HotkeyTrimSelectIn { get; set; }
        public string? HotkeyTrimSelectOut { get; set; }
        public string? HotkeyTrimNudgePlus { get; set; }
        public string? HotkeyTrimNudgeMinus { get; set; }

        public string? MidiBindTrimSelectIn { get; set; }
        public string? MidiBindTrimSelectOut { get; set; }
        public string? MidiBindTrimNudgePlus { get; set; }
        public string? MidiBindTrimNudgeMinus { get; set; }
        public int MidiArmedInput1Value { get; set; } = 38; // NEW (pink-ish example)
        public int MidiArmedInput2Value { get; set; } = 48; // NEW (blue-ish example)

        public Dictionary<int, PadSettings> Pads { get; set; } = new();

        public PadSettings GetOrCreatePad(int index)
        {
            if (!Pads.TryGetValue(index, out var ps))
            {
                ps = PadSettings.CreateDefault(index);
                Pads[index] = ps;
            }
            return ps;
        }
    }
}
