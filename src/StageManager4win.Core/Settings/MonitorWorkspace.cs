namespace StageManager4win.Core.Settings;

public sealed record MonitorWorkspace(
    string MonitorId,
    IReadOnlyList<WindowIdentity> StripOrder,
    WindowIdentity? Foreground,
    IReadOnlyList<WindowIdentity>? Stage = null);
