using System.Collections.Concurrent;
using System.Diagnostics;
using StageManager4win.Capture;
using StageManager4win.Core.Diagnostics;
using StageManager4win.Core.Stages;
using StageManager4win.Interop;
using StageManager4win.Overlay;

namespace StageManager4win.Rendering;

internal sealed class RenderThread : IDisposable
{
    private readonly BlockingCollection<RenderCommand> _commands = new();
    private readonly Dictionary<string, OverlayTarget> _targets = new();
    private readonly Dictionary<string, IReadOnlyList<SetTileVisual>> _setsByMonitor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OverlayPresentation> _presentationByMonitor = new(StringComparer.Ordinal);
    private readonly Thread _thread;
    private RenderDevice? _device;
    private CaptureManager? _capture;
    private int _previewFps = 30;
    private bool _started;
    private bool _disposed;

    public RenderThread()
    {
        _thread = new Thread(Run)
        {
            Name = "StageManager render",
            IsBackground = true
        };
    }

    public event Action? DeviceLost;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _thread.Start();
    }

    public void Post(RenderCommand command)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _commands.Add(command);
        }
        catch (InvalidOperationException)
        {
            // The render thread has already stopped.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _commands.CompleteAdding();
        if (_started)
        {
            _thread.Join();
        }

        ReleaseResources();
        _commands.Dispose();
    }

    private void Run()
    {
        try
        {
            _device = new RenderDevice();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            FileLogger.Default.Write(
                "render",
                $"device create hr=0x{exception.HResult:X8} {exception.GetType().Name}");
            if (DeviceLoss.IsDeviceLost(exception))
            {
                RaiseDeviceLost();
            }

            return;
        }

        var device = _device;
        try
        {
            _capture = new CaptureManager(
                device.D3D11Device,
                GraphicsCaptureInterop.CreateDevice(device.DxgiDevice));
            _capture.Failed += window =>
                Debug.WriteLine($"Capture failed for 0x{window.Value:X}; keeping the icon tile.");
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            FileLogger.Default.Write(
                "render",
                $"capture manager hr=0x{exception.HResult:X8} {exception.GetType().Name}");
            if (DeviceLoss.IsDeviceLost(exception))
            {
                ReleaseResources();
                RaiseDeviceLost();
                return;
            }
        }

        var dirty = false;
        try
        {
            while (true)
            {
                var stop = false;
                while (_commands.TryTake(out var command))
                {
                    try
                    {
                        stop = Handle(command, device, ref dirty);
                    }
                    catch (Exception exception) when (!DeviceLoss.IsDeviceLost(exception))
                    {
                        Debug.WriteLine(exception);
                    }

                    if (stop)
                    {
                        break;
                    }
                }

                if (stop || _commands.IsCompleted)
                {
                    break;
                }

                if (_capture is not null)
                {
                    var now = Stopwatch.GetTimestamp();
                    var interval = Math.Max(1, Stopwatch.Frequency / Math.Max(1, _previewFps));
                    foreach (var target in _targets.Values)
                    {
                        try
                        {
                            if (target.DrawLiveFrames(_capture, now, interval))
                            {
                                dirty = true;
                            }
                        }
                        catch (Exception exception) when (!DeviceLoss.IsDeviceLost(exception))
                        {
                            Debug.WriteLine(exception);
                        }
                    }
                }

                if (dirty)
                {
                    try
                    {
                        device.DComp.Commit();
                    }
                    catch (Exception exception) when (!DeviceLoss.IsDeviceLost(exception))
                    {
                        Debug.WriteLine(exception);
                    }

                    dirty = false;
                }

                Thread.Sleep(4);
            }
        }
        catch (Exception exception) when (DeviceLoss.IsDeviceLost(exception))
        {
            Debug.WriteLine(exception);
            FileLogger.Default.Write(
                "render",
                $"device lost hr=0x{exception.HResult:X8} {exception.GetType().Name}");
            ReleaseResources();
            RaiseDeviceLost();
        }
    }

    private void RaiseDeviceLost()
    {
        try
        {
            DeviceLost?.Invoke();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private void ReleaseResources()
    {
        try
        {
            _capture?.Dispose();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        _capture = null;
        foreach (var target in _targets.Values)
        {
            try
            {
                target.Dispose();
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        }

        _targets.Clear();
        _setsByMonitor.Clear();
        _presentationByMonitor.Clear();
        try
        {
            _device?.Dispose();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        _device = null;
    }

    private bool Handle(RenderCommand command, RenderDevice device, ref bool dirty)
    {
        switch (command)
        {
            case RenderCommand.CreateOverlay create:
                if (_targets.Remove(create.MonitorId, out var previous))
                {
                    previous.Dispose();
                }

                _targets[create.MonitorId] = new OverlayTarget(
                    device,
                    create.Hwnd,
                    create.MonitorId,
                    create.Dpi);
                _setsByMonitor.Remove(create.MonitorId);
                _presentationByMonitor.Remove(create.MonitorId);
                SyncCaptureRequests();
                dirty = true;
                break;
            case RenderCommand.UpdateOverlay update:
                if (_targets.TryGetValue(update.MonitorId, out var target))
                {
                    target.Update(
                        update.Sets,
                        update.Placements,
                        update.Presentation,
                        update.ClipDip);
                    _setsByMonitor[update.MonitorId] = update.Sets;
                    _presentationByMonitor[update.MonitorId] = update.Presentation;
                    SyncCaptureRequests();
                    dirty = true;
                }

                break;
            case RenderCommand.SetOverlayHover hover:
                if (_targets.TryGetValue(hover.MonitorId, out var hovered))
                {
                    hovered.SetHover(hover.Target);
                    dirty = true;
                }

                break;
            case RenderCommand.UpdateDragGhost ghost:
                if (_targets.TryGetValue(ghost.MonitorId, out var dragged))
                {
                    dragged.SetGhost(ghost.SetId, ghost.DeltaX, ghost.DeltaY);
                    dirty = true;
                }

                break;
            case RenderCommand.SetPreviewFps fps:
                _previewFps = fps.Fps;
                break;
            case RenderCommand.SetCapturePaused paused:
                _capture?.Pause(paused.Paused);
                break;
            case RenderCommand.RemoveOverlay remove:
                if (_targets.Remove(remove.MonitorId, out var removed))
                {
                    removed.Dispose();
                    dirty = true;
                }

                _setsByMonitor.Remove(remove.MonitorId);
                _presentationByMonitor.Remove(remove.MonitorId);
                SyncCaptureRequests();
                break;
            case RenderCommand.StopRenderThread:
                return true;
        }

        return false;
    }

    private void SyncCaptureRequests()
    {
        if (_capture is null)
        {
            return;
        }

        var requests = new List<CaptureRequest>();
        var seen = new HashSet<WindowKey>();
        foreach (var (monitorId, sets) in _setsByMonitor)
        {
            // Hidden and occluded strips must not keep capture sessions alive.
            if (_presentationByMonitor.GetValueOrDefault(monitorId)
                is OverlayPresentation.Hidden or OverlayPresentation.Occluded)
            {
                continue;
            }

            foreach (var set in sets)
            {
                foreach (var card in set.Cards.OrderBy(card => card.StackIndex))
                {
                    if (card.StackIndex > 2
                        || card.IsMinimized
                        || card.SourceHwnd == 0
                        || !seen.Add(card.Window))
                    {
                        continue;
                    }

                    requests.Add(new CaptureRequest(card.Window.Window, card.SourceHwnd));
                }
            }
        }

        _capture.SetRequests(requests);
    }
}
