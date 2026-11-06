namespace StageManager4win.Rendering;

public sealed record IconBitmap(int Width, int Height, byte[] Bgra)
{
    // Packs 0xAARRGGBB into the BGRA byte order D3D11 expects.
    public static IconBitmap Solid(int width, int height, uint bgra)
    {
        var bytes = new byte[checked(width * height * 4)];
        for (var index = 0; index < bytes.Length; index += 4)
        {
            bytes[index] = (byte)bgra;
            bytes[index + 1] = (byte)(bgra >> 8);
            bytes[index + 2] = (byte)(bgra >> 16);
            bytes[index + 3] = (byte)(bgra >> 24);
        }

        return new IconBitmap(width, height, bytes);
    }
}
