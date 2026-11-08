using System.Runtime.InteropServices;
using StageManager4win.Core.Layout;
using StageManager4win.Core.Windows;
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
    private readonly Action<WindowId> _invoke;
    private readonly Action<WindowId> _add;
    private TileUiaProvider[] _tiles = Array.Empty<TileUiaProvider>();
    private AddToStageUiaProvider[] _adds = Array.Empty<AddToStageUiaProvider>();
    private IRawElementProviderFragment[] _children = Array.Empty<IRawElementProviderFragment>();
    private UiaRect _bounds;

    public StripUiaProvider(nint hwnd, Action<WindowId> invoke, Action<WindowId> add)
    {
        _invoke = invoke;
        _add = add;
        _host = UiaInterop.UiaHostProviderFromHwnd(hwnd, out var host) >= 0 ? host : null;
    }

    public void Update(IReadOnlyList<TileVisual> tiles, int originX, int originY, uint dpi)
    {
        var scale = dpi / 96.0;
        lock (_sync)
        {
            var existingTiles = new Dictionary<WindowId, TileUiaProvider>(_tiles.Length);
            foreach (var provider in _tiles)
            {
                existingTiles[provider.Window] = provider;
            }

            var existingAdds = new Dictionary<WindowId, AddToStageUiaProvider>(_adds.Length);
            foreach (var provider in _adds)
            {
                existingAdds[provider.Window] = provider;
            }

            var nextTiles = new TileUiaProvider[tiles.Count];
            var nextAdds = new AddToStageUiaProvider[tiles.Count];
            var children = new IRawElementProviderFragment[tiles.Count * 2];
            var bounds = new UiaRect();
            for (var index = 0; index < tiles.Count; index++)
            {
                var tile = tiles[index];
                var stack = TileStack.HitBounds(tile.BoundsDip);
                var rectangle = new UiaRect(
                    originX + stack.X * scale,
                    originY + stack.Y * scale,
                    stack.Width * scale,
                    stack.Height * scale);
                if (!existingTiles.TryGetValue(tile.Window, out var tileProvider))
                {
                    tileProvider = new TileUiaProvider(this, tile.Window, _invoke);
                }

                if (!existingAdds.TryGetValue(tile.Window, out var addProvider))
                {
                    addProvider = new AddToStageUiaProvider(this, tile.Window, _add);
                }

                tileProvider.Update(tile, rectangle);
                addProvider.Update(tile, rectangle);
                nextTiles[index] = tileProvider;
                nextAdds[index] = addProvider;
                children[index * 2] = tileProvider;
                children[index * 2 + 1] = addProvider;
                bounds = Union(bounds, rectangle);
            }

            _tiles = nextTiles;
            _adds = nextAdds;
            _children = children;
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
        UiaInterop.UiaIsEnabledPropertyId => true,
        UiaInterop.UiaIsControlElementPropertyId => true,
        UiaInterop.UiaIsContentElementPropertyId => true,
        UiaInterop.UiaIsOffscreenPropertyId => false,
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
            foreach (var tile in _tiles)
            {
                var bounds = tile.BoundingRectangle;
                if (x >= bounds.Left
                    && x < bounds.Left + bounds.Width
                    && y >= bounds.Top
                    && y < bounds.Top + bounds.Height)
                {
                    return tile;
                }
            }

            return null;
        }
    }

    public IRawElementProviderFragment? GetFocus() => null;

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
}
