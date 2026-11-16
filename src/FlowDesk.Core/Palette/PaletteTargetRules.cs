namespace FlowDesk.Core.Palette;

public static class PaletteTargetRules
{
    public static bool IsAppTarget(uint process, uint ownProcess, string className) => process != 0 && process != ownProcess &&
        !string.IsNullOrWhiteSpace(className) && className is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "MultitaskingViewFrame");
}
