namespace StageManager4win.Core.Layout;

public static class MonitorKey
{
    // The monitor interface path ends with "#{interface-class-guid}". Strip it so
    // the key is stable and readable; an empty path falls back to the GDI name.
    public static string FromDeviceId(string? deviceId, string gdiDeviceName)
    {
        var path = deviceId?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            return gdiDeviceName;
        }

        var suffix = path.LastIndexOf("#{", StringComparison.Ordinal);
        return suffix >= 0 ? path[..suffix] : path;
    }
}
