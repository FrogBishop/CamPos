using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace CamPos.Core.Camera;

/// <summary>
/// Reads camera and active-character state published by CamPosHook.dll through named shared memory.
/// The hook runs inside the game process; this class only reads the mapping.
/// </summary>
public sealed class InjectedCameraSource : ICameraSource
{
    private const uint Magic = 0x43414D50;
    private const uint Version = 3;
    private const string GlobalName = "Global\\CamPosState_v3";
    private const string LocalName = "Local\\CamPosState_v3";

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct State
    {
        public uint Magic;
        public uint Version;
        public uint Valid;
        public uint Reserved;
        public uint Tick;
        public float Px, Py, Pz;
        public float Fx, Fy, Fz;
        public float Ux, Uy, Uz;
        public float Rx, Ry, Rz;
        public float Fov;
        public float Aspect;
        public uint PlayerValid;
        public float PlayerX, PlayerY, PlayerZ;
        public uint AbsoluteValid;
        public float OriginX, OriginZ;
    }

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly string _name;

    public InjectedCameraSource()
    {
        Exception? last = null;
        foreach (var name in new[] { GlobalName, LocalName })
        {
            try
            {
                _file = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
                _view = _file.CreateViewAccessor(0, Marshal.SizeOf<State>(), MemoryMappedFileAccess.Read);
                _name = name;
                return;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            "未找到 CamPos 注入共享内存。请先运行 CamPos.Injector.exe 启动游戏并注入 CamPosHook.dll。",
            last);
    }

    public string Description => "注入模式 · 相机与角色坐标";

    public CameraReadResult Read()
    {
        _view.Read(0, out State s);
        if (s.Magic != Magic || s.Version != Version)
            return CameraReadResult.Fail("注入共享内存版本不匹配。");
        if (s.Valid == 0)
            return CameraReadResult.Fail("注入已连接，等待相机数据（进入大世界后出现）。");

        var age = unchecked((uint)Environment.TickCount - s.Tick);
        if (age > 2000)
            return CameraReadResult.Fail("注入数据已过期（" + age + " ms）。");

        var pos = new Vec3(s.Px, s.Py, s.Pz);
        var forward = new Vec3(s.Fx, s.Fy, s.Fz);
        var up = new Vec3(s.Ux, s.Uy, s.Uz);
        var right = new Vec3(s.Rx, s.Ry, s.Rz);
        if (!pos.IsFinite || !forward.IsFinite || !up.IsFinite || !right.IsFinite)
            return CameraReadResult.Fail("注入数据包含无效数值。");

        Vec3? character = null;
        if (s.PlayerValid != 0)
        {
            var p = new Vec3(s.PlayerX, s.PlayerY, s.PlayerZ);
            if (p.IsFinite)
                character = p;
        }

        Vec3? absolute = null;
        Vec3? characterAbsolute = null;
        if (s.AbsoluteValid != 0 &&
            float.IsFinite(s.OriginX) && float.IsFinite(s.OriginZ))
        {
            absolute = new Vec3(pos.X - s.OriginX, pos.Y, pos.Z - s.OriginZ);
            if (character is { } cp)
                characterAbsolute = new Vec3(cp.X - s.OriginX, cp.Y, cp.Z - s.OriginZ);
        }

        var q = PoseMath.FromBasis(right, up, forward);
        var frame = new CameraFrame(
            pos, q, PoseMath.ToEuler(q),
            forward.Normalized(), up.Normalized(), right.Normalized(),
            s.Fov, Description, 0, DateTimeOffset.Now)
        {
            CharacterPosition = character,
            AbsolutePosition = absolute,
            CharacterAbsolutePosition = characterAbsolute
        };
        return CameraReadResult.Ok(frame);
    }

    public void Dispose()
    {
        _view.Dispose();
        _file.Dispose();
    }
}
