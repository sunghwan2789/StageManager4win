using StageManager4win.Core.Input;

namespace StageManager4win.Core.Tests;

public class DesktopClickTests
{
    [Fact]
    public void IsToggle_AcceptsAShortStillClick()
    {
        Assert.True(DesktopClick.IsToggle(3, 2, 300));
    }

    [Fact]
    public void IsToggle_RejectsADrag()
    {
        Assert.False(DesktopClick.IsToggle(5, 0, 100));
    }

    [Fact]
    public void IsToggle_RejectsASlowPress()
    {
        Assert.False(DesktopClick.IsToggle(0, 0, 501));
    }

    [Fact]
    public void IsDesktopClass_MatchesTheShellWindows()
    {
        Assert.True(DesktopClick.IsDesktopClass("Progman"));
        Assert.True(DesktopClick.IsDesktopClass("WorkerW"));
        Assert.False(DesktopClick.IsDesktopClass("SHELLDLL_DefView"));
        Assert.False(DesktopClick.IsDesktopClass("Shell_TrayWnd"));
    }
}
