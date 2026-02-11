using System;
using System.Collections.Generic;

namespace Echopad.Core
{
    public sealed class ProfileSwitchSettings
    {
        public int ActiveProfileIndex { get; set; } = 1;
        public ProfileMidiLinkMode MidiLinkMode { get; set; } = ProfileMidiLinkMode.PerProfile;
        // "Hold this MIDI bind + press slot bind"
        public string? MidiModifierBind { get; set; }
        public bool PadsMidiSameAsProfile1 { get; set; }
        public bool PadsMidiAndHotkeysSameAsProfile1 { get; set; }

        // e.g. "Ctrl+Shift"
        public string? HotkeyModifier { get; set; } = "Ctrl+Shift";

        // Always 16 items
        public List<ProfileSlotBind> Slots { get; set; } = new();

        public void EnsureSlots()
        {
            Slots ??= new List<ProfileSlotBind>();

            while (Slots.Count < 16)
                Slots.Add(new ProfileSlotBind());

            if (Slots.Count > 16)
                Slots.RemoveRange(16, Slots.Count - 16);
        }

    }

   
}
