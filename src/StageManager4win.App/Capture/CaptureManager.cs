using System.Collections.Concurrent;
using System.Diagnostics;
using StageManager4win.Core.Capture;
using StageManager4win.Core.Windows;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32.Graphics.Direct3D11;

namespace StageManager4win.Capture;

internal sealed class CaptureManager : IDisposable
{
    private readonly IDirect3DDevice _device;
    private readonly CaptureLeaseTracker _leases = new();
    private readonly Dictionary<WindowId, CaptureSession> _sessions = new();
    private readonly HashSet<WindowId> _failed = new();
    private readonly ConcurrentQueue<WindowId> _closed = new();
    private CaptureRequest[] _requests = Array.Empty<CaptureRequest>();
    private bool _paused;
    private bool _disposed;

    public CaptureManager(ID3D11Device d3dDevice, IDirect3DDevice device)
    {
        ArgumentNullException.ThrowIfNull(d3dDevice);
        _device = device;
        IsSupported = GraphicsCaptureSession.IsSupported();
        if (!IsSupported)
        {
            Debug.WriteLine("Windows Graphics Capture is not supported; keeping icon tiles.");
        }
    }

    public bool IsSupported { get; }

    public event Action<WindowId>? Failed;

    public void SetRequests(IReadOnlyList<CaptureRequest> requests)
    {
        _requests = requests.ToArray();
        Apply();
    }

    public void Pause(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        _paused = paused;
        if (paused)
        {
            DisposeSessions();
            _leases.Clear();
        }
        else
        {
            Apply();
        }
    }

    public Direct3D11CaptureFrame? TryGetFrame(WindowId window)
    {
        DrainClosed();
        return _sessions.TryGetValue(window, out var session) ? session.TryGetFrame() : null;
    }

    public void RecreatePool(WindowId window, SizeInt32 size)
    {
        DrainClosed();
        if (_sessions.TryGetValue(window, out var session))
        {
            session.RecreatePool(size);
        }
    }

    public void ReportFailure(WindowId window)
    {
        if (!_failed.Add(window))
        {
            return;
        }

        if (_sessions.Remove(window, out var session))
        {
            session.Dispose();
        }

        _leases.Clear();
        _leases.Reconcile(_sessions.Keys);
        Failed?.Invoke(window);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeSessions();
        _leases.Clear();
        _device.Dispose();
    }

    private void Apply()
    {
        if (_disposed || _paused || !IsSupported)
        {
            return;
        }

        DrainClosed();
        if (_failed.Count > 0)
        {
            var requested = _requests
                .Where(request => request.Hwnd != 0)
                .Select(request => request.Window)
                .ToHashSet();
            _failed.RemoveWhere(window => !requested.Contains(window));
        }

        var wanted = new List<CaptureRequest>(_requests.Length);
        var seen = new HashSet<WindowId>();
        foreach (var request in _requests)
        {
            if (request.Hwnd == 0 || _failed.Contains(request.Window) || !seen.Add(request.Window))
            {
                continue;
            }

            wanted.Add(request);
        }

        var change = _leases.Reconcile(wanted.Select(request => request.Window));
        foreach (var window in change.Removed)
        {
            if (_sessions.Remove(window, out var session))
            {
                session.Dispose();
            }
        }

        foreach (var window in change.Added)
        {
            var request = wanted.Find(candidate => candidate.Window == window);
            try
            {
                var session = new CaptureSession(request.Hwnd, _device);
                session.Closed += () => _closed.Enqueue(window);
                _sessions[window] = session;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                _failed.Add(window);
                Failed?.Invoke(window);
            }
        }
    }

    private void DrainClosed()
    {
        while (_closed.TryDequeue(out var window))
        {
            if (_sessions.Remove(window, out var session))
            {
                session.Dispose();
            }

            _leases.Clear();
            _leases.Reconcile(_sessions.Keys);
        }
    }

    private void DisposeSessions()
    {
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }
}
