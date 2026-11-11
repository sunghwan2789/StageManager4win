using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Layout;

public static class StripLayoutCalculator
{
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

        var sizes = new List<(WindowId Window, double Width, double Height)>(items.Count);
        var contentHeight = 0.0;
        for (var index = 0; index < items.Count; index++)
        {
            var aspect = NormalizeAspect(items[index].AspectRatio);
            var width = Math.Min(options.MaxTileWidth, options.MaxTileHeight * aspect);
            var height = width / aspect;
            if (index > 0)
            {
                contentHeight += options.Gap;
            }

            contentHeight += height;
            sizes.Add((items[index].Window, width, height));
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
        foreach (var (window, width, height) in sizes)
        {
            if (y < viewBottom && y + height > options.WorkAreaTop)
            {
                var x = options.Edge == StripEdge.Left
                    ? (stripWidth - width) / 2
                    : options.MonitorWidth - stripWidth + (stripWidth - width) / 2;
                tiles.Add(new TilePlacement(window, new RectD(x, y, width, height)));
            }

            y += height + options.Gap;
        }

        return new StripLayout(tiles, stripBounds, contentHeight, maxScroll);
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
