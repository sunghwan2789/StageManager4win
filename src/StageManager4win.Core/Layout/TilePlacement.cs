using StageManager4win.Core.Stages;

namespace StageManager4win.Core.Layout;

public sealed record TilePlacement(
    string SetId,
    RectD UnionBoundsDip,
    IReadOnlyList<CardPlacement> Cards,
    RectD BadgeBoundsDip,
    RectD HitBoundsDip);

public readonly record struct CardPlacement(
    WindowKey Window,
    RectD BoundsDip,
    int StackIndex,
    double Scale);

public readonly record struct ListingRowPlacement(
    string SetId,
    WindowKey Window,
    int RowIndex,
    RectD BoundsDip);

public enum HitKind
{
    Card,
    Badge,
    ListingRow,
    SetBackground
}

public readonly record struct HitTarget(
    HitKind Kind,
    string SetId,
    WindowKey? Window = null,
    int? RowIndex = null);
