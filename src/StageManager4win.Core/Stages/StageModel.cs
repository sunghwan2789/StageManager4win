using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Stages;

public sealed class StageModel
{
    private readonly Dictionary<WindowId, WindowSnapshot> _windows = new();
    private readonly Dictionary<string, MonitorData> _monitors = new(StringComparer.Ordinal);
    private WindowMode _mode;
    private long _nextSetId;

    public StageModel(WindowMode mode = WindowMode.OneAtATime)
    {
        _mode = mode;
    }

    public IReadOnlyDictionary<WindowId, WindowSnapshot> Windows => _windows;

    public IReadOnlyDictionary<string, MonitorState> Monitors =>
        _monitors.ToDictionary(
            pair => pair.Key,
            pair => new MonitorState(pair.Key, pair.Value.ActiveSet, pair.Value.StripSets.ToArray()),
            StringComparer.Ordinal);

    public WindowMode Mode => _mode;

    public void Reset(IEnumerable<WindowSnapshot> windows, WindowId? foreground = null)
    {
        _windows.Clear();
        _monitors.Clear();
        var ordered = windows.ToArray();
        foreach (var snapshot in ordered)
        {
            _windows[snapshot.Id] = snapshot;
            EnsureMonitor(snapshot.MonitorId).StripSets.Add(Singleton(WindowKey.For(snapshot)));
        }

        if (foreground is not { } foregroundId
            || !_windows.TryGetValue(foregroundId, out var foregroundSnapshot))
        {
            return;
        }

        var foregroundKey = WindowKey.For(foregroundSnapshot);
        var participants = new List<WindowKey> { foregroundKey };
        if (_mode == WindowMode.AllAtOnce)
        {
            foreach (var snapshot in ordered)
            {
                if (snapshot.Id == foregroundSnapshot.Id
                    || snapshot.MonitorId != foregroundSnapshot.MonitorId
                    || snapshot.AppId != foregroundSnapshot.AppId
                    || !snapshot.IsVisible
                    || snapshot.IsMinimized)
                {
                    continue;
                }

                participants.Add(WindowKey.For(snapshot));
            }
        }

        var monitor = EnsureMonitor(foregroundSnapshot.MonitorId);
        RemoveFromStrip(monitor, participants);
        var members = participants.Skip(1).Concat(participants.Take(1)).ToArray();
        monitor.ActiveSet = new StageSet(NewSetId(), members, foregroundKey, EmptyGeometry());
    }

    public void Add(WindowSnapshot snapshot)
    {
        if (_windows.ContainsKey(snapshot.Id))
        {
            Update(snapshot);
            return;
        }

        _windows[snapshot.Id] = snapshot;
        EnsureMonitor(snapshot.MonitorId).StripSets.Add(Singleton(WindowKey.For(snapshot)));
    }

    public MonitorChange? Update(WindowSnapshot snapshot)
    {
        if (!_windows.TryGetValue(snapshot.Id, out var existing))
        {
            Add(snapshot);
            return null;
        }

        _windows[snapshot.Id] = snapshot;
        if (existing.ProcessId != snapshot.ProcessId)
        {
            var oldKey = WindowKey.For(existing);
            if (Locate(oldKey) is { } located)
            {
                RemoveKey(located.Monitor, oldKey, promoteWhenActiveEmpty: true);
                RemoveMonitorIfEmpty(located.Monitor);
            }

            EnsureMonitor(snapshot.MonitorId).StripSets.Add(Singleton(WindowKey.For(snapshot)));
            return null;
        }

        if (existing.MonitorId == snapshot.MonitorId)
        {
            return null;
        }

        var key = WindowKey.For(snapshot);
        if (Locate(key) is { } found)
        {
            MoveToMonitor(found.Monitor, key, snapshot.MonitorId);
        }
        else
        {
            AppendToDestination(EnsureMonitor(snapshot.MonitorId), key);
        }

        return new MonitorChange(key, existing.MonitorId, snapshot.MonitorId);
    }

    public void Remove(WindowId window)
    {
        if (!_windows.Remove(window, out var snapshot))
        {
            return;
        }

        var key = WindowKey.For(snapshot);
        if (Locate(key) is { } located)
        {
            RemoveKey(located.Monitor, key, promoteWhenActiveEmpty: true);
            RemoveMonitorIfEmpty(located.Monitor);
        }
    }

