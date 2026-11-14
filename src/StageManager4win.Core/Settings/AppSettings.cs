using StripEdges = StageManager4win.Core.Layout.StripEdge;
using WindowModes = StageManager4win.Core.Stages.WindowMode;

namespace StageManager4win.Core.Settings;

public sealed record AppSettings
{
    public bool Enabled { get; init; } = true;
    public WindowModes WindowMode { get; init; } = WindowModes.OneAtATime;
    public StripEdges StripEdge { get; init; } = StripEdges.Left;
    public int PreviewFps { get; init; } = 30;
    public HotkeySettings Hotkeys { get; init; } = HotkeySettings.Default;
    public IReadOnlyList<string> ExcludedAppIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MonitorSettings> Monitors { get; init; }
        = Array.Empty<MonitorSettings>();

    public AppSettings Normalize() => this with
    {
        WindowMode = Enum.IsDefined(WindowMode) ? WindowMode : WindowModes.OneAtATime,
        StripEdge = Enum.IsDefined(StripEdge) ? StripEdge : StripEdges.Left,
        PreviewFps = NormalizeFps(PreviewFps),
        Hotkeys = Hotkeys ?? HotkeySettings.Default,
        ExcludedAppIds = (ExcludedAppIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToArray(),
        Monitors = NormalizeMonitors(Monitors)
    };

    public MonitorSettings? ForMonitor(string monitorId) =>
        Monitors.FirstOrDefault(entry =>
            string.Equals(entry.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));

    public bool IsManaged(string monitorId) => ForMonitor(monitorId)?.Managed ?? true;

    public StripEdges EdgeFor(string monitorId) =>
        ForMonitor(monitorId)?.StripEdge ?? StripEdge;

    private static MonitorSettings[] NormalizeMonitors(
        IReadOnlyList<MonitorSettings>? monitors)
    {
        var result = new List<MonitorSettings>();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in monitors ?? Array.Empty<MonitorSettings>())
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.MonitorId))
            {
                continue;
            }

            var normalized = entry with
            {
                MonitorId = entry.MonitorId.Trim(),
                StripEdge = entry.StripEdge is { } edge && Enum.IsDefined(edge) ? edge : null
            };
            if (indexes.TryGetValue(normalized.MonitorId, out var index))
            {
                result[index] = normalized;
            }
            else
            {
                indexes[normalized.MonitorId] = result.Count;
                result.Add(normalized);
            }
        }

        return result.ToArray();
    }

    private static int NormalizeFps(int fps)
    {
        if (fps <= 0)
        {
            return 30;
        }

        var nearest = 15;
        foreach (var candidate in new[] { 30, 60 })
        {
            if (Math.Abs(candidate - fps) < Math.Abs(nearest - fps))
            {
                nearest = candidate;
            }
        }

        return nearest;
    }
}
