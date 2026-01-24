namespace Echopad.Core
{
    public enum PadState
    {
        Empty = 0,

        // NEW: pad is armed for capture (Echo mode selected, but no clip assigned yet)
        Armed = 1,

        Loaded = 2,
        Playing = 3
    }
}
