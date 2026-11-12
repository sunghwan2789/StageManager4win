using StageManager4win.Core.Layout;
using StageManager4win.Core.Settings;
using StageManager4win.Core.Stages;
using Xunit;

namespace StageManager4win.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sm-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new SettingsStore(_dir);

        var settings = store.Load();

        Assert.True(settings.Enabled);
        Assert.Equal(WindowMode.OneAtATime, settings.WindowMode);
        Assert.Equal(StripEdge.Left, settings.StripEdge);
        Assert.Equal(30, settings.PreviewFps);
        Assert.Equal(HotkeySettings.Default, settings.Hotkeys);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = new SettingsStore(_dir);
        var settings = new AppSettings
        {
            Enabled = false,
            WindowMode = WindowMode.AllAtOnce,
            AutoHide = true,
            StripEdge = StripEdge.Right,
            PreviewFps = 60,
            Hotkeys = new HotkeySettings(new(3, 0x41), new(3, 0x42), new(3, 0x43), new(3, 0x44)),
            ExcludedAppIds = new[] { "app.one", "app.two" }
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(settings.Enabled, loaded.Enabled);
        Assert.Equal(settings.WindowMode, loaded.WindowMode);
        Assert.Equal(settings.AutoHide, loaded.AutoHide);
        Assert.Equal(settings.StripEdge, loaded.StripEdge);
        Assert.Equal(settings.PreviewFps, loaded.PreviewFps);
        Assert.Equal(settings.Hotkeys, loaded.Hotkeys);
        Assert.Equal(settings.ExcludedAppIds, loaded.ExcludedAppIds);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsAndKeepsBackup()
    {
        var store = new SettingsStore(_dir);
        File.WriteAllText(store.FilePath, "{ not json");

        var loaded = store.Load();

        Assert.True(loaded.Enabled);
        Assert.True(File.Exists(store.FilePath + ".bak"));
    }

    [Fact]
    public void Normalize_ClampsInvalidValues()
    {
        var normalized = new AppSettings
        {
            PreviewFps = 24,
            ExcludedAppIds = new[] { "", "  ", "app.one" }
        }.Normalize();

        Assert.Equal(30, normalized.PreviewFps);
        Assert.Equal(new[] { "app.one" }, normalized.ExcludedAppIds);
    }

    [Fact]
    public void Save_LeavesNoTempFile()
    {
        var store = new SettingsStore(_dir);
        store.Save(new AppSettings());

        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsMonitorSettings()
    {
        var store = new SettingsStore(_dir);
        var settings = new AppSettings
        {
            Monitors = new[]
            {
                new MonitorSettings(@"\\?\DISPLAY#ONE", Managed: false),
                new MonitorSettings(@"\\?\DISPLAY#TWO", StripEdge: StripEdge.Right, AutoHide: true)
            }
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(settings.Monitors, loaded.Monitors);
        Assert.Contains("\"monitors\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Normalize_CleansMonitorEntries()
    {
        var nullList = new AppSettings { Monitors = null! }.Normalize();
        Assert.Empty(nullList.Monitors);

        var normalized = new AppSettings
        {
            Monitors = new[]
            {
                new MonitorSettings("  "),
                new MonitorSettings(@"  \\.\DISPLAY1  ", Managed: true, StripEdge: StripEdge.Left),
                new MonitorSettings(@"\\.\DISPLAY1", Managed: false, StripEdge: (StripEdge)99),
                new MonitorSettings(@"\\.\DISPLAY2", StripEdge: StripEdge.Right),
                new MonitorSettings(@"\\.\DISPLAY2", StripEdge: StripEdge.Left)
            }
        }.Normalize();

        Assert.Equal(2, normalized.Monitors.Count);
        Assert.Equal(@"\\.\DISPLAY1", normalized.Monitors[0].MonitorId);
        Assert.False(normalized.Monitors[0].Managed);
        Assert.Null(normalized.Monitors[0].StripEdge);
        Assert.Equal(@"\\.\DISPLAY2", normalized.Monitors[1].MonitorId);
        Assert.Equal(StripEdge.Left, normalized.Monitors[1].StripEdge);
    }

    [Fact]
    public void MonitorResolvers_UseDefaultsAndOverrides()
    {
        var settings = new AppSettings
        {
            StripEdge = StripEdge.Left,
            AutoHide = false,
            Monitors = new[]
            {
                new MonitorSettings("A", Managed: false, StripEdge: StripEdge.Right, AutoHide: true),
                new MonitorSettings("B")
            }
        };

        Assert.False(settings.IsManaged("a"));
        Assert.True(settings.IsManaged("missing"));
        Assert.Equal(StripEdge.Right, settings.EdgeFor("A"));
        Assert.Equal(StripEdge.Left, settings.EdgeFor("missing"));
        Assert.True(settings.AutoHideFor("A"));
        Assert.False(settings.AutoHideFor("missing"));
        Assert.Null(settings.ForMonitor("missing"));
    }
}
