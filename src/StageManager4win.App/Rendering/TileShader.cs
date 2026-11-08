using System.Text;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D;

namespace StageManager4win.Rendering;

internal static class TileShader
{
    private const string Hlsl = """
        cbuffer TileConstants : register(b0)
        {
            float4 UvRect;      // xy = icon scale, zw = icon offset
            float4 Background;
            float4 BorderRect;  // xy = border thickness in UV units
            float4 BorderColor;
        };

        Texture2D TileTexture : register(t0);
        SamplerState TileSampler : register(s0);

        struct VertexOutput
        {
            float4 Position : SV_Position;
            float2 Uv : TEXCOORD0;
        };

        VertexOutput VSMain(uint id : SV_VertexID)
        {
            float2 uv = float2((float)((id << 1) & 2), (float)(id & 2));
            VertexOutput output;
            output.Uv = uv;
            output.Position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
            return output;
        }

        float4 PSMain(VertexOutput input) : SV_Target
        {
            if (BorderRect.x > 0.0
                && (input.Uv.x < BorderRect.x
                    || input.Uv.x > 1.0 - BorderRect.x
                    || input.Uv.y < BorderRect.y
                    || input.Uv.y > 1.0 - BorderRect.y))
            {
                return BorderColor;
            }

            float2 local = (input.Uv - UvRect.zw) / UvRect.xy;
            if (any(local < 0.0) || any(local > 1.0))
            {
                return Background;
            }

            return float4(TileTexture.Sample(TileSampler, local).rgb, 1.0);
        }
        """;

    public static unsafe ID3DBlob Compile(string entryPoint, string profile)
    {
        var source = Encoding.UTF8.GetBytes(Hlsl);
        var result = PInvoke.D3DCompile(
            source,
            null,
            null,
            null,
            entryPoint,
            profile,
            0,
            0,
            out var code,
            out var errors);
        if (result.Failed)
        {
            var message = errors is null ? result.ToString() : ReadBlob(errors);
            throw new InvalidOperationException($"Shader compilation failed: {message}");
        }

        if (errors is not null)
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(errors);
        }

        return code;
    }

    private static unsafe string ReadBlob(ID3DBlob blob)
    {
        var length = (int)blob.GetBufferSize();
        return length == 0 ? string.Empty : Encoding.UTF8.GetString((byte*)blob.GetBufferPointer(), length);
    }
}
