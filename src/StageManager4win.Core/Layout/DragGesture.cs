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

    public static bool ShouldDrop(RectD monitor, RectD strip, double x, double y) =>
        !Outside(monitor, x, y) && Outside(strip, x, y);
}
