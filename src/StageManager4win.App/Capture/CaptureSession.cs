using System.Diagnostics;
using StageManager4win.Core.Diagnostics;
using StageManager4win.Interop;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace StageManager4win.Capture;

internal sealed class CaptureSession : IDisposable
{
    private readonly IDirect3DDevice _device;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly object _gate = new();
    private Direct3D11CaptureFrame? _frame;
    private bool _disposed;

    public CaptureSession(nint hwnd, IDirect3DDevice device)
    {
        _device = device;
        var item = GraphicsCaptureInterop.CreateItemForWindow(hwnd);
        Direct3D11CaptureFramePool? pool = null;
        GraphicsCaptureSession? session = null;
        try
        {
            var size = item.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                throw new InvalidOperationException("The capture item has an empty size.");
            }

            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                size);
            session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            item.Closed += OnItemClosed;
            pool.FrameArrived += OnFrameArrived;
            session.StartCapture();
        }
        catch
        {
            // Release a partially created session so repeated failures cannot leak.
            try
            {
                item.Closed -= OnItemClosed;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }

            try
            {
                if (pool is not null)
                {
                    pool.FrameArrived -= OnFrameArrived;
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }

            try
            {
                session?.Dispose();
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }

            try
            {
                pool?.Dispose();
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }

            throw;
        }

        Item = item;
        _pool = pool;
        _session = session;
    }

    public GraphicsCaptureItem Item { get; }

    public event Action? Closed;

    public Direct3D11CaptureFrame? TryGetFrame()
    {
        lock (_gate)
        {
            var frame = _frame;
            _frame = null;
            return frame;
        }
    }

    public void RecreatePool(SizeInt32 size)
    {
        if (size.Width <= 0 || size.Height <= 0 || _disposed)
        {
            return;
        }

        _pool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
    }

    public void Dispose()
    {
        Direct3D11CaptureFrame? frame;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            frame = _frame;
            _frame = null;
        }

        _pool.FrameArrived -= OnFrameArrived;
        Item.Closed -= OnItemClosed;
        try
        {
            _session.Dispose();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            FileLogger.Default.Write(
                "capture",
                $"dispose hr=0x{exception.HResult:X8} {exception.GetType().Name}");
        }

        _pool.Dispose();
        frame?.Dispose();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Direct3D11CaptureFrame? frame;
        try
        {
            frame = sender.TryGetNextFrame();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            FileLogger.Default.Write(
                "capture",
                $"frame hr=0x{exception.HResult:X8} {exception.GetType().Name}");
            return;
        }

        if (frame is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                frame.Dispose();
                return;
            }

            var previous = _frame;
            _frame = frame;
            previous?.Dispose();
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args) => Closed?.Invoke();
}
