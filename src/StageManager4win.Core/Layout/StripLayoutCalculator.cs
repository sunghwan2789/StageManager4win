using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Layout;

public static class StripLayoutCalculator
{
    public static StripLayout Calculate(
        IReadOnlyList<StripItem> items,
        StripLayoutOptions options,
        int firstVisibleIndex = 0)
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

        var start = Math.Clamp(firstVisibleIndex, 0, items.Count);
        var visible = new List<(WindowId Window, double Width, double Height)>();
        var totalHeight = 0.0;

        for (var index = start; index < items.Count; index++)
        {
            var aspect = NormalizeAspect(items[index].AspectRatio);
            var width = Math.Min(options.MaxTileWidth, options.MaxTileHeight * aspect);
            var height = width / aspect;

            if (visible.Count > 0 && totalHeight + options.Gap + height > options.WorkAreaHeight)
            {
                break;
            }

            if (visible.Count > 0)
            {
                totalHeight += options.Gap;
            }

            totalHeight += height;
            visible.Add((items[index].Window, width, height));
        }

        var y = options.WorkAreaTop + (options.WorkAreaHeight - totalHeight) / 2;
        var tiles = new List<TilePlacement>(visible.Count);

        foreach (var (window, width, height) in visible)
        {
            var x = options.Edge == StripEdge.Left
                ? (stripWidth - width) / 2
                : options.MonitorWidth - stripWidth + (stripWidth - width) / 2;

            tiles.Add(new TilePlacement(window, new RectD(x, y, width, height)));
            y += height + options.Gap;
        }

        return new StripLayout(tiles, stripBounds, start, tiles.Count);
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