    public bool SetForeground(WindowId window)
    {
        if (!_windows.TryGetValue(window, out var snapshot))
        {
            return false;
        }

        var key = WindowKey.For(snapshot);
        if (!_monitors.TryGetValue(snapshot.MonitorId, out var monitor)
            || monitor.ActiveSet is not { } active
            || !active.Contains(key))
        {
            return false;
        }

        monitor.ActiveSet = WithForeground(active, key);
        return true;
    }

    public ActivationResult Activate(WindowId window, ActivationSource source)
    {
        if (!_windows.TryGetValue(window, out var snapshot))
        {
            return ActivationResult.Failure;
        }

        if (source == ActivationSource.Passive)
        {
            return ActivationResult.Failure;
        }

        var key = WindowKey.For(snapshot);
        if (Locate(key) is { } located)
        {
            if (located.IsActive)
            {
                located.Monitor.ActiveSet = WithForeground(located.Set, key);
                return new ActivationResult(ActivationOutcome.Changed, located.Set.Id);
            }

            var index = located.Monitor.StripSets.FindIndex(set => set.Id == located.Set.Id);
            if (index < 0)
            {
                return ActivationResult.Failure;
            }

            var demotedId = located.Monitor.ActiveSet?.Id;
            var promoted = located.Monitor.StripSets[index];
            located.Monitor.StripSets.RemoveAt(index);
            DemoteActive(located.Monitor);
            located.Monitor.ActiveSet = WithForeground(promoted, key);
            return new ActivationResult(ActivationOutcome.PromotedSet, promoted.Id, demotedId);
        }

        if (source != ActivationSource.User)
        {
            return ActivationResult.Failure;
        }

        var participants = new List<WindowKey> { key };
        if (_mode == WindowMode.AllAtOnce)
        {
            foreach (var candidate in _windows.Values)
            {
                if (candidate.Id == snapshot.Id
                    || candidate.MonitorId != snapshot.MonitorId
                    || candidate.AppId != snapshot.AppId
                    || !candidate.IsVisible
                    || candidate.IsMinimized)
                {
                    continue;
                }

                participants.Add(WindowKey.For(candidate));
            }
        }

        foreach (var participant in participants)
        {
            RemoveKeyEverywhere(participant, promoteWhenActiveEmpty: false);
        }

        var monitor = EnsureMonitor(snapshot.MonitorId);
        var previousId = monitor.ActiveSet?.Id;
        DemoteActive(monitor);
        var members = participants.Skip(1).Concat(participants.Take(1)).ToArray();
        var created = new StageSet(NewSetId(), members, key, EmptyGeometry());
        monitor.ActiveSet = created;
        return new ActivationResult(ActivationOutcome.CreatedSet, created.Id, previousId);
    }

    public ActivationResult ShiftAdd(WindowId window)
    {
        if (!_windows.TryGetValue(window, out var snapshot))
        {
            return ActivationResult.Failure;
        }

        var key = WindowKey.For(snapshot);
        var monitor = EnsureMonitor(snapshot.MonitorId);
        if (monitor.ActiveSet is null)
        {
            RemoveKeyEverywhere(key, promoteWhenActiveEmpty: true);
            monitor = EnsureMonitor(snapshot.MonitorId);
            var created = Singleton(key);
            monitor.ActiveSet = created;
            return new ActivationResult(ActivationOutcome.CreatedSet, created.Id);
        }

        if (monitor.ActiveSet.Contains(key))
        {
            monitor.ActiveSet = WithForeground(monitor.ActiveSet, key);
            return new ActivationResult(ActivationOutcome.Changed, monitor.ActiveSet.Id);
        }

        RemoveKeyEverywhere(key, promoteWhenActiveEmpty: true);
        monitor = EnsureMonitor(snapshot.MonitorId);
        monitor.ActiveSet = WithForeground(monitor.ActiveSet!, key);
        return new ActivationResult(ActivationOutcome.Changed, monitor.ActiveSet.Id);
    }

