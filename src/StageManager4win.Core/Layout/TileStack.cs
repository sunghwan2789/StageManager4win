namespace StageManager4win.Core.Layout;

public readonly record struct StackCard(
    double OffsetX,
    double OffsetY,
    double Scale,
    float Dim);

public static class TileStack
{
    public const double OverflowTop = 20;
    public const double OverflowBottom = 8;
    public const double OverflowSide = 28;
    public const double TiltShiftRatio = 0.08;

    public static RectD HitBounds(RectD front) =>
        new(
            front.X - OverflowSide,
            front.Y - OverflowTop,
            front.Width + OverflowSide * 2,
            front.Height + OverflowTop + OverflowBottom);

    // Same-app windows only recede; the twist is per app tile, not inside the stack.
    public static StackCard Card(int index) => index switch
    {
        0 => new StackCard(0, 0, 1.0, 1f),
        1 => new StackCard(-14, -4, 0.97, 0.9f),
        _ => new StackCard(-28, -8, 0.94, 0.8f),
    };

    // Above the middle the far edge leans down, below it leans up, so the
    // strip fans away from the screen center instead of zigzagging per app.
    public static double TiltSlope(
        double centerY,
        double viewportCenterY,
        double viewportHalfHeight,
        double width,
        double height)
    {
        var position = Math.Clamp(
            (centerY - viewportCenterY) / Math.Max(1, viewportHalfHeight),
            -1,
            1);
        return -position * TiltShiftRatio * height / Math.Max(1, width);
    }
}
