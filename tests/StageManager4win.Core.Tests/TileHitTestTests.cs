using StageManager4win.Core.Layout;
using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Tests;

public class TileHitTestTests
{
    private static WindowKey K(long id) => new(new WindowId(id), 1);

    private static CardPlacement Card(long id, RectD bounds, int stackIndex, double scale = 1) =>
        new(K(id), bounds, stackIndex, scale);

    private static TilePlacement Tile(
        RectD union,
        RectD? badge = null,
        params CardPlacement[] cards) =>
        new(
            "set-1",
            union,
            cards,
            badge ?? new RectD(-1000, -1000, 0, 0),
            TileStack.HitBounds(union));

    [Fact]
    public void ListingRow_TakesPrecedence()
    {
        var rows = new[]
        {
            new ListingRowPlacement("set-1", K(9), 2, new RectD(0, 0, 100, 100))
        };
        var tiles = new[] { Tile(new RectD(0, 0, 100, 100), new RectD(0, 0, 30, 30)) };

        var hit = TileHitTest.Hit(tiles, rows, 10, 10);

        Assert.Equal(new HitTarget(HitKind.ListingRow, "set-1", K(9), 2), hit);
    }

    [Fact]
    public void Badge_TakesPrecedenceOverCardsAndBackground()
    {
        var tiles = new[]
        {
            Tile(new RectD(0, 0, 100, 100), new RectD(0, 0, 20, 20), Card(1, new RectD(0, 0, 100, 100), 0))
        };

        var hit = TileHitTest.Hit(tiles, Array.Empty<ListingRowPlacement>(), 10, 10);

        Assert.Equal(new HitTarget(HitKind.Badge, "set-1"), hit);
    }

    [Fact]
    public void Cards_AreTestedFrontFirst()
    {
        var tiles = new[]
        {
            Tile(
                new RectD(0, 0, 100, 100),
                cards: new[]
                {
                    Card(1, new RectD(0, 0, 100, 100), 0),
                    Card(2, new RectD(0, 0, 100, 100), 1)
                })
        };

        var hit = TileHitTest.Hit(tiles, Array.Empty<ListingRowPlacement>(), 10, 10);

        Assert.Equal(new HitTarget(HitKind.Card, "set-1", K(2)), hit);
    }

    [Fact]
    public void BackCard_IsHitWhenTheFrontCardDoesNotCoverThePoint()
    {
        var tiles = new[]
        {
            Tile(
                new RectD(0, 0, 100, 100),
                cards: new[]
                {
                    Card(1, new RectD(0, 0, 100, 100), 0),
                    Card(2, new RectD(50, 50, 20, 20), 1)
                })
        };

        var hit = TileHitTest.Hit(tiles, Array.Empty<ListingRowPlacement>(), 10, 10);

        Assert.Equal(new HitTarget(HitKind.Card, "set-1", K(1)), hit);
    }

    [Fact]
    public void SetBackground_IsTheUnionFallback()
    {
        var tiles = new[] { Tile(new RectD(0, 0, 100, 100)) };

        var hit = TileHitTest.Hit(tiles, Array.Empty<ListingRowPlacement>(), 10, 10);

        Assert.Equal(new HitTarget(HitKind.SetBackground, "set-1"), hit);
    }

    [Fact]
    public void HitBounds_IncludeTheOverflow()
    {
        var tiles = new[] { Tile(new RectD(100, 100, 50, 50)) };

        Assert.Equal(
            new HitTarget(HitKind.SetBackground, "set-1"),
            TileHitTest.Hit(tiles, Array.Empty<ListingRowPlacement>(), 80, 80));
        Assert.Null(TileHitTest.Hit(tiles, Array.Empty<ListingRowPlacement>(), 71, 79));
    }

    [Fact]
    public void Badge_OutsideTheUnionIsStillHit()
    {
        var tiles = new[] { Tile(new RectD(100, 100, 50, 50), new RectD(0, 0, 20, 20)) };

        var hit = TileHitTest.Hit(tiles, Array.Empty<ListingRowPlacement>(), 10, 10);

        Assert.Equal(new HitTarget(HitKind.Badge, "set-1"), hit);
    }

    [Fact]
    public void EmptyOrOutside_ReturnsNull()
    {
        Assert.Null(TileHitTest.Hit(
            Array.Empty<TilePlacement>(), Array.Empty<ListingRowPlacement>(), 1, 1));
        Assert.Null(TileHitTest.Hit(
            new[] { Tile(new RectD(0, 0, 100, 100)) },
            Array.Empty<ListingRowPlacement>(),
            500,
            500));
    }
}
