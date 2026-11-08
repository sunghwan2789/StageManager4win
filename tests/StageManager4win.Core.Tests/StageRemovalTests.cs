using StageManager4win.Core.Input;
using StageManager4win.Core.Stages;

namespace StageManager4win.Core.Tests;

public class StageRemovalTests
{
    [Fact]
    public void ShouldRemove_WhenEveryConditionHolds()
    {
        Assert.True(StageRemoval.ShouldRemove(
            wasOnStage: true, sameMonitor: true,
            startWidth: 800, startHeight: 600,
            endWidth: 800, endHeight: 600,
            cursorOnTile: true));
    }

    [Fact]
    public void ShouldNotRemove_WhenAeroSnapChangedTheSize()
    {
        Assert.False(StageRemoval.ShouldRemove(
            wasOnStage: true, sameMonitor: true,
            startWidth: 800, startHeight: 600,
            endWidth: 960, endHeight: 1040,
            cursorOnTile: true));
    }

    [Fact]
    public void ShouldNotRemove_WhenTheCursorIsNotOnATile()
    {
        Assert.False(StageRemoval.ShouldRemove(
            wasOnStage: true, sameMonitor: true,
            startWidth: 800, startHeight: 600,
            endWidth: 800, endHeight: 600,
            cursorOnTile: false));
    }

    [Fact]
    public void ShouldNotRemove_AcrossMonitors()
    {
        Assert.False(StageRemoval.ShouldRemove(
            wasOnStage: true, sameMonitor: false,
            startWidth: 800, startHeight: 600,
            endWidth: 800, endHeight: 600,
            cursorOnTile: true));
    }

    [Fact]
    public void ShouldNotRemove_WhenTheWindowWasNotOnStage()
    {
        Assert.False(StageRemoval.ShouldRemove(
            wasOnStage: false, sameMonitor: true,
            startWidth: 800, startHeight: 600,
            endWidth: 800, endHeight: 600,
            cursorOnTile: true));
    }

    [Fact]
    public void ShouldNotRemove_WhenTheSizeChangedByMoreThanTheTolerance()
    {
        Assert.False(StageRemoval.ShouldRemove(
            wasOnStage: true, sameMonitor: true,
            startWidth: 800, startHeight: 600,
            endWidth: 803, endHeight: 600,
            cursorOnTile: true));
    }
}
