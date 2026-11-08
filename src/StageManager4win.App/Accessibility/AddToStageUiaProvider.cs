using System.Runtime.InteropServices;
using StageManager4win.Core.Windows;
using StageManager4win.Rendering;

namespace StageManager4win.Accessibility;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class AddToStageUiaProvider :
    IRawElementProviderSimple,
    IRawElementProviderFragment,
    IInvokeProvider
{
    private static readonly object NotSupported = UiaInterop.NotSupported;
    private readonly StripUiaProvider _root;
    private readonly Action<WindowId> _add;
    private AddData _data;

    public AddToStageUiaProvider(StripUiaProvider root, WindowId window, Action<WindowId> add)
    {
        _root = root;
        _add = add;
        _data = new AddData(window, string.Empty, string.Empty, string.Empty, default);
    }

    public WindowId Window => Volatile.Read(ref _data).Window;

    public void Update(TileVisual tile, UiaRect bounds) =>
        Volatile.Write(ref _data, new AddData(
            tile.Window,
            tile.AppId,
            tile.AppName,
            tile.Title,
            bounds));

    public UiaProviderOptions ProviderOptions => UiaProviderOptions.ServerSideProvider;

    public object? GetPatternProvider(int patternId) =>
        patternId == UiaInterop.UiaInvokePatternId ? this : null;

    public object GetPropertyValue(int propertyId)
    {
        var data = Volatile.Read(ref _data);
        return propertyId switch
        {
            UiaInterop.UiaControlTypePropertyId => UiaInterop.UiaButtonControlTypeId,
            UiaInterop.UiaNamePropertyId => $"Add {data.AppName} to the stage",
            UiaInterop.UiaAutomationIdPropertyId => $"add|{data.AppId}|{data.Window.Value}",
            UiaInterop.UiaIsKeyboardFocusablePropertyId => false,
            UiaInterop.UiaIsEnabledPropertyId => true,
            UiaInterop.UiaIsControlElementPropertyId => true,
            UiaInterop.UiaIsContentElementPropertyId => true,
            UiaInterop.UiaIsOffscreenPropertyId => false,
            UiaInterop.UiaBoundingRectanglePropertyId =>
                new[] { data.Bounds.Left, data.Bounds.Top, data.Bounds.Width, data.Bounds.Height },
            _ => NotSupported
        };
    }

    public IRawElementProviderSimple? HostRawElementProvider => null;

    public IRawElementProviderFragment? Navigate(NavigateDirection direction) => direction switch
    {
        NavigateDirection.Parent => _root,
        NavigateDirection.NextSibling => _root.NavigateSibling(this, 1),
        NavigateDirection.PreviousSibling => _root.NavigateSibling(this, -1),
        _ => null
    };

    public int[] GetRuntimeId() =>
        new[] { UiaInterop.UiaAppendRuntimeId, unchecked((int)Window.Value) ^ 0x5A5A5A };

    public UiaRect BoundingRectangle => Volatile.Read(ref _data).Bounds;

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus()
    {
    }

    public IRawElementProviderFragmentRoot FragmentRoot => _root;

    public void Invoke() => _add(Window);

    private sealed record AddData(
        WindowId Window,
        string AppId,
        string AppName,
        string Title,
        UiaRect Bounds);
}
