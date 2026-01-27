using System;
using System.Collections.Generic;

namespace Echopad.Core
{
    public sealed class GlobalSettings
    {
        // =========================================================
        // OLD (keep for backward compatibility)
        // =========================================================
        public string? Input1DeviceId { get; set; }
        public string? Input2DeviceId { get; set; }
        public string? MainOutDeviceId { get; set; }
        public string? MonitorOutDeviceId { get; set; }

        // =========================================================
        // NEW: Endpoint-based audio routing (per channel mode)
        // Input1/Input2 = Local capture/loopback OR VBAN RX
        // Out1/Out2     = Local WASAPI OR VBAN TX
        // =========================================================
        public InputEndpointSettings Input1 { get; set; } = new();
        public InputEndpointSettings Input2 { get; set; } = new();

        public OutputEndpointSettings Out1 { get; set; } = new(); // replaces MainOut concept
        public OutputEndpointSettings Out2 { get; set; } = new(); // replaces MonitorOut concept

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

        public int MidiArmedInput1Value { get; set; } = 38; // pink-ish
        public int MidiArmedInput2Value { get; set; } = 48; // blue-ish

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

        // =========================================================
        // NEW: Backward-compat sync helper
        //
        // Call this once after loading settings from disk.
        // It will:
        //  - Populate new endpoint fields from legacy device IDs if missing
        //  - Keep legacy fields updated from new endpoint fields (optional)
        // =========================================================
        public void EnsureCompatibility()
        {
            // -------- Inputs --------
            if (Input1 == null) Input1 = new InputEndpointSettings();
            if (Input2 == null) Input2 = new InputEndpointSettings();

            // If endpoint LocalDeviceId missing, fall back to legacy InputXDeviceId
            if (string.IsNullOrWhiteSpace(Input1.LocalDeviceId) && !string.IsNullOrWhiteSpace(Input1DeviceId))
                Input1.LocalDeviceId = Input1DeviceId;

            if (string.IsNullOrWhiteSpace(Input2.LocalDeviceId) && !string.IsNullOrWhiteSpace(Input2DeviceId))
                Input2.LocalDeviceId = Input2DeviceId;

            // Keep legacy fields updated (so older code paths still work)
            if (!string.IsNullOrWhiteSpace(Input1.LocalDeviceId))
                Input1DeviceId = Input1.LocalDeviceId;

            if (!string.IsNullOrWhiteSpace(Input2.LocalDeviceId))
                Input2DeviceId = Input2.LocalDeviceId;

            // -------- Outputs --------
            if (Out1 == null) Out1 = new OutputEndpointSettings();
            if (Out2 == null) Out2 = new OutputEndpointSettings();

            if (string.IsNullOrWhiteSpace(Out1.LocalDeviceId) && !string.IsNullOrWhiteSpace(MainOutDeviceId))
                Out1.LocalDeviceId = MainOutDeviceId;

            if (string.IsNullOrWhiteSpace(Out2.LocalDeviceId) && !string.IsNullOrWhiteSpace(MonitorOutDeviceId))
                Out2.LocalDeviceId = MonitorOutDeviceId;

            if (!string.IsNullOrWhiteSpace(Out1.LocalDeviceId))
                MainOutDeviceId = Out1.LocalDeviceId;

            if (!string.IsNullOrWhiteSpace(Out2.LocalDeviceId))
                MonitorOutDeviceId = Out2.LocalDeviceId;

            // If user hasn't decided modes yet, default to Local
            // (Don't override if already set)
            if (Input1.Mode != AudioEndpointMode.Local && Input1.Mode != AudioEndpointMode.Vban)
                Input1.Mode = AudioEndpointMode.Local;

            if (Input2.Mode != AudioEndpointMode.Local && Input2.Mode != AudioEndpointMode.Vban)
                Input2.Mode = AudioEndpointMode.Local;

            if (Out1.Mode != AudioEndpointMode.Local && Out1.Mode != AudioEndpointMode.Vban)
                Out1.Mode = AudioEndpointMode.Local;

            if (Out2.Mode != AudioEndpointMode.Local && Out2.Mode != AudioEndpointMode.Vban)
                Out2.Mode = AudioEndpointMode.Local;
        }
    }
}
