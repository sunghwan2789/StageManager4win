namespace StageManager4win.Core.Stages;

public sealed record StageSet(
    string Id,
    IReadOnlyList<WindowKey> Members,
    WindowKey Foreground,
    IReadOnlyDictionary<WindowKey, WindowGeometry> Geometry)
{
    public WindowKey Front => Members[^1];

    public bool Contains(WindowKey key) => Members.Contains(key);
}
