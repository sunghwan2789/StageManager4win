using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Stages;

public static class StripGrouping
{
    public const int MaxCards = 3;

    public static IReadOnlyList<StripGroup> Group(
        IReadOnlyList<WindowId> strip,
        IReadOnlyDictionary<WindowId, WindowSnapshot> windows,
        int maxCards = MaxCards)
    {
        var groups = new List<StripGroup>();
        var seen = new HashSet<WindowId>();
        var emittedApps = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in strip)
        {
            if (!windows.TryGetValue(id, out var snapshot) || !seen.Add(id))
            {
                continue;
            }

            if (!emittedApps.Add(snapshot.AppId))
            {
                continue;
            }

            var cards = new List<WindowId> { id };
            foreach (var other in strip)
            {
                if (cards.Count >= maxCards)
                {
                    break;
                }

                if (other == id || seen.Contains(other))
                {
                    continue;
                }

                if (windows.TryGetValue(other, out var candidate)
                    && candidate.AppId == snapshot.AppId)
                {
                    cards.Add(other);
                    seen.Add(other);
                }
            }

            groups.Add(new StripGroup(cards[0], cards));
        }

        return groups;
    }
}
