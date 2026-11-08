namespace StageManager4win.Core.Layout;

public static class DragGesture
{
    public const double ClickSlopDip = 4;

    public static bool IsClick(double dx, double dy) =>
        dx * dx + dy * dy <= ClickSlopDip * ClickSlopDip;

    public static bool Outside(RectD bounds, double x, double y) =>
        x < bounds.X
        || y < bounds.Y
        || x >= bounds.X + bounds.Width
        || y >= bounds.Y + bounds.Height;
}
