namespace StageManager4win.Core.Stages;

public enum ActivationSource
{
    User,
    Restore,
    Programmatic,
    Passive
}

public enum RemoveReason
{
    Closed,
    Minimized,
    ShiftAdded,
    MonitorChanged
}

public enum ActivationOutcome
{
    Changed,
    CreatedSet,
    PromotedSet,
    Failure
}

public sealed record ActivationResult(
    ActivationOutcome Outcome,
    string? SetId = null,
    string? DemotedSetId = null)
{
    public static ActivationResult Failure { get; } = new(ActivationOutcome.Failure);
}

public sealed record MonitorChange(
    WindowKey Window,
    string FromMonitorId,
    string ToMonitorId);