    public ActivationResult SwitchToSet(string setId)
    {
        foreach (var monitor in _monitors.Values)
        {
            if (monitor.ActiveSet?.Id == setId)
            {
                return new ActivationResult(ActivationOutcome.Changed, setId);
            }

            var index = monitor.StripSets.FindIndex(set => set.Id == setId);
            if (index < 0)
            {
                continue;
            }

            var demotedId = monitor.ActiveSet?.Id;
            var promoted = monitor.StripSets[index];
            monitor.StripSets.RemoveAt(index);
            DemoteActive(monitor);
            monitor.ActiveSet = WithForeground(promoted, promoted.Foreground);
            return new ActivationResult(ActivationOutcome.PromotedSet, promoted.Id, demotedId);
        }

        return ActivationResult.Failure;
    }

    public ActivationResult OnMonitorChanged(WindowId window)
    {
        if (!_windows.TryGetValue(window, out var snapshot))
        {
            return ActivationResult.Failure;
        }

        var key = WindowKey.For(snapshot);
        if (Locate(key) is not { } located)
        {
            return ActivationResult.Failure;
        }

        if (located.Monitor.Id == snapshot.MonitorId)
        {
            return new ActivationResult(ActivationOutcome.Changed, located.Set.Id);
        }

        return MoveToMonitor(located.Monitor, key, snapshot.MonitorId);
    }

    public void Minimize(WindowId window)
    {
        if (!_windows.TryGetValue(window, out var snapshot))
        {
            return;
        }

        _windows[window] = snapshot with { IsMinimized = true };
        var key = WindowKey.For(snapshot);
        if (Locate(key) is not { } located)
        {
            EnsureMonitor(snapshot.MonitorId).StripSets.Add(Singleton(key));
            return;
        }

        if (!located.IsActive)
        {
            return;
        }

        RemoveKey(located.Monitor, key, promoteWhenActiveEmpty: true);
    }

    public void Restore(WindowId window)
    {
        if (_windows.TryGetValue(window, out var snapshot))
        {
            _windows[window] = snapshot with { IsMinimized = false };
        }
    }

    public void Restore(MonitorState state)
    {
        _monitors.Remove(state.MonitorId);
        var active = FilterSet(state.ActiveSet);
        var strip = new List<StageSet>();
        foreach (var set in state.StripSets)
        {
            if (FilterSet(set) is { } filtered)
            {
                strip.Add(filtered);
            }
        }

        if (active is null && strip.Count == 0)
        {
            return;
        }

        var monitor = EnsureMonitor(state.MonitorId);
        monitor.ActiveSet = active;
        monitor.StripSets.AddRange(strip);
    }

    public void ApplyWorkspace(
        string monitorId,
        IReadOnlyList<WindowId> stage,
        IReadOnlyList<WindowId> strip,
        WindowId? foreground)
    {
        var existingOrder = new List<WindowId>();
        if (_monitors.TryGetValue(monitorId, out var existing))
        {
            if (existing.ActiveSet is { } active)
            {
                existingOrder.AddRange(active.Members.Reverse().Select(key => key.Window));
            }

            foreach (var set in existing.StripSets)
            {
                existingOrder.AddRange(set.Members.Reverse().Select(key => key.Window));
            }

            _monitors.Remove(monitorId);
        }

        var knownStage = stage.Where(_windows.ContainsKey).Distinct().ToList();
        var knownStrip = strip.Where(_windows.ContainsKey).Distinct().ToList();
        var used = knownStage.Concat(knownStrip).ToHashSet();
        var leftovers = existingOrder.Where(id => !used.Contains(id) && _windows.ContainsKey(id)).ToList();
        foreach (var snapshot in _windows.Values)
        {
            if (snapshot.MonitorId == monitorId
                && !used.Contains(snapshot.Id)
                && !leftovers.Contains(snapshot.Id))
            {
                leftovers.Add(snapshot.Id);
            }
        }

        var monitor = EnsureMonitor(monitorId);
        if (knownStage.Count > 0)
        {
            var front = foreground is { } requested && knownStage.Contains(requested)
                ? requested
                : knownStage[0];
            var members = knownStage
                .Where(id => id != front)
                .Reverse()
                .Select(IdKey)
                .Append(IdKey(front))
                .ToArray();
            monitor.ActiveSet = new StageSet(NewSetId(), members, IdKey(front), EmptyGeometry());
        }

        foreach (var id in knownStrip.Where(id => !knownStage.Contains(id)))
        {
            monitor.StripSets.Add(Singleton(IdKey(id)));
        }

        foreach (var id in leftovers)
        {
            monitor.StripSets.Add(Singleton(IdKey(id)));
        }

        RemoveMonitorIfEmpty(monitor);
    }

