namespace FlowDesk.Core.Palette;

public static class PaletteAnswerInput
{
    // Printable composed text (Shift/caps/layout/AltGr already resolved by CharacterReceived)
    // starts a fresh query in the current mode. Control shortcuts stay native to the focused text.
    public static bool StartsSearch(char character, bool control, bool alt, bool altGr = false) =>
        !char.IsControl(character) && ((!control && !alt) || altGr);
}
