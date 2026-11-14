using StageManager4win.Core.Layout;
using StageManager4win.Core.Stages;

namespace StageManager4win.Rendering;

public sealed record SetTileVisual(
    string SetId,
    RectD UnionBoundsDip,
    double CornerRadiusDip,
    IReadOnlyList<CardVisual> Cards,
    IReadOnlyList<BadgeChipVisual> Badges,
    RectD BadgeBoundsDip,
    IReadOnlyList<ListingRowVisual> ListingRows,
    bool ListingOpen,
    bool HighContrast,
    uint BorderBgra);

public sealed record CardVisual(
    WindowKey Window,
    RectD BoundsDip,
    int StackIndex,
    double Scale,
    IconBitmap? Icon,
    bool IsMinimized,
    nint SourceHwnd,
    string AppId,
    string AppName,
    string Title);

public sealed record BadgeChipVisual(
    string AppId,
    string AppName,
    int Count,
    RectD BoundsDip,
    bool IsOverflow,
    IconBitmap? Icon);

public sealed record ListingRowVisual(
    WindowKey Window,
    int RowIndex,
    RectD BoundsDip,
    string AppId,
    string AppName,
    string Title,
    IconBitmap? Icon);
