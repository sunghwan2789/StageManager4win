namespace StageManager4win.Core.Layout;

public static class TileStack
{
    public const double MaxUnionWidth = 300;
    public const double MaxCardStep = 14;
    public const double MinCardStep = 4;
    public const double DepthScale = 0.97;
    public const double MinDepthScale = 0.86;
    public const double OverflowTop = 20;
    public const double OverflowBottom = 8;
    public const double OverflowSide = 28;

    public static double Step(int count, double cardWidth)
    {
        if (count <= 1)
        {
            return 0;
        }

        var step = (MaxUnionWidth - cardWidth) / (count - 1);
        return Math.Clamp(step, MinCardStep, MaxCardStep);
    }

    public static double Scale(int stackIndex, int count)
    {
        if (count <= 1)
        {
            return 1;
        }

        var depth = Math.Clamp(count - 1 - stackIndex, 0, count - 1);
        return Math.Max(MinDepthScale, Math.Pow(DepthScale, depth));
    }

    public static RectD UnionBounds(
        int count,
        double cardWidth,
        double cardHeight,
        double unionLeft,
        double middleY)
    {
        var effectiveCount = Math.Max(1, count);
        var width = cardWidth + (effectiveCount - 1) * Step(effectiveCount, cardWidth);
        return new RectD(unionLeft, middleY - cardHeight / 2, width, cardHeight);
    }

    public static RectD CardBounds(
        int stackIndex,
        int count,
        double cardWidth,
        double cardHeight,
        double unionLeft,
        double middleY,
        StripEdge edge)
    {
        var effectiveCount = Math.Max(1, count);
        var index = Math.Clamp(stackIndex, 0, effectiveCount - 1);
        var scale = Scale(index, effectiveCount);
        var width = cardWidth * scale;
        var height = cardHeight * scale;
        var unionWidth = cardWidth + (effectiveCount - 1) * Step(effectiveCount, cardWidth);
        var left = edge == StripEdge.Left
            ? unionLeft + index * Step(effectiveCount, cardWidth)
            : unionLeft + unionWidth - index * Step(effectiveCount, cardWidth) - width;
        return new RectD(left, middleY - height / 2, width, height);
    }

    public static RectD HitBounds(RectD union) =>
        new(
            union.X - OverflowSide,
            union.Y - OverflowTop,
            union.Width + OverflowSide * 2,
            union.Height + OverflowTop + OverflowBottom);
}
