using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using static Windows.Win32.PInvoke;

namespace StageManager4win;

public class Thumbnail : IDisposable
{
    public const int ThumbnailContentWidth = 160;
    public const int ThumbnailContentHeight = 120;

    private readonly nint _id;

    internal Thumbnail(HWND destination, HWND source)
    {
        DwmRegisterThumbnail(destination, source, out _id).ThrowOnFailure();
    }

    public void Dispose()
    {
        DwmUnregisterThumbnail(_id);
        GC.SuppressFinalize(this);
    }

    public Size SourceSize
    {
        get
        {
            DwmQueryThumbnailSourceSize(_id, out var size).ThrowOnFailure();
            return size;
        }
    }

    public Size Size
    {
        get
        {
            var sourceSize = SourceSize;
            return Size.Truncate(sourceSize * GetFitRatio(sourceSize));
        }
    }

    public Point Location { get; set; } = Point.Empty;

    public bool Inflate { get; set; }

    public Rectangle Rectangle
    {
        get
        {
            var rect = new Rectangle(Location, Size);
            if (!Inflate)
            {
                rect.Inflate(-5, -5);
            }
            return rect;
        }
    }

    public void Update()
    {
        var thumbnailProps = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY | DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION,
            opacity = (byte)(255 * (Inflate ? 100 : 80) / 100),
            fSourceClientAreaOnly = false,
            fVisible = true,
            rcDestination = Rectangle,
        };
        DwmUpdateThumbnailProperties(_id, thumbnailProps).ThrowOnFailure();
    }

    private static float GetFitRatio(Size sourceSize)
    {
        var widthRatio = (float)ThumbnailContentWidth / sourceSize.Width;
        var heightRatio = (float)ThumbnailContentHeight / sourceSize.Height;
        return float.Min(widthRatio, heightRatio);
    }
}
