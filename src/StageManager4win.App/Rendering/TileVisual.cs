using StageManager4win.Core.Layout;
using StageManager4win.Core.Windows;

namespace StageManager4win.Rendering;

public sealed record TileVisual(
    WindowId Window,
    RectD BoundsDip,
    double CornerRadiusDip,
    IconBitmap? Icon,
    bool IsMinimized,
    nint SourceHwnd,
    string AppId,
    string AppName,
    string Title,
    bool HighContrast,
    uint BorderBgra);
