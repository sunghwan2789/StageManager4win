using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace StageManager4win.Rendering;

public static class IconLoader
{
    public static IconBitmap? Load(string? executablePath, int pixelSize = 64)
    {
        if (string.IsNullOrEmpty(executablePath) || pixelSize <= 0)
        {
            return null;
        }

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            using var source = icon.ToBitmap();
            using var canvas = new Bitmap(pixelSize, pixelSize, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(Color.FromArgb(255, 20, 20, 26));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var scale = Math.Min(
                    (double)pixelSize / source.Width,
                    (double)pixelSize / source.Height);
                var width = Math.Max(1, (int)(source.Width * scale));
                var height = Math.Max(1, (int)(source.Height * scale));
                graphics.DrawImage(
                    source,
                    new Rectangle((pixelSize - width) / 2, (pixelSize - height) / 2, width, height));
            }

            var data = canvas.LockBits(
                new Rectangle(0, 0, pixelSize, pixelSize),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new byte[pixelSize * pixelSize * 4];
                for (var y = 0; y < pixelSize; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        data.Scan0 + y * data.Stride,
                        pixels,
                        y * pixelSize * 4,
                        pixelSize * 4);
                }

                return new IconBitmap(pixelSize, pixelSize, pixels);
            }
            finally
            {
                canvas.UnlockBits(data);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
