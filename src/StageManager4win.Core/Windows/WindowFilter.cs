namespace StageManager4win.Core.Windows;

public static class WindowFilter
{
    public static bool IsCandidate(WindowTraits traits) =>
        traits.IsTopLevel
        && traits.IsVisible
        && !traits.IsToolWindow
        && !traits.IsNoActivate
        && !traits.IsTopmost
        && !traits.IsCloaked
        && !traits.HasOwner
        && !traits.IsShellWindow
        && !traits.IsOwnProcess
        && traits.HasNonZeroSize
        && !traits.IsFullscreen;
}
