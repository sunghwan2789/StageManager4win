using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dxgi.Common;

namespace StageManager4win.Rendering;

internal static class TilePainter
{
    // A 1x1 bitmap is a solid tile; larger bitmaps are letterboxed as app icons.
    private const float IconScale = 0.6f;

    [StructLayout(LayoutKind.Sequential)]
    private struct TileConstants
    {
        public Vector2 UvScale;
        public Vector2 UvOffset;
        public Vector4 Background;
        public Vector4 BorderRect;
        public Vector4 BorderColor;
        public Vector4 Tint;
    }

    public static unsafe void Paint(
        RenderDevice device,
        IDCompositionSurface surface,
        IconBitmap bitmap,
        uint width,
        uint height,
        uint borderPixels,
        uint borderBgra,
        float dim = 1f)
    {
        var expected = checked(bitmap.Width * bitmap.Height * 4);
        if (bitmap.Bgra.Length < expected)
        {
            throw new ArgumentException("Icon pixel buffer is too small.", nameof(bitmap));
        }

        ID3D11Texture2D? iconTexture = null;
        ID3D11ShaderResourceView? view = null;
        try
        {
            fixed (byte* pixels = bitmap.Bgra)
            {
                var description = new D3D11_TEXTURE2D_DESC
                {
                    Width = (uint)bitmap.Width,
                    Height = (uint)bitmap.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                    SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                    Usage = D3D11_USAGE.D3D11_USAGE_IMMUTABLE,
                    BindFlags = D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE
                };
                var data = new D3D11_SUBRESOURCE_DATA
                {
                    pSysMem = pixels,
                    SysMemPitch = (uint)(bitmap.Width * 4),
                    SysMemSlicePitch = (uint)expected
                };
                device.D3D11Device.CreateTexture2D(description, data, out iconTexture);
            }

            device.D3D11Device.CreateShaderResourceView(iconTexture, null, out view);

            var isSolid = bitmap.Width == 1 && bitmap.Height == 1;
            var scale = isSolid ? 1f : IconScale;
            var constants = new TileConstants
            {
                UvScale = new Vector2(scale),
                UvOffset = new Vector2((1f - scale) / 2f),
                Background = new Vector4(0.08f, 0.08f, 0.10f, 1f),
                BorderRect = BorderInUv(borderPixels, width, height),
                BorderColor = ToColor(borderBgra),
                Tint = new Vector4(dim, dim, dim, 1f)
            };
            Draw(device, surface, view, constants, width, height);
        }
        finally
        {
            if (view is not null)
            {
                Marshal.ReleaseComObject(view);
            }

            if (iconTexture is not null)
            {
                Marshal.ReleaseComObject(iconTexture);
            }
        }
    }

    public static unsafe void PaintFrame(
        RenderDevice device,
        IDCompositionSurface surface,
        ID3D11Texture2D texture,
        uint width,
        uint height,
        Vector2 uvScale,
        uint borderPixels,
        uint borderBgra,
        float dim = 1f)
    {
        ID3D11ShaderResourceView? view = null;
        try
        {
            device.D3D11Device.CreateShaderResourceView(texture, null, out view);
            var constants = new TileConstants
            {
                UvScale = uvScale,
                UvOffset = Vector2.Zero,
                Background = new Vector4(0f, 0f, 0f, 1f),
                BorderRect = BorderInUv(borderPixels, width, height),
                BorderColor = ToColor(borderBgra),
                Tint = new Vector4(dim, dim, dim, 1f)
            };
            Draw(device, surface, view, constants, width, height);
        }
        finally
        {
            if (view is not null)
            {
                Marshal.ReleaseComObject(view);
            }
        }
    }

    private static Vector4 BorderInUv(uint borderPixels, uint width, uint height) =>
        borderPixels > 0 && width > 0 && height > 0
            ? new Vector4((float)borderPixels / width, (float)borderPixels / height, 0f, 0f)
            : Vector4.Zero;

    private static Vector4 ToColor(uint bgra) => new(
        ((bgra >> 16) & 0xFF) / 255f,
        ((bgra >> 8) & 0xFF) / 255f,
        (bgra & 0xFF) / 255f,
        ((bgra >> 24) & 0xFF) / 255f);

    private static unsafe void Draw(
        RenderDevice device,
        IDCompositionSurface surface,
        ID3D11ShaderResourceView view,
        TileConstants constants,
        uint width,
        uint height)
    {
        surface.BeginDraw(null, typeof(ID3D11Texture2D).GUID, out var update, out var offset);
        ID3D11RenderTargetView? target = null;
        try
        {
            var texture = (ID3D11Texture2D)update;
            device.D3D11Device.CreateRenderTargetView(texture, null, out target);

            var context = device.Context;
            context.Map(device.Constants, 0, D3D11_MAP.D3D11_MAP_WRITE_DISCARD, 0, out var mapped);
            *(TileConstants*)mapped.pData = constants;
            context.Unmap(device.Constants, 0);

            var viewport = new D3D11_VIEWPORT
            {
                TopLeftX = offset.X,
                TopLeftY = offset.Y,
                Width = width,
                Height = height,
                MinDepth = 0,
                MaxDepth = 1
            };

            context.OMSetRenderTargets(new[] { target }, null);
            context.RSSetViewports(new[] { viewport });
            context.IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            context.VSSetShader(device.VertexShader, null, 0);
            context.PSSetShader(device.PixelShader, null, 0);
            context.PSSetConstantBuffers(0, new[] { device.Constants });
            context.PSSetShaderResources(0, new[] { view });
            context.PSSetSamplers(0, new[] { device.Sampler });
            context.Draw(3, 0);
        }
        finally
        {
            surface.EndDraw();
            if (target is not null)
            {
                Marshal.ReleaseComObject(target);
            }

            Marshal.ReleaseComObject(update);
        }
    }
}
