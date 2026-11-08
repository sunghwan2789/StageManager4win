namespace StageManager4win.Core.Layout;

public sealed record StripLayout(
    IReadOnlyList<TilePlacement> Tiles,
    RectD StripBounds,
    int FirstVisibleIndex,
    int VisibleCount);
