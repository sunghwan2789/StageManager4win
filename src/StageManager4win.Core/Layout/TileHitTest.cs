using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Layout;

public static class TileHitTest
{
    public static WindowId? Hit(IReadOnlyList<TilePlacement> tiles, double x, double y)
    {
        foreach (var tile in tiles)
        {
            var bounds = tile.Bounds;
            if (x >= bounds.X && x < bounds.X + bounds.Width &&
                y >= bounds.Y && y < bounds.Y + bounds.Height)
            {
                return tile.Window;
            }
        }

        return null;
    }
}
