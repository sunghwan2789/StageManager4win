using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace StageManager4win.Rendering;

public sealed record IconBitmap(int Width, int Height, byte[] Bgra)
{
    private const uint RowBackground = 0xFF20202A;
    private const uint RowText = 0xFFFFFFFF;
    private const uint ChipBackground = 0xFF3B82F6;
    private const uint ChipText = 0xFFFFFFFF;

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

    public static IconBitmap Chip(int size, string text)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(ToColor(ChipBackground));
        DrawText(graphics, text, size, size, 0, ChipText, size * 0.42f, centered: true);
        return FromBitmap(bitmap);
    }

    public static IconBitmap ListingRow(
        int width,
        int height,
        IconBitmap? icon,
        string title,
        string appName,
        bool highContrast)
    {
        var background = highContrast ? 0xFF000000 : RowBackground;
        var foreground = highContrast ? 0xFFFFFFFF : RowText;
        var padding = Math.Max(2, height / 8);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(ToColor(background));
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var iconSize = height - padding * 2;
        var textLeft = padding;
        if (icon is not null)
        {
            using var iconBitmap = ToBitmap(icon);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(
                iconBitmap,
                new Rectangle(padding, padding, iconSize, iconSize));
            textLeft += iconSize + padding;
        }

        var text = string.IsNullOrWhiteSpace(appName) ? title : $"{title} — {appName}";
        DrawText(
            graphics,
            text,
            width - textLeft - padding,
            height,
            textLeft,
            foreground,
            Math.Max(10, height * 0.4f),
            centered: false);
        return FromBitmap(bitmap);
    }

    private static unsafe void DrawText(
        Graphics graphics,
        string text,
        int width,
        int height,
        int left,
        uint foreground,
        float fontSize,
        bool centered)
    {
        using var font = new Font("Segoe UI", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(ToColor(foreground));
        using var format = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Alignment = centered ? StringAlignment.Center : StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(
            text,
            font,
            brush,
            new RectangleF(left, 0, Math.Max(1, width), height),
            format);
    }

    private static Color ToColor(uint bgra) => Color.FromArgb((int)bgra);

    private static unsafe Bitmap ToBitmap(IconBitmap icon)
    {
        var bitmap = new Bitmap(icon.Width, icon.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, icon.Width, icon.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            fixed (byte* pixels = icon.Bgra)
            {
                for (var y = 0; y < icon.Height; y++)
                {
                    Buffer.MemoryCopy(
                        pixels + y * icon.Width * 4,
                        (byte*)data.Scan0 + y * data.Stride,
                        data.Stride,
                        icon.Width * 4);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static unsafe IconBitmap FromBitmap(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(
                    data.Scan0 + y * data.Stride,
                    pixels,
                    y * bitmap.Width * 4,
                    bitmap.Width * 4);
            }

            return new IconBitmap(bitmap.Width, bitmap.Height, pixels);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
