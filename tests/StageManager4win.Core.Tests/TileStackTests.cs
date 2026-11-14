using StageManager4win.Core.Layout;

namespace StageManager4win.Core.Tests;

public class TileStackTests
{
    [Fact]
    public void Step_SingleCard_IsZero()
    {
        Assert.Equal(0, TileStack.Step(1, 160));
        Assert.Equal(0, TileStack.Step(0, 160));
    }

    [Fact]
    public void Step_TwoCards_ClampsToTheMaximum()
    {
        Assert.Equal(TileStack.MaxCardStep, TileStack.Step(2, 100));
    }

    [Fact]
    public void Step_ManyCards_ClampsToTheMinimum()
    {
        Assert.Equal(TileStack.MinCardStep, TileStack.Step(100, 160));
    }

    [Fact]
    public void Step_UsesTheUnionWidthCap()
    {
        Assert.Equal(200.0 / 19.0, TileStack.Step(20, 100), 6);
    }

    [Fact]
    public void Scale_BackCardsRecedeAndHitTheFloor()
    {
        Assert.Equal(0.9409, TileStack.Scale(0, 3), 6);
        Assert.Equal(0.97, TileStack.Scale(1, 3), 6);
        Assert.Equal(TileStack.MinDepthScale, TileStack.Scale(0, 20), 6);
    }

    [Fact]
    public void Scale_FrontCardIsFullSize()
    {
        Assert.Equal(1, TileStack.Scale(4, 5));
    }

    [Fact]
    public void UnionBounds_UseTheFanWidthAndMiddleY()
    {
        var union = TileStack.UnionBounds(3, 100, 80, 10, 100);

        Assert.Equal(10, union.X);
        Assert.Equal(60, union.Y);
        Assert.Equal(128, union.Width);
        Assert.Equal(80, union.Height);
    }

    [Fact]
    public void CardBounds_Left_SharedMiddleAndFrontExtendsInward()
    {
        var back = TileStack.CardBounds(0, 3, 100, 80, 10, 100, StripEdge.Left);
        var front = TileStack.CardBounds(2, 3, 100, 80, 10, 100, StripEdge.Left);

        Assert.Equal(100, back.Y + back.Height / 2, 6);
        Assert.Equal(100, front.Y + front.Height / 2, 6);
        Assert.Equal(10, back.X, 6);
        Assert.Equal(38, front.X, 6);
        Assert.Equal(100, front.Width, 6);
        Assert.True(back.Width < front.Width);
        Assert.True(front.X + front.Width > back.X + back.Width);
    }

    [Fact]
    public void CardBounds_Right_MirrorsTheFanAtTheUnionRight()
    {
        var back = TileStack.CardBounds(0, 3, 100, 80, 10, 100, StripEdge.Right);
        var front = TileStack.CardBounds(2, 3, 100, 80, 10, 100, StripEdge.Right);

        Assert.Equal(100, back.Y + back.Height / 2, 6);
        Assert.Equal(10, front.X, 6);
        Assert.Equal(138, back.X + back.Width, 6);
        Assert.True(front.X < back.X);
        Assert.True(back.X + back.Width > front.X + front.Width);
    }

    [Fact]
    public void HitBounds_ExpandsUpAndSideways()
    {
        var bounds = TileStack.HitBounds(new RectD(100, 200, 160, 120));

        Assert.Equal(72, bounds.X);
        Assert.Equal(180, bounds.Y);
        Assert.Equal(216, bounds.Width);
        Assert.Equal(148, bounds.Height);
    }
}
