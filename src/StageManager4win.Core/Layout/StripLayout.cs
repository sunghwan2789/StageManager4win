namespace StageManager4win.Core.Layout;

public sealed record StripLayout(
    IReadOnlyList<TilePlacement> Tiles,
    RectD StripBounds,
    double ContentHeight,
    double MaxScroll);
