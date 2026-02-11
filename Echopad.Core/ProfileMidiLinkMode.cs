namespace Echopad.Core
{
    public enum ProfileMidiLinkMode
    {
        // Default: each profile can have its own pad MIDI/hotkeys
        PerProfile = 0,

        // NEW: all profiles use Profile 1 pad MIDI triggers
        PadsMidiSameAsProfile1 = 1,

        // NEW: all profiles use Profile 1 pad MIDI triggers + pad hotkeys
        PadsMidiAndHotkeysSameAsProfile1 = 2
    }
}
