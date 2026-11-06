using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;
using Xunit;

namespace StageManager4win.Core.Tests;

public class CycleCursorTests
{
    private static readonly WindowId W1 = new(1);
    private static readonly WindowId W2 = new(2);
    private static readonly WindowId W3 = new(3);

    [Fact]
    public void Advance_Forward_StartsAtFirstItem_ThenWraps()
    {
        var cursor = new CycleCursor(new[] { W1, W2, W3 });

        Assert.Equal(W1, cursor.Advance(1, _ => true));
        Assert.Equal(W2, cursor.Advance(1, _ => true));
        Assert.Equal(W3, cursor.Advance(1, _ => true));
        Assert.Equal(W1, cursor.Advance(1, _ => true));
    }

    [Fact]
    public void Advance_Backward_StartsAtLastItem()
    {
        var cursor = new CycleCursor(new[] { W1, W2, W3 });

        Assert.Equal(W3, cursor.Advance(-1, _ => true));
        Assert.Equal(W2, cursor.Advance(-1, _ => true));
    }

    [Fact]
    public void Advance_SkipsIneligibleItems()
    {
        var cursor = new CycleCursor(new[] { W1, W2, W3 });
        cursor.Advance(1, _ => true);

        Assert.Equal(W3, cursor.Advance(1, id => id != W2));
    }

    [Fact]
    public void Advance_WhenNothingIsEligible_ReturnsNullAndKeepsCurrent()
    {
        var cursor = new CycleCursor(new[] { W1, W2 });
        cursor.Advance(1, _ => true);

        Assert.Null(cursor.Advance(1, _ => false));
        Assert.Equal(W1, cursor.Current);
    }

    [Fact]
    public void Advance_EmptyOrder_ReturnsNull()
    {
        var cursor = new CycleCursor(Array.Empty<WindowId>());

        Assert.Null(cursor.Advance(1, _ => true));
        Assert.Null(cursor.Current);
    }
}
