using System.Globalization;

namespace CamPos.Core.Camera;

public static class PoseMath
{
    public const float RadToDeg = 180f / MathF.PI;
    public const float DegToRad = MathF.PI / 180f;

    public static Quat FromEuler(float pitchDeg, float yawDeg, float rollDeg)
    {
        // Display convention: positive pitch looks up. A right-handed rotation around +X
        // by a negative angle tilts +Z upwards, so the pitch axis is negated here.
        var qx = FromAxisAngle(Vec3.UnitX, -pitchDeg * DegToRad);
        var qy = FromAxisAngle(Vec3.UnitY, yawDeg * DegToRad);
        var qz = FromAxisAngle(Vec3.UnitZ, rollDeg * DegToRad);
        return (qy * qx * qz).Normalized();
    }

    public static Quat FromAxisAngle(Vec3 axis, float radians)
    {
        var a = axis.Normalized();
        var half = radians * 0.5f;
        var s = MathF.Sin(half);
        return new Quat(a.X * s, a.Y * s, a.Z * s, MathF.Cos(half));
    }

    public static Vec3 ToEuler(Quat q)
    {
        q = q.Normalized();
        var forward = q.Rotate(Vec3.UnitZ).Normalized();
        var up = q.Rotate(Vec3.UnitY).Normalized();
        var right = q.Rotate(Vec3.UnitX).Normalized();

        var yaw = MathF.Atan2(forward.X, forward.Z) * RadToDeg;
        var pitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f)) * RadToDeg;
        var roll = MathF.Atan2(right.Y, up.Y) * RadToDeg;

        if (MathF.Abs(forward.Y) > 0.9999f)
            roll = MathF.Atan2(right.Y, up.Y) * RadToDeg;

        return new Vec3(NormalizeAngle(pitch), NormalizeAngle(yaw), NormalizeAngle(roll));
    }

    public static float NormalizeAngle(float degrees)
    {
        while (degrees > 180f) degrees -= 360f;
        while (degrees < -180f) degrees += 360f;
        return degrees;
    }

    public static Quat FromBasis(Vec3 right, Vec3 up, Vec3 forward)
    {
        right = right.Normalized();
        up = up.Normalized();
        forward = forward.Normalized();

        var m00 = right.X; var m01 = up.X; var m02 = forward.X;
        var m10 = right.Y; var m11 = up.Y; var m12 = forward.Y;
        var m20 = right.Z; var m21 = up.Z; var m22 = forward.Z;

        var trace = m00 + m11 + m22;
        float x, y, z, w;
        if (trace > 0f)
        {
            var s = MathF.Sqrt(trace + 1f) * 2f;
            w = 0.25f * s;
            x = (m21 - m12) / s;
            y = (m02 - m20) / s;
            z = (m10 - m01) / s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            var s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
            w = (m21 - m12) / s;
            x = 0.25f * s;
            y = (m01 + m10) / s;
            z = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            var s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
            w = (m02 - m20) / s;
            x = (m01 + m10) / s;
            y = 0.25f * s;
            z = (m12 + m21) / s;
        }
        else
        {
            var s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
            w = (m10 - m01) / s;
            x = (m02 + m20) / s;
            y = (m12 + m21) / s;
            z = 0.25f * s;
        }

        return new Quat(x, y, z, w).Normalized();
    }

    public static Vec3 BasisRight(Quat q) => q.Normalized().Rotate(Vec3.UnitX);
    public static Vec3 BasisUp(Quat q) => q.Normalized().Rotate(Vec3.UnitY);
    public static Vec3 BasisForward(Quat q) => q.Normalized().Rotate(Vec3.UnitZ);

    public static string FormatDegrees(float deg) =>
        string.Create(CultureInfo.InvariantCulture, $"{deg:0.0}°");
}
