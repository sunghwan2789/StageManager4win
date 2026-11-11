using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dxgi;

namespace StageManager4win.Rendering;

internal sealed class RenderDevice : IDisposable
{
    private const uint SdkVersion = 7;

    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11Buffer _constants;

    public RenderDevice()
    {
        var levels = new[]
        {
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0
        };
        var result = PInvoke.D3D11CreateDevice(
            null,
            D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
            default,
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            levels,
            SdkVersion,
            out var device,
            out _,
            out _context);
        if (result.Failed)
        {
            result = PInvoke.D3D11CreateDevice(
                null,
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                default,
                D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                new[] { D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0 },
                SdkVersion,
                out device,
                out _,
                out _context);
        }

        result.ThrowOnFailure();
        D3D11Device = device;
        DxgiDevice = (IDXGIDevice)device;
        PInvoke.DCompositionCreateDevice(DxgiDevice, out IDCompositionDevice dcomp).ThrowOnFailure();
        DComp = dcomp;

        _vertexShader = CreateVertexShader(device);
        _pixelShader = CreatePixelShader(device);

        var sampler = new D3D11_SAMPLER_DESC
        {
            Filter = D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressV = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressW = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            ComparisonFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
            MaxLOD = float.MaxValue
        };
        device.CreateSamplerState(sampler, out _sampler);

        var buffer = new D3D11_BUFFER_DESC
        {
            ByteWidth = TilePainter.ConstantBufferSize,
            Usage = D3D11_USAGE.D3D11_USAGE_DYNAMIC,
            BindFlags = D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER,
            CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_WRITE
        };
        device.CreateBuffer(buffer, null, out _constants);
    }

    public ID3D11Device D3D11Device { get; }

    public IDXGIDevice DxgiDevice { get; }

    public IDCompositionDevice DComp { get; }

    internal ID3D11DeviceContext Context => _context;

    internal ID3D11VertexShader VertexShader => _vertexShader;

    internal ID3D11PixelShader PixelShader => _pixelShader;

    internal ID3D11SamplerState Sampler => _sampler;

    internal ID3D11Buffer Constants => _constants;

    public void Dispose()
    {
        Marshal.ReleaseComObject(_constants);
        Marshal.ReleaseComObject(_sampler);
        Marshal.ReleaseComObject(_pixelShader);
        Marshal.ReleaseComObject(_vertexShader);
        Marshal.ReleaseComObject(DComp);
        Marshal.ReleaseComObject(DxgiDevice);
        Marshal.ReleaseComObject(D3D11Device);
        Marshal.ReleaseComObject(_context);
    }

    private static unsafe ID3D11VertexShader CreateVertexShader(ID3D11Device device)
    {
        var blob = TileShader.Compile("VSMain", "vs_5_0");
        try
        {
            ID3D11VertexShader_unmanaged* raw = null;
            device.CreateVertexShader(blob.GetBufferPointer(), blob.GetBufferSize(), null, &raw);
            try
            {
                raw->QueryInterface(out ID3D11VertexShader shader).ThrowOnFailure();
                return shader;
            }
            finally
            {
                raw->Release();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(blob);
        }
    }

    private static unsafe ID3D11PixelShader CreatePixelShader(ID3D11Device device)
    {
        var blob = TileShader.Compile("PSMain", "ps_5_0");
        try
        {
            ID3D11PixelShader_unmanaged* raw = null;
            device.CreatePixelShader(blob.GetBufferPointer(), blob.GetBufferSize(), null, &raw);
            try
            {
                raw->QueryInterface(out ID3D11PixelShader shader).ThrowOnFailure();
                return shader;
            }
            finally
            {
                raw->Release();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(blob);
        }
    }
}
