namespace StageManager4win.Core.Windows;

public sealed record WindowSnapshot(
    WindowId Id,
    int ProcessId,
    string AppId,
    string AppName,
    string Title,
    string? ExecutablePath,
    WindowBounds Bounds,
    string MonitorId,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    bool IsFullscreen);
