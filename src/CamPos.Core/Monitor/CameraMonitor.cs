using CamPos.Core.Camera;

namespace CamPos.Core.Monitor;

public sealed class CameraMonitor : IDisposable
{
    private readonly object _gate = new();
    private ICameraSource? _source;
    private Timer? _timer;
    private int _reading;
    private int _intervalMs = 33;

    public event Action<CameraReadResult>? Updated;

    public CameraFrame? LastGoodFrame { get; private set; }
    public DateTimeOffset LastSuccess { get; private set; }
    public ICameraSource? Source { get { lock (_gate) return _source; } }
    public bool IsRunning { get; private set; }

    public void SetSource(ICameraSource? source)
    {
        ICameraSource? old;
        lock (_gate)
        {
            old = _source;
            _source = source;
            LastGoodFrame = null;
            LastSuccess = default;
        }
        old?.Dispose();
        if (source is not null)
            Start(_intervalMs);
        else
            Stop();
    }

    public void Start(int intervalMs)
    {
        _intervalMs = Math.Clamp(intervalMs, 10, 1000);
        lock (_gate)
        {
            if (_source is null)
                return;
            _timer?.Dispose();
            _timer = new Timer(_ => Tick(), null, 0, _intervalMs);
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            IsRunning = false;
        }
    }

    public CameraReadResult ReadOnce()
    {
        ICameraSource? src;
        lock (_gate) src = _source;
        if (src is null)
            return CameraReadResult.Fail("没有数据源。");
        var rr = src.Read();
        if (rr.Success && rr.Frame is not null)
        {
            LastGoodFrame = rr.Frame;
            LastSuccess = DateTimeOffset.Now;
        }
        return rr;
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _reading, 1) != 0)
            return;
        try
        {
            var rr = ReadOnce();
            Updated?.Invoke(rr);
        }
        catch (Exception ex)
        {
            Updated?.Invoke(CameraReadResult.Fail("读取异常: " + ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _reading, 0);
        }
    }

    public void Dispose()
    {
        Stop();
        ICameraSource? src;
        lock (_gate) src = _source;
        src?.Dispose();
    }
}
