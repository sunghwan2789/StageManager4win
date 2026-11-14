using StageManager4win.Core.Stages;

namespace StageManager4win.Core.Layout;

public readonly record struct StripItem(
    string SetId,
    IReadOnlyList<WindowKey> Members,
    int AppCount,
    double AspectRatio)
{
    public int MemberCount => Members.Count;
}
