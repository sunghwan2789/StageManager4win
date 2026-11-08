namespace StageManager4win.Core.Layout;

public readonly record struct StackCard(double OffsetY, double Scale, double RotationDegrees, float Dim);

public static class TileStack
{
    public const double OverflowTop = 20;
    public const double OverflowSide = 8;

    public static RectD HitBounds(RectD front) =>
        new(
            front.X - OverflowSide,
            front.Y - OverflowTop,
            front.Width + OverflowSide * 2,
            front.Height + OverflowTop);

    public static StackCard Card(int index) => index switch
    {
        0 => new StackCard(0, 1.0, 0, 1f),
        1 => new StackCard(-6, 0.96, -2.5, 0.6f),
        _ => new StackCard(-11, 0.92, 2.0, 0.4f),
    };
}
