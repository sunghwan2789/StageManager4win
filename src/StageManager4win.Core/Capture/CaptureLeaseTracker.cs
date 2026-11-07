using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Capture;

public sealed record CaptureLeaseChange(
    IReadOnlyList<WindowId> Added,
    IReadOnlyList<WindowId> Removed);

public sealed class CaptureLeaseTracker
{
    private WindowId[] _active = Array.Empty<WindowId>();

    public IReadOnlyList<WindowId> Active => _active.ToArray();

    public CaptureLeaseChange Reconcile(IEnumerable<WindowId> requested)
    {
        var next = requested.Distinct().ToArray();
        var current = _active;
        var added = next.Where(id => !current.Contains(id)).ToArray();
        var removed = current.Where(id => !next.Contains(id)).ToArray();
        _active = next;
        return new CaptureLeaseChange(added, removed);
    }

    public CaptureLeaseChange Clear() => Reconcile(Array.Empty<WindowId>());
}
