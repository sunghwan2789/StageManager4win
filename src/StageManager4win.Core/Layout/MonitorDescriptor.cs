using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Layout;

public sealed record MonitorDescriptor(
    string Id,
    WindowBounds Bounds,
    WindowBounds WorkArea,
    uint Dpi,
    string GdiDeviceName = "",
    string Name = "",
    bool IsPrimary = false);
