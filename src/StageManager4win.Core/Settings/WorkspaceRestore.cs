using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Settings;

public static class WorkspaceRestore
{
    public static MonitorState? Plan(
        MonitorState current,
        MonitorWorkspace saved,
        IReadOnlyDictionary<WindowId, WindowSnapshot> windows)
    {
        if (saved.Stage is not { Count: > 0 })
        {
            return null;
        }

        // Current stage windows may be missing from a saved strip, so both lists are candidates.
        var candidates = current.Stage.Concat(current.Strip).Distinct().ToList();
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
        var activeAppId = foreground is { } window && windows.TryGetValue(window, out var snapshot)
            ? snapshot.AppId
            : null;
        return new MonitorState(current.MonitorId, stage, strip, foreground, activeAppId);
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