    public IReadOnlyList<WindowId> CycleOrder(string monitorId)
    {
        if (!_monitors.TryGetValue(monitorId, out var monitor))
        {
            return Array.Empty<WindowId>();
        }

        var order = new List<WindowId>();
        if (monitor.ActiveSet is { } active)
        {
            order.AddRange(active.Members.Reverse().Select(key => key.Window));
        }

        foreach (var set in monitor.StripSets)
        {
            order.AddRange(set.Members.Reverse().Select(key => key.Window));
        }

        return order;
    }

    public void CaptureGeometry(WindowId window, WindowGeometry geometry)
    {
        if (!_windows.TryGetValue(window, out var snapshot))
        {
            return;
        }

        var key = WindowKey.For(snapshot);
        if (Locate(key) is not { } located)
        {
            return;
        }

        var geometryByKey = new Dictionary<WindowKey, WindowGeometry>(located.Set.Geometry)
        {
            [key] = geometry
        };
        var updated = located.Set with { Geometry = geometryByKey };
        if (located.IsActive)
        {
            located.Monitor.ActiveSet = updated;
            return;
        }

        var index = located.Monitor.StripSets.FindIndex(set => set.Id == located.Set.Id);
        if (index >= 0)
        {
            located.Monitor.StripSets[index] = updated;
        }
    }

    public void SetMode(WindowMode mode) => _mode = mode;

    private WindowKey IdKey(WindowId id) => WindowKey.For(_windows[id]);

    private MonitorData EnsureMonitor(string monitorId)
    {
        if (!_monitors.TryGetValue(monitorId, out var monitor))
        {
            monitor = new MonitorData(monitorId);
            _monitors[monitorId] = monitor;
        }

        return monitor;
    }

    private void RemoveMonitorIfEmpty(MonitorData monitor)
    {
        if (monitor.ActiveSet is null && monitor.StripSets.Count == 0)
        {
            _monitors.Remove(monitor.Id);
        }
    }

    private (MonitorData Monitor, StageSet Set, bool IsActive)? Locate(WindowKey key)
    {
        foreach (var monitor in _monitors.Values)
        {
            if (monitor.ActiveSet is { } active && active.Contains(key))
            {
                return (monitor, active, true);
            }

            foreach (var set in monitor.StripSets)
            {
                if (set.Contains(key))
                {
                    return (monitor, set, false);
                }
            }
        }

        return null;
    }

    private void RemoveKeyEverywhere(WindowKey key, bool promoteWhenActiveEmpty)
    {
        foreach (var monitor in _monitors.Values.ToArray())
        {
            if (!RemoveKey(monitor, key, promoteWhenActiveEmpty))
            {
                continue;
            }

            RemoveMonitorIfEmpty(monitor);
            return;
        }
    }

    private static bool RemoveKey(MonitorData monitor, WindowKey key, bool promoteWhenActiveEmpty)
    {
        if (monitor.ActiveSet is { } active && active.Contains(key))
        {
            monitor.ActiveSet = Detach(active, key);
            if (monitor.ActiveSet is null && promoteWhenActiveEmpty)
            {
                PromoteFirst(monitor);
            }

            return true;
        }

        for (var index = 0; index < monitor.StripSets.Count; index++)
        {
            var set = monitor.StripSets[index];
            if (!set.Contains(key))
            {
                continue;
            }

            var updated = Detach(set, key);
            if (updated is null)
            {
                monitor.StripSets.RemoveAt(index);
            }
            else
            {
                monitor.StripSets[index] = updated;
            }

            return true;
        }

        return false;
    }

