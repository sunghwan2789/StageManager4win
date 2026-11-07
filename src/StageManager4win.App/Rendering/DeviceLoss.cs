namespace StageManager4win.Rendering;

internal static class DeviceLoss
{
    // DXGI_ERROR_DEVICE_REMOVED, DXGI_ERROR_DEVICE_HUNG, DXGI_ERROR_DEVICE_RESET.
    private const int DeviceRemoved = unchecked((int)0x887A0005);
    private const int DeviceHung = unchecked((int)0x887A0006);
    private const int DeviceReset = unchecked((int)0x887A0007);

    // WinRT failures are not always COMException, so check the HRESULT on any exception.
    public static bool IsDeviceLost(Exception exception) => IsDeviceLost(exception.HResult);

    public static bool IsDeviceLost(int hresult) =>
        hresult is DeviceRemoved or DeviceHung or DeviceReset;
}
