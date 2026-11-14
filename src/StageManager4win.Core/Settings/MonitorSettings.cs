using StripEdges = StageManager4win.Core.Layout.StripEdge;

namespace StageManager4win.Core.Settings;

public sealed record MonitorSettings(
    string MonitorId,
    bool Managed = true,
    StripEdges? StripEdge = null);
