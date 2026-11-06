namespace StageManager4win.Core.Layout;

public sealed record StripLayoutOptions(
    double MonitorWidth,
    double MonitorHeight,
    double WorkAreaTop,
    double WorkAreaHeight,
    StripEdge Edge = StripEdge.Left,
    double MaxTileWidth = 160,
    double MaxTileHeight = 120,
    double Gap = 30,
    double StripWidthRatio = 0.12,
    double MinStripWidth = 160,
    double MaxStripWidth = 320);
