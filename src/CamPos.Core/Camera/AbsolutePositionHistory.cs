namespace CamPos.Core.Camera;

internal sealed class AbsolutePositionHistory
{
    // The v3 publisher accepts cached reference/player samples for these intervals.
    internal const int MaxOriginAge = 1500;
    internal const int MaxPlayerAge = 1000;

    internal readonly record struct Sample(long FrameTime, Vec3 Camera, Vec3? Player, long PlayerTime);

    private readonly Queue<Sample> _pending = new();
    private Vec3? _origin;
    private uint _generation;
    private long _lastFrameTime;
    private long _playerValidAfter;
    private Sample? _confirmed;

    public void Clear()
    {
        _pending.Clear();
        _origin = null;
        _confirmed = null;
    }

    public Sample? Observe(long frameTime, Vec3 origin, uint generation, long originTime,
        Vec3 camera, Vec3? player, long playerTime)
    {
        if (_origin != origin || _generation != generation || frameTime < _lastFrameTime ||
            frameTime - _lastFrameTime > MaxOriginAge)
        {
            Clear();
            _origin = origin;
            _generation = generation;
            // The first observation of a new reference can still contain a cached player.
            _playerValidAfter = frameTime;
        }
        else if (frameTime == _lastFrameTime)
        {
            return _confirmed;
        }

        _lastFrameTime = frameTime;
        if (player is null)
        {
            _playerValidAfter = frameTime;
            if (_confirmed is { } previous)
                _confirmed = previous with { Player = null };
        }

        _pending.Enqueue(new Sample(frameTime, camera - origin, player - origin, playerTime));
        // A later observation of the same reference brackets the position sample.
        // A change or loss of reference discards all samples still awaiting confirmation.
        while (_pending.TryPeek(out var sample) && sample.FrameTime < originTime)
        {
            _pending.Dequeue();
            _confirmed = sample.PlayerTime > _playerValidAfter ? sample : sample with { Player = null };
        }
        return _confirmed;
    }
}
