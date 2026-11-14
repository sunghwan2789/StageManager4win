using System.Runtime.InteropServices;
using StageManager4win.Core.Layout;
using StageManager4win.Core.Stages;
using StageManager4win.Rendering;

namespace StageManager4win.Accessibility;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class StripUiaProvider :
    IRawElementProviderSimple,
    IRawElementProviderFragment,
    IRawElementProviderFragmentRoot
{
    private readonly object _sync = new();
    private readonly IRawElementProviderSimple? _host;
    private readonly Action<WindowKey> _invoke;
    private readonly Action<WindowKey> _add;
    private readonly Action<string> _openListing;
    private List<ActionUiaProvider> _actions = new();
    private IRawElementProviderFragment[] _children = Array.Empty<IRawElementProviderFragment>();
    private ActionUiaProvider[] _hitOrder = Array.Empty<ActionUiaProvider>();
    private UiaRect _bounds;
    private bool _exposed;

    public StripUiaProvider(
        nint hwnd,
        Action<WindowKey> invoke,
        Action<WindowKey> add,
        Action<string> openListing)
    {
        _invoke = invoke;
        _add = add;
        _openListing = openListing;
        _host = UiaInterop.UiaHostProviderFromHwnd(hwnd, out var host) >= 0 ? host : null;
    }

    public void Update(
        IReadOnlyList<SetTileVisual> sets,
        IReadOnlyList<TilePlacement> placements,
        int originX,
        int originY,
        uint dpi,
        bool exposed,
        RectD? clipDip = null,
        RectD? modalDip = null)
    {
        lock (_sync)
        {
            _exposed = exposed;
            if (!exposed)
            {
                _children = Array.Empty<IRawElementProviderFragment>();
                _hitOrder = Array.Empty<ActionUiaProvider>();
                _bounds = default;
                return;
            }

            var scale = dpi / 96.0;
            var clip = clipDip is { } clipValue
                ? ToScreen(clipValue, originX, originY, scale)
                : (UiaRect?)null;
            var modal = modalDip is { } modalValue
                ? ToScreen(modalValue, originX, originY, scale)
                : (UiaRect?)null;

            var cache = new Dictionary<string, ActionUiaProvider>(_actions.Count, StringComparer.Ordinal);
            foreach (var provider in _actions)
            {
                cache[provider.AutomationId] = provider;
            }

            var next = new List<ActionUiaProvider>();
            var children = new List<IRawElementProviderFragment>();
            var setActions = new List<ActionUiaProvider>();
            var badgeActions = new List<ActionUiaProvider>();
            var cardActions = new List<ActionUiaProvider>();
            var listingActions = new List<ActionUiaProvider>();
            var bounds = new UiaRect();

            ActionUiaProvider Add(
                string id,
                string name,
                RectD boundsDip,
                Action invoke,
                List<ActionUiaProvider> category)
            {
                var rectangle = ToScreen(boundsDip, originX, originY, scale);
                var offscreen = clip is { } visible && !Intersects(rectangle, visible)
                    || modal is { } popup && Intersects(rectangle, popup);
                if (!cache.TryGetValue(id, out var provider))
                {
                    provider = new ActionUiaProvider(this, id, invoke);
                }

                provider.Update(name, rectangle, offscreen, invoke);
                next.Add(provider);
                children.Add(provider);
                category.Add(provider);
                bounds = Union(bounds, rectangle);
                return provider;
            }

            var setsById = sets.ToDictionary(set => set.SetId, StringComparer.Ordinal);
            foreach (var placement in placements)
            {
                if (!setsById.TryGetValue(placement.SetId, out var set)
                    || placement.Cards.Count == 0)
                {
                    continue;
                }

                var frontPlacement = placement.Cards[^1];
                var front = FindCard(set, frontPlacement.Window) ?? set.Cards[^1];
                var appCount = set.Cards.Select(card => card.AppId).Distinct(StringComparer.Ordinal).Count();
                Add(
                    $"set|{set.SetId}",
                    $"{front.AppName} — {set.Cards.Count} windows, {appCount} apps",
                    placement.UnionBoundsDip,
                    () => _invoke(front.Window),
                    setActions);

                for (var index = placement.Cards.Count - 1; index >= 0; index--)
                {
                    var card = placement.Cards[index];
                    var visual = FindCard(set, card.Window);
                    if (visual is null)
                    {
                        continue;
                    }

                    Add(
                        $"window|{visual.Window.Window.Value}",
                        $"{visual.AppName} — {visual.Title}",
                        card.BoundsDip,
                        () => _invoke(visual.Window),
                        cardActions);
                }

                Add(
                    $"add|{front.Window.Window.Value}",
                    $"Add {front.AppName} to the stage",
                    placement.UnionBoundsDip,
                    () => _add(front.Window),
                    setActions);

                if (set.Badges.Count > 0)
                {
                    Add(
                        $"setbadge|{set.SetId}",
                        $"Open {front.AppName} set",
                        Union(set.Badges.Select(badge => badge.BoundsDip)),
                        () => _openListing(set.SetId),
                        badgeActions);
                }

                if (set.ListingOpen)
                {
                    foreach (var row in set.ListingRows)
                    {
                        Add(
                            $"listing|{row.Window.Window.Value}",
                            $"{row.AppName} — {row.Title}",
                            row.BoundsDip,
                            () => _invoke(row.Window),
                            listingActions);
                    }
                }
            }

            _actions = next;
            _children = children.ToArray();
            _hitOrder = listingActions
                .Concat(badgeActions)
                .Concat(cardActions)
                .Concat(setActions)
                .ToArray();
            _bounds = bounds;
        }
    }

    public IRawElementProviderFragment? NavigateSibling(IRawElementProviderFragment provider, int delta)
    {
        lock (_sync)
        {
            var index = Array.IndexOf(_children, provider);
            var target = index + delta;
            return index >= 0 && target >= 0 && target < _children.Length ? _children[target] : null;
        }
    }

    public UiaProviderOptions ProviderOptions => UiaProviderOptions.ServerSideProvider;

    public object? GetPatternProvider(int patternId) => null;

    public object GetPropertyValue(int propertyId) => propertyId switch
    {
        UiaInterop.UiaControlTypePropertyId => UiaInterop.UiaPaneControlTypeId,
        UiaInterop.UiaNamePropertyId => "Stage Manager strip",
        UiaInterop.UiaIsKeyboardFocusablePropertyId => false,
        UiaInterop.UiaIsEnabledPropertyId => _exposed,
        UiaInterop.UiaIsControlElementPropertyId => true,
        UiaInterop.UiaIsContentElementPropertyId => true,
        UiaInterop.UiaIsOffscreenPropertyId => !_exposed,
        UiaInterop.UiaBoundingRectanglePropertyId => BoundsArray(),
        _ => UiaInterop.NotSupported
    };

    private double[] BoundsArray()
    {
        lock (_sync)
        {
            return new[] { _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height };
        }
    }

    public IRawElementProviderSimple? HostRawElementProvider => _host;

    public IRawElementProviderFragment? Navigate(NavigateDirection direction)
    {
        lock (_sync)
        {
            return direction switch
            {
                NavigateDirection.FirstChild => _children.Length > 0 ? _children[0] : null,
                NavigateDirection.LastChild => _children.Length > 0 ? _children[^1] : null,
                _ => null
            };
        }
    }

    // The host HWND supplies the fragment root's runtime id.
    public int[]? GetRuntimeId() => null;

    public UiaRect BoundingRectangle
    {
        get
        {
            lock (_sync)
            {
                return _bounds;
            }
        }
    }

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus()
    {
    }

    public IRawElementProviderFragmentRoot FragmentRoot => this;

    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y)
    {
        lock (_sync)
        {
            if (!_exposed)
            {
                return null;
            }

            foreach (var action in _hitOrder)
            {
                var bounds = action.BoundingRectangle;
                if (x >= bounds.Left
                    && x < bounds.Left + bounds.Width
                    && y >= bounds.Top
                    && y < bounds.Top + bounds.Height)
                {
                    return action;
                }
            }

            return null;
        }
    }

    public IRawElementProviderFragment? GetFocus() => null;

    private static CardVisual? FindCard(SetTileVisual set, WindowKey window)
    {
        foreach (var card in set.Cards)
        {
            if (card.Window == window)
            {
                return card;
            }
        }

        return null;
    }

    private static UiaRect ToScreen(RectD dip, int originX, int originY, double scale) =>
        new(
            originX + dip.X * scale,
            originY + dip.Y * scale,
            dip.Width * scale,
            dip.Height * scale);

    private static RectD Union(IEnumerable<RectD> rectangles)
    {
        var first = true;
        var left = 0.0;
        var top = 0.0;
        var right = 0.0;
        var bottom = 0.0;
        foreach (var rectangle in rectangles)
        {
            if (first)
            {
                left = rectangle.X;
                top = rectangle.Y;
                right = rectangle.X + rectangle.Width;
                bottom = rectangle.Y + rectangle.Height;
                first = false;
                continue;
            }

            left = Math.Min(left, rectangle.X);
            top = Math.Min(top, rectangle.Y);
            right = Math.Max(right, rectangle.X + rectangle.Width);
            bottom = Math.Max(bottom, rectangle.Y + rectangle.Height);
        }

        return first ? default : new RectD(left, top, right - left, bottom - top);
    }

    private static UiaRect Union(UiaRect a, UiaRect b)
    {
        if (a.Width <= 0 || a.Height <= 0)
        {
            return b;
        }

        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        var right = Math.Max(a.Left + a.Width, b.Left + b.Width);
        var bottom = Math.Max(a.Top + a.Height, b.Top + b.Height);
        return new UiaRect(left, top, right - left, bottom - top);
    }

    private static bool Intersects(UiaRect a, UiaRect b) =>
        a.Left < b.Left + b.Width
        && a.Left + a.Width > b.Left
        && a.Top < b.Top + b.Height
        && a.Top + a.Height > b.Top;
}
