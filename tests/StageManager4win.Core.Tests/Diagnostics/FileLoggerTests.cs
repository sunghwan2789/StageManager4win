using StageManager4win.Core.Diagnostics;

namespace StageManager4win.Core.Tests.Diagnostics;

public sealed class FileLoggerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "sm4w-logger-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void Write_appends_a_line_with_the_category_and_message()
    {
        var path = Path.Combine(_directory, "logs", "app.log");
        var logger = new FileLogger(path);

        logger.Write("capture", "window=42 hr=0x80004005");

        var text = File.ReadAllText(path);
        Assert.Contains("capture", text);
        Assert.Contains("window=42 hr=0x80004005", text);
        Assert.Single(File.ReadAllLines(path));
    }

    [Fact]
    public void Write_rotates_at_the_limit_and_replaces_the_old_backup()
    {
        var path = Path.Combine(_directory, "app.log");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path + ".1", "stale");
        var logger = new FileLogger(path, maxBytes: 1);

        logger.Write("app", "first");
        logger.Write("app", "second");

        Assert.True(File.Exists(path + ".1"));
        Assert.DoesNotContain("stale", File.ReadAllText(path + ".1"));
        Assert.Contains("first", File.ReadAllText(path + ".1"));
        Assert.Contains("second", File.ReadAllText(path));
    }

    [Fact]
    public void Write_serializes_parallel_writers()
    {
        var path = Path.Combine(_directory, "app.log");
        var logger = new FileLogger(path);

        Parallel.For(0, 200, index => logger.Write("test", $"line={index}"));

        var lines = File.ReadAllLines(path);
        Assert.Equal(200, lines.Length);
        Assert.Equal(200, lines.Distinct().Count());
    }

    [Fact]
    public void Write_does_not_throw_when_the_file_cannot_be_opened()
    {
        Directory.CreateDirectory(_directory);
        var logger = new FileLogger(_directory);

        logger.Write("test", "message");

        Assert.True(Directory.Exists(_directory));
    }
}
