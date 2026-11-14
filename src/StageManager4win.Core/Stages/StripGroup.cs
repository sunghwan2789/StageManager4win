namespace StageManager4win.Core.Stages;

public sealed record StripGroup(
    string SetId,
    WindowKey Front,
    IReadOnlyList<WindowKey> Members);
