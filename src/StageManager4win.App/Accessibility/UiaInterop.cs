using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace StageManager4win.Accessibility;

internal static class UiaInterop
{
    public const int UiaInvokePatternId = 10000;
    public const int UiaBoundingRectanglePropertyId = 30002;
    public const int UiaControlTypePropertyId = 30003;
    public const int UiaIsOffscreenPropertyId = 30024;
    public const int UiaNamePropertyId = 30005;
    public const int UiaIsKeyboardFocusablePropertyId = 30009;
    public const int UiaIsEnabledPropertyId = 30010;
    public const int UiaAutomationIdPropertyId = 30011;
    public const int UiaIsControlElementPropertyId = 30016;
    public const int UiaIsContentElementPropertyId = 30017;
    public const int UiaButtonControlTypeId = 50000;
    // 50020 is UIA_TextControlTypeId; the Pane control type is 50033.
    public const int UiaPaneControlTypeId = 50033;
    public const int UiaAppendRuntimeId = 3;

    public static object NotSupported { get; } = GetNotSupported();

    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    public static extern nint UiaReturnRawElementProvider(
        nint hwnd,
        nint wParam,
        nint lParam,
        [MarshalAs(UnmanagedType.Interface)] IRawElementProviderSimple? provider);

    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    public static extern int UiaHostProviderFromHwnd(
        nint hwnd,
        [MarshalAs(UnmanagedType.Interface)] out IRawElementProviderSimple provider);

    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    private static extern int UiaGetReservedNotSupportedValue(
        [MarshalAs(UnmanagedType.IUnknown)] out object value);

    private static object GetNotSupported()
    {
        Marshal.ThrowExceptionForHR(UiaGetReservedNotSupportedValue(out var value));
        return value;
    }
}

[ComVisible(true)]
[Flags]
public enum UiaProviderOptions
{
    ServerSideProvider = 0x2
}

[ComVisible(true)]
public enum NavigateDirection
{
    Parent = 0,
    NextSibling = 1,
    PreviousSibling = 2,
    FirstChild = 3,
    LastChild = 4
}

[ComVisible(true)]
[StructLayout(LayoutKind.Sequential)]
public struct UiaRect
{
    public UiaRect(double left, double top, double width, double height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public double Left;
    public double Top;
    public double Width;
    public double Height;
}

[ComVisible(true)]
[Guid("D6DD68D1-86FD-4332-8666-9ABEDEA2D24C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRawElementProviderSimple
{
    UiaProviderOptions ProviderOptions { get; }

    [return: MarshalAs(UnmanagedType.IUnknown)]
    object? GetPatternProvider(int patternId);

    [return: MarshalAs(UnmanagedType.Struct)]
    object GetPropertyValue(int propertyId);

    IRawElementProviderSimple? HostRawElementProvider { get; }
}

[ComVisible(true)]
[Guid("F7063DA8-8359-439C-9297-BBC5299A7D87")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRawElementProviderFragment
{
    IRawElementProviderFragment? Navigate(NavigateDirection direction);

    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
    int[]? GetRuntimeId();

    UiaRect BoundingRectangle { get; }

    IRawElementProviderSimple[]? GetEmbeddedFragmentRoots();

    void SetFocus();

    IRawElementProviderFragmentRoot FragmentRoot { get; }
}

[ComVisible(true)]
[Guid("620CE2A5-A8F0-40A9-8B8C-3BD1E2B0F0F1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRawElementProviderFragmentRoot
{
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);

    IRawElementProviderFragment? GetFocus();
}

[ComVisible(true)]
[Guid("54FCB24B-E18E-47A2-B4D3-ECCBE77599A2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInvokeProvider
{
    void Invoke();
}
