using StageManager4win.Core.Layout;
using Xunit;

namespace StageManager4win.Core.Tests;

public class MonitorKeyTests
{
    [Fact]
    public void FromDeviceId_StripsTheInterfaceGuid()
    {
        var path = @"\\?\DISPLAY#DELA06E#5&2e2fefea&0&UID1078018#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

        Assert.Equal(
            @"\\?\DISPLAY#DELA06E#5&2e2fefea&0&UID1078018",
            MonitorKey.FromDeviceId(path, @"\\.\DISPLAY1"));
    }

    [Fact]
    public void FromDeviceId_KeepsAPathWithoutTheGuid()
    {
        var path = @"\\?\DISPLAY#DELA06E#5&2e2fefea&0&UID1078018";

        Assert.Equal(path, MonitorKey.FromDeviceId(path, @"\\.\DISPLAY1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromDeviceId_FallsBackToTheGdiName(string? deviceId)
    {
        Assert.Equal(@"\\.\DISPLAY1", MonitorKey.FromDeviceId(deviceId, @"\\.\DISPLAY1"));
    }
}
