namespace StageManager4win.Core.Windows;

public readonly record struct WindowTraits(
    bool IsTopLevel,
    bool IsVisible,
    bool IsToolWindow,
    bool IsNoActivate,
    bool IsTopmost,
    bool IsCloaked,
    bool HasOwner,
    bool IsShellWindow,
    bool IsOwnProcess,
    bool HasNonZeroSize,
    bool IsFullscreen);
