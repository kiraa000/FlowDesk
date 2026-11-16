namespace FlowDesk.Core.Palette;

public enum PaletteFocusDecision { Focused, Retry, Cancel, Failed }
public static class PaletteFocusRules
{
    public static PaletteFocusDecision Decide(bool currentOpen, bool foregroundIsPalette, bool foregroundIsOriginal, bool searchFocused, int attempt)
    {
        if (!currentOpen) return PaletteFocusDecision.Cancel;
        if (foregroundIsPalette && searchFocused) return PaletteFocusDecision.Focused;
        if (!foregroundIsPalette && !foregroundIsOriginal) return PaletteFocusDecision.Cancel;
        return attempt >= 3 ? PaletteFocusDecision.Failed : PaletteFocusDecision.Retry;
    }
}