    private ActivationResult MoveToMonitor(MonitorData oldMonitor, WindowKey key, string destinationId)
    {
        RemoveKey(oldMonitor, key, promoteWhenActiveEmpty: true);
        RemoveMonitorIfEmpty(oldMonitor);
        if (_monitors.TryGetValue(destinationId, out var stale))
        {
            RemoveKey(stale, key, promoteWhenActiveEmpty: true);
            RemoveMonitorIfEmpty(stale);
        }

        var destination = EnsureMonitor(destinationId);
        if (destination.ActiveSet is null)
        {
            var created = Singleton(key);
            destination.ActiveSet = created;
            return new ActivationResult(ActivationOutcome.CreatedSet, created.Id);
        }

        var active = destination.ActiveSet with
        {
            Members = destination.ActiveSet.Members.Append(key).ToArray()
        };
        destination.ActiveSet = WithForeground(active, active.Foreground);
        return new ActivationResult(ActivationOutcome.Changed, destination.ActiveSet.Id);
    }

    private void AppendToDestination(MonitorData destination, WindowKey key)
    {
        if (destination.ActiveSet is null)
        {
            destination.ActiveSet = Singleton(key);
            return;
        }

        var active = destination.ActiveSet with
        {
            Members = destination.ActiveSet.Members.Append(key).ToArray()
        };
        destination.ActiveSet = WithForeground(active, active.Foreground);
    }

    private static void RemoveFromStrip(MonitorData monitor, IReadOnlyList<WindowKey> keys)
    {
        foreach (var key in keys)
        {
            for (var index = monitor.StripSets.Count - 1; index >= 0; index--)
            {
                var set = monitor.StripSets[index];
                if (!set.Contains(key))
                {
                    continue;
                }

                var updated = Detach(set, key);
                if (updated is null)
                {
                    monitor.StripSets.RemoveAt(index);
                }
                else
                {
                    monitor.StripSets[index] = updated;
                }
            }
        }
    }

    private static StageSet? Detach(StageSet set, WindowKey key)
    {
        var members = set.Members.Where(member => member != key).ToArray();
        return members.Length == 0 ? null : SetMembers(set, members);
    }

    private static StageSet SetMembers(StageSet set, IReadOnlyList<WindowKey> members)
    {
        var geometry = set.Geometry
            .Where(pair => members.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var foreground = members.Contains(set.Foreground) ? set.Foreground : members[^1];
        return set with
        {
            Members = members,
            Foreground = foreground,
            Geometry = geometry
        };
    }

    private static StageSet WithForeground(StageSet set, WindowKey key)
    {
        var members = set.Members.Where(member => member != key).Append(key).ToArray();
        return set with { Members = members, Foreground = key };
    }

    private static void PromoteFirst(MonitorData monitor)
    {
        if (monitor.ActiveSet is not null || monitor.StripSets.Count == 0)
        {
            return;
        }

        var promoted = monitor.StripSets[0];
        monitor.StripSets.RemoveAt(0);
        monitor.ActiveSet = promoted;
    }

    private static void DemoteActive(MonitorData monitor)
    {
        if (monitor.ActiveSet is null)
        {
            return;
        }

        monitor.StripSets.Add(monitor.ActiveSet);
        monitor.ActiveSet = null;
    }

    private StageSet? FilterSet(StageSet? set)
    {
        if (set is null)
        {
            return null;
        }

        var members = set.Members
            .Where(member => _windows.TryGetValue(member.Window, out var snapshot)
                && snapshot.ProcessId == member.ProcessId)
            .ToArray();
        if (members.Length == 0)
        {
            return null;
        }

        var geometry = set.Geometry
            .Where(pair => members.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var foreground = members.Contains(set.Foreground) ? set.Foreground : members[^1];
        return set with
        {
            Members = members,
            Foreground = foreground,
            Geometry = geometry
        };
    }

    private StageSet Singleton(WindowKey key) =>
        new(NewSetId(), new[] { key }, key, EmptyGeometry());

    private string NewSetId() => $"set-{++_nextSetId:000000}";

    private static Dictionary<WindowKey, WindowGeometry> EmptyGeometry() => new();

    private sealed class MonitorData
    {
        public MonitorData(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public StageSet? ActiveSet { get; set; }

        public List<StageSet> StripSets { get; } = new();
    }
}
