namespace StageManager4win.Core.Layout;

public sealed record StripLayout(
    IReadOnlyList<TilePlacement> Tiles,
    int FirstVisibleIndex,
    int VisibleCount);
