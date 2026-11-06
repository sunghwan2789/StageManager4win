using System.Collections.Concurrent;
using System.Diagnostics;

namespace StageManager4win.Rendering;

internal sealed class RenderThread : IDisposable
{
    private readonly BlockingCollection<RenderCommand> _commands = new();
    private readonly Dictionary<string, OverlayTarget> _targets = new();
    private readonly Thread _thread;
    private RenderDevice? _device;
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

        foreach (var target in _targets.Values)
        {
            target.Dispose();
        }

        _targets.Clear();
        _device?.Dispose();
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
            return;
        }

        var device = _device;
        var dirty = false;
        foreach (var command in _commands.GetConsumingEnumerable())
        {
            try
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
                        dirty = true;
                        break;
                    case RenderCommand.UpdateOverlay update:
                        if (_targets.TryGetValue(update.MonitorId, out var target))
                        {
                            target.Update(update.Tiles, update.Visible);
                            dirty = true;
                        }

                        break;
                    case RenderCommand.SetOverlayHover hover:
                        if (_targets.TryGetValue(hover.MonitorId, out var hovered))
                        {
                            hovered.SetHover(hover.Window);
                            dirty = true;
                        }

                        break;
                    case RenderCommand.RemoveOverlay remove:
                        if (_targets.Remove(remove.MonitorId, out var removed))
                        {
                            removed.Dispose();
                            dirty = true;
                        }

                        break;
                    case RenderCommand.StopRenderThread:
                        return;
                }

                if (dirty && _commands.Count == 0)
                {
                    device.DComp.Commit();
                    dirty = false;
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        }
    }
}
