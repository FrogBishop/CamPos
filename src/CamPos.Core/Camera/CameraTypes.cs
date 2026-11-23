using System.Globalization;

namespace CamPos.Core.Camera;

public readonly record struct Vec3(float X, float Y, float Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

    public Vec3 Normalized()
    {
        var len = Length;
        return len > 1e-8f ? new(X / len, Y / len, Z / len) : Zero;
    }

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
    public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec3 Cross(Vec3 a, Vec3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X:0.00}, {Y:0.00}, {Z:0.00})");
}

public readonly record struct Quat(float X, float Y, float Z, float W)
{
    public static readonly Quat Identity = new(0, 0, 0, 1);

    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) && float.IsFinite(W);
    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z + W * W);

    public Quat Normalized()
    {
        var len = Length;
        return len > 1e-8f ? new(X / len, Y / len, Z / len, W / len) : Identity;
    }

    public static Quat operator *(Quat a, Quat b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public Vec3 Rotate(Vec3 v)
    {
        var q = Normalized();
        var u = new Vec3(q.X, q.Y, q.Z);
        var t = Vec3.Cross(u, v) * 2f;
        return v + t * q.W + Vec3.Cross(u, t);
    }
}

public sealed record CameraFrame(
    Vec3 Position,
    Quat Rotation,
    Vec3 EulerDegrees,
    Vec3 Forward,
    Vec3 Up,
    Vec3 Right,
    float FovDegrees,
    string Source,
    ulong PrimaryAddress,
    DateTimeOffset Timestamp)
{
    public Vec3? CharacterPosition { get; init; }
    public Vec3? AbsolutePosition { get; init; }
    public Vec3? CharacterAbsolutePosition { get; init; }
    public bool HasFov => float.IsFinite(FovDegrees) && FovDegrees > 0.01f;
    public bool HasCharacterPosition => CharacterPosition is { } p && p.IsFinite;
    public bool HasAbsolutePosition => AbsolutePosition is { } p && p.IsFinite;
    public bool HasCharacterAbsolutePosition => CharacterAbsolutePosition is { } p && p.IsFinite;

    public string FormatCompact(string separator = "  ")
    {
        var fov = HasFov ? $"{FovDegrees:0.0}°" : "—";
        return string.Create(CultureInfo.InvariantCulture,
            $"X {Position.X,8:0.00}  Y {Position.Y,8:0.00}  Z {Position.Z,8:0.00}{separator}" +
            $"Yaw {EulerDegrees.Y,7:0.0}°  Pitch {EulerDegrees.X,6:0.0}°  Roll {EulerDegrees.Z,5:0.0}°{separator}FOV {fov}");
    }
}

public readonly record struct CameraReadResult(bool Success, CameraFrame? Frame, string Error)
{
    public static CameraReadResult Ok(CameraFrame frame) => new(true, frame, string.Empty);
    public static CameraReadResult Fail(string error) => new(false, null, error);
}
