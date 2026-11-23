using CamPos.Core.Camera;

namespace CamPos.Core.Monitor;

public sealed class CameraMonitor : IDisposable
{
    private readonly object _gate = new();
    private ICameraSource? _source;
    private Timer? _timer;
    private int _reading;
    private int _intervalMs = 33;
    private long _generation;

    public event Action<CameraReadResult>? Updated;

    public CameraFrame? LastGoodFrame { get; private set; }
    public DateTimeOffset LastSuccess { get; private set; }
    public ICameraSource? Source { get { lock (_gate) return _source; } }
    public bool IsRunning { get; private set; }
    public long SourceGeneration { get { lock (_gate) return _generation; } }

    public void SetSource(ICameraSource? source)
    {
        ICameraSource? old;
        lock (_gate)
        {
            old = _source;
            if (ReferenceEquals(old, source) && source is not null)
            {
                if (!IsRunning) Start(_intervalMs);
                return;
            }
            _source = source;
            LastGoodFrame = null;
            LastSuccess = default;
            if (source is not null) Start(_intervalMs);
            else Stop();
        }
        old?.Dispose();
    }

    public void Start(int intervalMs)
    {
        lock (_gate)
        {
            _intervalMs = Math.Clamp(intervalMs, 10, 1000);
            if (_source is null)
                return;
            _timer?.Dispose();
            long generation = ++_generation;
            _timer = new Timer(_ => Tick(generation), null, 0, _intervalMs);
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _generation++;
            _timer?.Dispose();
            _timer = null;
            IsRunning = false;
        }
    }

    public CameraReadResult ReadOnce()
    {
        lock (_gate) return ReadCurrentSource();
    }

    private CameraReadResult ReadCurrentSource()
    {
        if (_source is null) return CameraReadResult.Fail("没有数据源。") with { SourceGeneration = _generation };
        var result = _source.Read();
        if (result.Success && result.Frame is { } frame)
        {
            LastGoodFrame = frame;
            LastSuccess = frame.Timestamp;
        }
        return result with { SourceGeneration = _generation };
    }

    private void Tick(long generation)
    {
        if (Interlocked.Exchange(ref _reading, 1) != 0)
            return;
        try
        {
            CameraReadResult rr;
            lock (_gate)
            {
                if (generation != _generation) return;
                rr = ReadCurrentSource();
            }
            Updated?.Invoke(rr);
        }
        catch (Exception ex)
        {
            Updated?.Invoke(CameraReadResult.Fail("读取异常: " + ex.Message) with { SourceGeneration = generation });
        }
        finally
        {
            Interlocked.Exchange(ref _reading, 0);
        }
    }

    public void Dispose()
    {
        SetSource(null);
    }
}
