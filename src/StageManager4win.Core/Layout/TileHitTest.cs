namespace StageManager4win.Core.Layout;

public static class TileHitTest
{
    public static HitTarget? Hit(
        IReadOnlyList<TilePlacement> tiles,
        IReadOnlyList<ListingRowPlacement> listingRows,
        double x,
        double y)
    {
        foreach (var row in listingRows)
        {
            if (Contains(row.BoundsDip, x, y))
            {
                return new HitTarget(HitKind.ListingRow, row.SetId, row.Window, row.RowIndex);
            }
        }

        foreach (var tile in tiles)
        {
            if (Contains(tile.BadgeBoundsDip, x, y))
            {
                return new HitTarget(HitKind.Badge, tile.SetId);
            }
        }

        foreach (var tile in tiles)
        {
            for (var index = tile.Cards.Count - 1; index >= 0; index--)
            {
                var card = tile.Cards[index];
                if (Contains(card.BoundsDip, x, y))
                {
                    return new HitTarget(HitKind.Card, tile.SetId, card.Window);
                }
            }
        }

        foreach (var tile in tiles)
        {
            if (Contains(tile.HitBoundsDip, x, y))
            {
                return new HitTarget(HitKind.SetBackground, tile.SetId);
            }
        }

        return null;
    }

    private static bool Contains(RectD bounds, double x, double y) =>
        x >= bounds.X && x < bounds.X + bounds.Width &&
        y >= bounds.Y && y < bounds.Y + bounds.Height;
}
