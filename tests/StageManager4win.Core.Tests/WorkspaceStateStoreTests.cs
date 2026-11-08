using StageManager4win.Core.Settings;
using Xunit;

namespace StageManager4win.Core.Tests;

public sealed class WorkspaceStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sm-tests-" + Guid.NewGuid().ToString("N"));

    public WorkspaceStateStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new WorkspaceStateStore(_dir);
        var state = new WorkspaceState(new[]
        {
            new MonitorWorkspace("\\\\.\\DISPLAY1",
                new[] { new WindowIdentity("app.one", "One"), new WindowIdentity("app.two", "Two") },
                new WindowIdentity("app.two", "Two"))
        });

        store.Save(state);
        var loaded = store.Load();

        var monitor = Assert.Single(loaded.Monitors);
        Assert.Equal("\\\\.\\DISPLAY1", monitor.MonitorId);
        Assert.Equal(new[] { "app.one", "app.two" }, monitor.StripOrder.Select(w => w.AppId).ToArray());
        Assert.Equal("app.two", monitor.Foreground?.AppId);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty()
    {
        var store = new WorkspaceStateStore(_dir);
        File.WriteAllText(store.FilePath, "nope");

        var loaded = store.Load();

        Assert.Empty(loaded.Monitors);
    }

    [Fact]
    public void Stage_RoundTrips()
    {
        var store = new WorkspaceStateStore(_dir);
        store.Save(new WorkspaceState(new[]
        {
            new MonitorWorkspace(
                "M1",
                new[] { new WindowIdentity("A", "a1") },
                new WindowIdentity("B", "b1"),
                new[] { new WindowIdentity("B", "b1") })
        }));

        var loaded = store.Load();
        var monitor = Assert.Single(loaded.Monitors);
        Assert.NotNull(monitor.Stage);
        Assert.Single(monitor.Stage!);
        Assert.Equal("B", monitor.Stage![0].AppId);
    }

    [Fact]
    public void Load_WithoutStage_LeavesItNull()
    {
        File.WriteAllText(
            Path.Combine(_dir, "workspace.json"),
            "{\"monitors\":[{\"monitorId\":\"M1\",\"stripOrder\":[],\"foreground\":null}]}");

        var monitor = Assert.Single(new WorkspaceStateStore(_dir).Load().Monitors);

        Assert.Null(monitor.Stage);
    }
}
