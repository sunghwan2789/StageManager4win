using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using WinRT;

namespace StageManager4win.Interop;

internal static class GraphicsCaptureInterop
{
    public static unsafe GraphicsCaptureItem CreateItemForWindow(nint hwnd)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        PInvoke.WindowsCreateString(className, (uint)className.Length, out var hstring).ThrowOnFailure();
        try
        {
            using var lease = hstring.Lease();
            var factoryIid = typeof(IGraphicsCaptureItemInterop).GUID;
            PInvoke.RoGetActivationFactory(lease.Value, &factoryIid, out var factoryObject)
                .ThrowOnFailure();
            try
            {
                var factory = (IGraphicsCaptureItemInterop)factoryObject;
                // IGraphicsCaptureItem is internal in the projection; use its IID directly.
                var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                factory.CreateForWindow(hwnd, ref itemIid, out var item);
                try
                {
                    return MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
                }
                finally
                {
                    Marshal.Release(item);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factoryObject);
            }
        }
        finally
        {
            hstring.Dispose();
        }
    }

    public static IDirect3DDevice CreateDevice(IDXGIDevice dxgiDevice)
    {
        PInvoke.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable).ThrowOnFailure();
        try
        {
            var unknown = Marshal.GetIUnknownForObject(inspectable);
            try
            {
                // FromAbi adds its own reference; the one from GetIUnknownForObject is ours.
                return MarshalInspectable<IDirect3DDevice>.FromAbi(unknown);
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(inspectable);
        }
    }

    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        try
        {
            var iid = typeof(ID3D11Texture2D).GUID;
            access.GetInterface(ref iid, out var texture);
            try
            {
                return (ID3D11Texture2D)Marshal.GetObjectForIUnknown(texture);
            }
            finally
            {
                Marshal.Release(texture);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(access);
        }
    }
}

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    void CreateForWindow(nint window, ref Guid iid, out nint result);

    void CreateForMonitor(nint monitor, ref Guid iid, out nint result);
}

[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    void GetInterface(ref Guid iid, out nint result);
}
