using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;
using Xunit;

namespace StageManager4win.Core.Tests;

public class StageModelTests
{
    private static WindowSnapshot W(long id, string app, string monitor = "M1", bool minimized = false) =>
        new(new WindowId(id), (int)id, app, app, $"window {id}", null,
            new WindowBounds(0, 0, 800, 600), monitor,
            IsVisible: true, IsMinimized: minimized, IsCloaked: false, IsFullscreen: false);

    private static void AssertOrder(IEnumerable<WindowId> expected, IEnumerable<WindowId> actual) =>
        Assert.Equal(expected.ToArray(), actual.ToArray());

    [Fact]
    public void Reset_WithoutForeground_SeedsStripInInputOrder()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") });

        var m = model.Monitors["M1"];
        AssertOrder(new[] { new WindowId(1), new WindowId(2) }, m.Strip);
        Assert.Empty(m.Stage);
        Assert.Null(m.Foreground);
        Assert.Null(m.ActiveAppId);
    }

    [Fact]
    public void Reset_WithForeground_StagesOnlyThatWindow()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "A") }, new WindowId(1));

        var m = model.Monitors["M1"];
        AssertOrder(new[] { new WindowId(1) }, m.Stage);
        AssertOrder(new[] { new WindowId(2) }, m.Strip);
        Assert.Equal("A", m.ActiveAppId);
        Assert.Equal(new WindowId(1), m.Foreground);
    }

    [Fact]
    public void SetForeground_MovesPreviousStageToTopOfStrip()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        Assert.True(model.SetForeground(new WindowId(2)));

        var m = model.Monitors["M1"];
        AssertOrder(new[] { new WindowId(2) }, m.Stage);
        AssertOrder(new[] { new WindowId(1) }, m.Strip);
    }

    [Fact]
    public void SetForeground_RemovesTheNewStageWindowFromTheStrip()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(1));

        model.SetForeground(new WindowId(2));

        var m = model.Monitors["M1"];
        AssertOrder(new[] { new WindowId(2) }, m.Stage);
        AssertOrder(new[] { new WindowId(1), new WindowId(3) }, m.Strip);
    }

    [Fact]
    public void SetForeground_LeavesOtherMonitorsUnchanged()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A", "M1"), W(2, "B", "M2") }, new WindowId(1));

        model.SetForeground(new WindowId(2));

        AssertOrder(new[] { new WindowId(1) }, model.Monitors["M1"].Stage);
        AssertOrder(new[] { new WindowId(2) }, model.Monitors["M2"].Stage);
    }

    [Fact]
    public void SetForeground_UnknownWindow_ReturnsFalse()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A") });

        Assert.False(model.SetForeground(new WindowId(99)));
    }

    [Fact]
    public void SetForeground_SameWindow_IsIdempotent()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        Assert.True(model.SetForeground(new WindowId(1)));
        AssertOrder(new[] { new WindowId(1) }, model.Monitors["M1"].Stage);
        AssertOrder(new[] { new WindowId(2) }, model.Monitors["M1"].Strip);
    }

    [Fact]
    public void AllAtOnce_StagesEveryAppWindowOnThatMonitor()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "A", "M2"), W(4, "B") }, new WindowId(1));

        AssertOrder(new[] { new WindowId(1), new WindowId(2) }, model.Monitors["M1"].Stage);
        AssertOrder(new[] { new WindowId(4) }, model.Monitors["M1"].Strip);
        AssertOrder(new[] { new WindowId(3) }, model.Monitors["M2"].Strip);
    }

    [Fact]
    public void AllAtOnce_ForegroundingAnotherApp_StagesThatAppsWindows()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B"), W(4, "B") }, new WindowId(1));

        model.SetForeground(new WindowId(4));

        AssertOrder(new[] { new WindowId(4), new WindowId(3) }, model.Monitors["M1"].Stage);
        AssertOrder(new[] { new WindowId(1), new WindowId(2) }, model.Monitors["M1"].Strip);
    }

    [Fact]
    public void Automatic_BehavesLikeOneAtATime()
    {
        var model = new StageModel(WindowMode.Automatic);
        model.Reset(new[] { W(1, "A"), W(2, "A") }, new WindowId(1));

        AssertOrder(new[] { new WindowId(1) }, model.Monitors["M1"].Stage);
        AssertOrder(new[] { new WindowId(2) }, model.Monitors["M1"].Strip);
    }

    [Fact]
    public void SetMode_AllAtOnce_ExpandsTheCurrentStage()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(1));

        model.SetMode(WindowMode.AllAtOnce);

        AssertOrder(new[] { new WindowId(1), new WindowId(2) }, model.Monitors["M1"].Stage);
        AssertOrder(new[] { new WindowId(3) }, model.Monitors["M1"].Strip);
    }

    [Fact]
    public void SetMode_OneAtATime_ShrinksTheStageToTheForeground()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(1));

        model.SetMode(WindowMode.OneAtATime);

        AssertOrder(new[] { new WindowId(1) }, model.Monitors["M1"].Stage);
        AssertOrder(new[] { new WindowId(2), new WindowId(3) }, model.Monitors["M1"].Strip);
    }

    [Fact]
    public void Add_NewWindowOfActiveApp_AllAtOnce_JoinsTopOfStage()
    {
        var model = new StageModel(WindowMode.AllAtOnce);
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        model.Add(W(3, "A"));

        AssertOrder(new[] { new WindowId(3), new WindowId(1) }, model.Monitors["M1"].Stage);
        AssertOrder(new[] { new WindowId(2) }, model.Monitors["M1"].Strip);
    }

    [Fact]
    public void Add_BackgroundWindow_JoinsTopOfStrip()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        model.Add(W(3, "C"));

        AssertOrder(new[] { new WindowId(3), new WindowId(2) }, model.Monitors["M1"].Strip);
    }

    [Fact]
    public void Minimize_StageWindow_MovesToTopOfStrip_AndClearsForeground()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "A") }, new WindowId(1));

        model.Minimize(new WindowId(1));

        var m = model.Monitors["M1"];
        Assert.Empty(m.Stage);
        AssertOrder(new[] { new WindowId(1), new WindowId(2) }, m.Strip);
        Assert.Null(m.Foreground);
        Assert.Equal("A", m.ActiveAppId);
    }

    [Fact]
    public void Minimize_StripWindow_KeepsItsPosition()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        model.Minimize(new WindowId(2));

        AssertOrder(new[] { new WindowId(2) }, model.Monitors["M1"].Strip);
    }

    [Fact]
    public void Remove_ForegroundWindow_ClearsForeground()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        model.Remove(new WindowId(1));

        var m = model.Monitors["M1"];
        Assert.Empty(m.Stage);
        Assert.Null(m.Foreground);
        Assert.Null(m.ActiveAppId);
    }

    [Fact]
    public void Update_MovedToAnotherMonitor_BackfillsOldStageFromStripTop()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

        model.Update(W(1, "A", "M2"));

        var old = model.Monitors["M1"];
        AssertOrder(new[] { new WindowId(2) }, old.Stage);
        Assert.Empty(old.Strip);
        Assert.Equal("B", old.ActiveAppId);
        Assert.Null(old.Foreground);
        AssertOrder(new[] { new WindowId(1) }, model.Monitors["M2"].Strip);
    }

    [Fact]
    public void Restore_ClearsMinimizedFlagWithoutMovingTheWindow()
    {
        var model = new StageModel();
        model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));
        model.Minimize(new WindowId(2));

        model.Restore(new WindowId(2));

        Assert.False(model.Windows[new WindowId(2)].IsMinimized);
        AssertOrder(new[] { new WindowId(2) }, model.Monitors["M1"].Strip);
    }
}
