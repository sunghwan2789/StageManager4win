using System.Runtime.InteropServices;

namespace StageManager4win.Accessibility;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ActionUiaProvider :
    IRawElementProviderSimple,
    IRawElementProviderFragment,
    IInvokeProvider
{
    private readonly StripUiaProvider _root;
    private Action _invoke;
    private ActionData _data;

    public ActionUiaProvider(StripUiaProvider root, string automationId, Action invoke)
    {
        _root = root;
        _invoke = invoke;
        _data = new ActionData(automationId, string.Empty, default, true);
    }

    public string AutomationId => Volatile.Read(ref _data).AutomationId;

    public void Update(string name, UiaRect bounds, bool offscreen, Action invoke)
    {
        var id = AutomationId;
        Volatile.Write(ref _data, new ActionData(id, name, bounds, offscreen));
        _invoke = invoke;
    }

    public UiaProviderOptions ProviderOptions => UiaProviderOptions.ServerSideProvider;

    public object? GetPatternProvider(int patternId) =>
        patternId == UiaInterop.UiaInvokePatternId ? this : null;

    public object GetPropertyValue(int propertyId)
    {
        var data = Volatile.Read(ref _data);
        return propertyId switch
        {
            UiaInterop.UiaControlTypePropertyId => UiaInterop.UiaButtonControlTypeId,
            UiaInterop.UiaNamePropertyId => data.Name,
            UiaInterop.UiaAutomationIdPropertyId => data.AutomationId,
            UiaInterop.UiaIsKeyboardFocusablePropertyId => false,
            UiaInterop.UiaIsEnabledPropertyId => true,
            UiaInterop.UiaIsControlElementPropertyId => true,
            UiaInterop.UiaIsContentElementPropertyId => true,
            UiaInterop.UiaIsOffscreenPropertyId => data.IsOffscreen,
            UiaInterop.UiaBoundingRectanglePropertyId =>
                new[] { data.Bounds.Left, data.Bounds.Top, data.Bounds.Width, data.Bounds.Height },
            _ => UiaInterop.NotSupported
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
        new[] { UiaInterop.UiaAppendRuntimeId, Hash(AutomationId) };

    public UiaRect BoundingRectangle => Volatile.Read(ref _data).Bounds;

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus()
    {
    }

    public IRawElementProviderFragmentRoot FragmentRoot => _root;

    public void Invoke() => _invoke();

    private static int Hash(string value)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var character in value)
            {
                hash = (hash ^ character) * 16777619;
            }

            return (int)hash;
        }
    }

    private sealed record ActionData(
        string AutomationId,
        string Name,
        UiaRect Bounds,
        bool IsOffscreen);
}
