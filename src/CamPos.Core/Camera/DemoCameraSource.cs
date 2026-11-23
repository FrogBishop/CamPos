namespace CamPos.Core.Camera;

/// <summary>An animated fake camera used by the UI demo. It never reads the game.</summary>
public sealed class DemoCameraSource : ICameraSource
{
    private readonly DateTime _start = DateTime.UtcNow;
    private readonly float _phase;

    public DemoCameraSource()
    {
        _phase = (float)(Environment.TickCount64 % 6283) / 1000f;
    }

    public string Description => "演示数据 · 不读取游戏";

    public CameraReadResult Read()
    {
        var t = (float)(DateTime.UtcNow - _start).TotalSeconds + _phase;
        var pos = new Vec3(124.6f + MathF.Sin(t * 0.31f) * 18f,
            32.4f + MathF.Sin(t * 0.53f) * 1.8f,
            -318.2f + MathF.Cos(t * 0.31f) * 18f);
        var yaw = t * 24f;
        var pitch = MathF.Sin(t * 0.7f) * 18f;
        var roll = MathF.Sin(t * 0.43f) * 2f;
        var q = PoseMath.FromEuler(pitch, yaw, roll);
        var fov = 45f + MathF.Sin(t * 0.21f) * 7f;
        var forward = q.Rotate(Vec3.UnitZ);
        var character = pos + forward * 5.5f - Vec3.UnitY * 1.6f;
        var frame = new CameraFrame(pos, q, PoseMath.ToEuler(q),
            forward, q.Rotate(Vec3.UnitY), q.Rotate(Vec3.UnitX),
            fov, Description, 0x0, DateTimeOffset.Now)
        {
            CharacterPosition = character,
            AbsolutePosition = new Vec3(pos.X + 1930.59f, pos.Y, pos.Z + 1266.06f),
            CharacterAbsolutePosition = new Vec3(character.X + 1930.59f, character.Y, character.Z + 1266.06f)
        };
        return CameraReadResult.Ok(frame);
    }

    public void Dispose()
    {
    }
}
