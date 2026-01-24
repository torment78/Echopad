namespace Echopad.Core
{
    // =========================================================
    // CLIP MODE (edit / copy state)
    // =========================================================
    // This is NOT UI logic.
    // It describes the role of the pad during edit operations.
    public enum ClipMod
    {
        None = 0,

        // This pad is the active copy source (red outline)
        CopySource = 1,

        // This pad has received a copied clip (yellow outline)
        CopiedTarget = 2
    }
}
