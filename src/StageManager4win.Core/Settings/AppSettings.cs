using StripEdges = StageManager4win.Core.Layout.StripEdge;
using WindowModes = StageManager4win.Core.Stages.WindowMode;

namespace StageManager4win.Core.Settings;

public sealed record AppSettings
{
    public bool Enabled { get; init; } = true;
    public WindowModes WindowMode { get; init; } = WindowModes.OneAtATime;
    public bool AutoHide { get; init; }
    public StripEdges StripEdge { get; init; } = StripEdges.Left;
    public int PreviewFps { get; init; } = 30;
    public HotkeySettings Hotkeys { get; init; } = HotkeySettings.Default;
    public IReadOnlyList<string> ExcludedAppIds { get; init; } = Array.Empty<string>();

    public AppSettings Normalize() => this with
    {
        WindowMode = Enum.IsDefined(WindowMode) ? WindowMode : WindowModes.OneAtATime,
        StripEdge = Enum.IsDefined(StripEdge) ? StripEdge : StripEdges.Left,
        PreviewFps = NormalizeFps(PreviewFps),
        Hotkeys = Hotkeys ?? HotkeySettings.Default,
        ExcludedAppIds = (ExcludedAppIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToArray()
    };

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
