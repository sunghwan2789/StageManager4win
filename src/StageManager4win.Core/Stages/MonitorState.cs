namespace StageManager4win.Core.Stages;

public sealed record MonitorState(
    string MonitorId,
    StageSet? ActiveSet,
    IReadOnlyList<StageSet> StripSets);
