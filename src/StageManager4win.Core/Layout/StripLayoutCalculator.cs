using StageManager4win.Core.Stages;

namespace StageManager4win.Core.Layout;

public static class StripLayoutCalculator
{
    private const double BadgeSizeDip = 28;
    private const double BadgeGapDip = 4;
    private const double BadgeOverlapDip = 4;
    private const double BadgeInsetDip = 6;

    public static StripLayout Calculate(
        IReadOnlyList<StripItem> items,
        StripLayoutOptions options,
        double scrollOffset = 0)
    {
        var stripWidth = Math.Clamp(
            options.MonitorWidth * options.StripWidthRatio,
            options.MinStripWidth,
            options.MaxStripWidth);
        var stripBounds = new RectD(
            options.Edge == StripEdge.Left ? 0 : options.MonitorWidth - stripWidth,
            options.WorkAreaTop,
            stripWidth,
            options.WorkAreaHeight);

        var sizes = new List<(StripItem Item, double CardWidth, double CardHeight, double UnionWidth, int Count)>(items.Count);
        var contentHeight = 0.0;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var aspect = NormalizeAspect(item.AspectRatio);
            var cardWidth = Math.Min(options.MaxTileWidth, options.MaxTileHeight * aspect);
            var cardHeight = cardWidth / aspect;
            var members = item.Members ?? Array.Empty<WindowKey>();
            var count = Math.Max(1, members.Count);
            var unionWidth = cardWidth + (count - 1) * TileStack.Step(count, cardWidth);
            if (index > 0)
            {
                contentHeight += options.Gap;
            }

            contentHeight += cardHeight;
            sizes.Add((item, cardWidth, cardHeight, unionWidth, count));
        }

        // Scrolling moves the whole column; content that fits stays centered and
        // ignores the wheel so items never disappear when there is nothing to scroll.
        var maxScroll = Math.Max(0, contentHeight - options.WorkAreaHeight);
        var offset = Math.Clamp(scrollOffset, 0, maxScroll);
        var viewBottom = options.WorkAreaTop + options.WorkAreaHeight;
        var y = maxScroll > 0
            ? options.WorkAreaTop - offset
            : options.WorkAreaTop + (options.WorkAreaHeight - contentHeight) / 2;

        var tiles = new List<TilePlacement>(sizes.Count);
        foreach (var (item, cardWidth, cardHeight, unionWidth, count) in sizes)
        {
            if (y < viewBottom && y + cardHeight > options.WorkAreaTop)
            {
                var centered = (stripWidth - unionWidth) / 2;
                var x = options.Edge == StripEdge.Left
                    ? Math.Max(0, centered)
                    : Math.Min(
                        options.MonitorWidth - unionWidth,
                        options.MonitorWidth - stripWidth + centered);
                var middleY = y + cardHeight / 2;
                var union = TileStack.UnionBounds(count, cardWidth, cardHeight, x, middleY);
                var members = item.Members ?? Array.Empty<WindowKey>();
                var cards = new List<CardPlacement>(members.Count);
                for (var index = 0; index < members.Count; index++)
                {
                    cards.Add(new CardPlacement(
                        members[index],
                        TileStack.CardBounds(
                            index,
                            count,
                            cardWidth,
                            cardHeight,
                            x,
                            middleY,
                            options.Edge),
                        count - 1 - index,
                        TileStack.Scale(index, count)));
                }

                var badge = BadgeBounds(union, Math.Max(1, item.AppCount), options.Edge);
                tiles.Add(new TilePlacement(
                    item.SetId,
                    union,
                    cards,
                    badge,
                    TileStack.HitBounds(union)));
            }

            y += cardHeight + options.Gap;
        }

        return new StripLayout(tiles, stripBounds, contentHeight, maxScroll);
    }

    private static RectD BadgeBounds(RectD union, int appCount, StripEdge edge)
    {
        var slots = Math.Min(4, appCount);
        if (appCount >= 5)
        {
            slots++;
        }

        var spacedWidth = slots * BadgeSizeDip + (slots - 1) * BadgeGapDip;
        var maxWidth = Math.Max(BadgeSizeDip, union.Width - BadgeInsetDip * 2);
        var width = spacedWidth <= maxWidth
            ? spacedWidth
            : BadgeSizeDip + (slots - 1) * (BadgeSizeDip - BadgeOverlapDip);
        var bottom = union.Y + union.Height - BadgeInsetDip;
        var x = edge == StripEdge.Left
            ? union.X + BadgeInsetDip
            : union.X + union.Width - BadgeInsetDip - width;
        return new RectD(x, bottom - BadgeSizeDip, width, BadgeSizeDip);
    }

    private static double NormalizeAspect(double aspect)
    {
        if (double.IsNaN(aspect) || double.IsInfinity(aspect))
        {
            return 4.0 / 3.0;
        }

        return Math.Clamp(aspect, 0.25, 4.0);
    }
}
