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
}
