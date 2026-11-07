using StageManager4win.Core.Capture;
using StageManager4win.Core.Windows;
using Xunit;

namespace StageManager4win.Core.Tests;

public class CaptureLeaseTrackerTests
{
    private static readonly WindowId W1 = new(1);
    private static readonly WindowId W2 = new(2);
    private static readonly WindowId W3 = new(3);

    [Fact]
    public void FirstReconcile_AddsEveryRequestedWindowInOrder()
    {
        var tracker = new CaptureLeaseTracker();

        var change = tracker.Reconcile(new[] { W2, W1 });

        Assert.Equal(new[] { W2, W1 }, change.Added);
        Assert.Empty(change.Removed);
        Assert.Equal(new[] { W2, W1 }, tracker.Active);
    }

    [Fact]
    public void UnchangedRequest_ChangesNothing()
    {
        var tracker = new CaptureLeaseTracker();
        tracker.Reconcile(new[] { W1, W2 });

        var change = tracker.Reconcile(new[] { W1, W2 });

        Assert.Empty(change.Added);
        Assert.Empty(change.Removed);
    }

    [Fact]
    public void DroppingAWindow_RemovesOnlyThatWindow()
    {
        var tracker = new CaptureLeaseTracker();
        tracker.Reconcile(new[] { W1, W2, W3 });

        var change = tracker.Reconcile(new[] { W3, W1 });

        Assert.Empty(change.Added);
        Assert.Equal(new[] { W2 }, change.Removed);
        Assert.Equal(new[] { W3, W1 }, tracker.Active);
    }

    [Fact]
    public void DuplicateRequests_AreCollapsed()
    {
        var tracker = new CaptureLeaseTracker();

        var change = tracker.Reconcile(new[] { W1, W1, W2, W1 });

        Assert.Equal(new[] { W1, W2 }, change.Added);
        Assert.Equal(new[] { W1, W2 }, tracker.Active);
    }

    [Fact]
    public void Clear_RemovesEveryLease()
    {
        var tracker = new CaptureLeaseTracker();
        tracker.Reconcile(new[] { W1, W2 });

        var change = tracker.Clear();

        Assert.Equal(new[] { W1, W2 }, change.Removed);
        Assert.Empty(tracker.Active);
        Assert.Empty(tracker.Clear().Removed);
    }
}
