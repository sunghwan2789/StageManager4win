using StageManager4win.Core.Windows;
using Xunit;

namespace StageManager4win.Core.Tests;

public class WindowFilterTests
{
    private static readonly WindowTraits Candidate = new(
        IsTopLevel: true, IsVisible: true, IsToolWindow: false, IsNoActivate: false,
        IsTopmost: false, IsCloaked: false, HasOwner: false, IsShellWindow: false,
        IsOwnProcess: false, HasNonZeroSize: true, IsFullscreen: false);

    [Fact]
    public void Candidate_IsAccepted() => Assert.True(WindowFilter.IsCandidate(Candidate));

    [Fact]
    public void NotTopLevel_IsRejected() => Assert.False(WindowFilter.IsCandidate(Candidate with { IsTopLevel = false }));

    [Fact]
    public void NotVisible_IsRejected() => Assert.False(WindowFilter.IsCandidate(Candidate with { IsVisible = false }));

    [Fact]
    public void ToolWindow_IsRejected() => Assert.False(WindowFilter.IsCandidate(Candidate with { IsToolWindow = true }));

    [Fact]
    public void NoActivate_IsRejected() => Assert.False(WindowFilter.IsCandidate(Candidate with { IsNoActivate = true }));

    [Fact]
    public void Topmost_IsRejected() => Assert.False(WindowFilter.IsCandidate(Candidate with { IsTopmost = true }));

    [Fact]
    public void Cloaked_IsRejected() => Assert.False(WindowFilter.IsCandidate(Candidate with { IsCloaked = true }));

    [Fact]
    public void OwnedWindow_IsRejected() => Assert.False(WindowFilter.IsCandidate(Candidate with { HasOwner = true }));

    [Fact]
    public void ShellWindow_IsRejected() => Assert.False(WindowFilter.IsCandidate(Candidate with { IsShellWindow = true }));

    [Fact]
    public void OwnProcess_IsRejected() => Assert.False(WindowFilter.IsCandidate(Candidate with { IsOwnProcess = true }));

    [Fact]
    public void ZeroSize_IsRejected() => Assert.False(WindowFilter.IsCandidate(Candidate with { HasNonZeroSize = false }));

    [Fact]
    public void Fullscreen_IsRejected() => Assert.False(WindowFilter.IsCandidate(Candidate with { IsFullscreen = true }));
}
