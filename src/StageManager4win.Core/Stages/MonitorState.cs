using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Stages;

public sealed record MonitorState(
    string MonitorId,
    IReadOnlyList<WindowId> Stage,
    IReadOnlyList<WindowId> Strip,
    WindowId? Foreground,
    string? ActiveAppId);
