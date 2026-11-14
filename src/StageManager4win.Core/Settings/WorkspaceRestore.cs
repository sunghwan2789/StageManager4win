using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Settings;

public sealed record WorkspacePlan(
    string MonitorId,
    IReadOnlyList<WindowId> Stage,
    IReadOnlyList<WindowId> Strip,
    WindowId? Foreground);

public static class WorkspaceRestore
{
    public static WorkspacePlan? Plan(
        MonitorState current,
        MonitorWorkspace saved,
        IReadOnlyDictionary<WindowId, WindowSnapshot> windows)
    {
        if (saved.Stage is not { Count: > 0 })
        {
            return null;
        }

        var candidates = Candidates(current);
        var staged = new HashSet<WindowId>();
        var stage = new List<WindowId>();
        foreach (var identity in saved.Stage)
        {
            if (Find(candidates, identity, windows, staged) is { } id)
            {
                stage.Add(id);
                staged.Add(id);
            }
        }

        var strip = candidates.Where(id => !staged.Contains(id)).ToList();
        var foreground = saved.Foreground is { } foregroundIdentity
            ? Find(candidates, foregroundIdentity, windows, null)
            : null;
        return new WorkspacePlan(current.MonitorId, stage, strip, foreground);
    }

    private static List<WindowId> Candidates(MonitorState current)
    {
        var candidates = new List<WindowId>();
        if (current.ActiveSet is { } active)
        {
            candidates.AddRange(active.Members.Reverse().Select(key => key.Window));
        }

        foreach (var set in current.StripSets)
        {
            candidates.AddRange(set.Members.Reverse().Select(key => key.Window));
        }

        return candidates.Distinct().ToList();
    }

    private static WindowId? Find(
        IReadOnlyList<WindowId> candidates,
        WindowIdentity identity,
        IReadOnlyDictionary<WindowId, WindowSnapshot> windows,
        HashSet<WindowId>? used)
    {
        foreach (var id in candidates)
        {
            if (used?.Contains(id) == true)
            {
                continue;
            }

            if (windows.TryGetValue(id, out var snapshot)
                && snapshot.AppId == identity.AppId
                && snapshot.Title == identity.Title)
            {
                return id;
            }
        }

        return null;
    }
}
