using System.Runtime.InteropServices;
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
    private TileUiaProvider[] _tiles = Array.Empty<TileUiaProvider>();
    private UiaRect _bounds;

    public StripUiaProvider(nint hwnd, Action<WindowId> invoke)
    {
        _invoke = invoke;
        _host = UiaInterop.UiaHostProviderFromHwnd(hwnd, out var host) >= 0 ? host : null;
    }

    public void Update(IReadOnlyList<TileVisual> tiles, int originX, int originY, uint dpi)
    {
        var scale = dpi / 96.0;
        lock (_sync)
        {
            var existing = new Dictionary<WindowId, TileUiaProvider>(_tiles.Length);
            foreach (var provider in _tiles)
            {
                existing[provider.Window] = provider;
            }

            var next = new TileUiaProvider[tiles.Count];
            var bounds = new UiaRect();
            for (var index = 0; index < tiles.Count; index++)
            {
                var tile = tiles[index];
                var rectangle = new UiaRect(
                    originX + tile.BoundsDip.X * scale,
                    originY + tile.BoundsDip.Y * scale,
                    tile.BoundsDip.Width * scale,
                    tile.BoundsDip.Height * scale);
                if (!existing.TryGetValue(tile.Window, out var provider))
                {
                    provider = new TileUiaProvider(this, tile.Window, _invoke);
                }

                provider.Update(tile, rectangle);
                next[index] = provider;
                bounds = Union(bounds, rectangle);
            }

            _tiles = next;
            _bounds = bounds;
        }
    }

    public TileUiaProvider? NavigateSibling(TileUiaProvider tile, int delta)
    {
        lock (_sync)
        {
            var index = Array.IndexOf(_tiles, tile);
            var target = index + delta;
            return index >= 0 && target >= 0 && target < _tiles.Length ? _tiles[target] : null;
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
                NavigateDirection.FirstChild => _tiles.Length > 0 ? _tiles[0] : null,
                NavigateDirection.LastChild => _tiles.Length > 0 ? _tiles[^1] : null,
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
