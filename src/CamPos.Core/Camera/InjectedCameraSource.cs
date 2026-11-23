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
    private const long SequenceOffset = 12;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private record struct State
    {
        public uint Magic;
        public uint Version;
        public uint Valid;
        public uint Sequence;
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

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TimedState
    {
        public State Data;
        public uint PlayerTick, OriginTick, OriginGeneration;
    }

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly uint _version;
    private readonly AbsolutePositionHistory _history = new();
    private State? _previous;

    public InjectedCameraSource()
    {
        Exception? last = null;
        foreach (uint version in new uint[] { 4, 3 })
        foreach (var scope in new[] { "Global", "Local" })
        {
            MemoryMappedFile? file = null;
            try
            {
                file = MemoryMappedFile.OpenExisting($"{scope}\\CamPosState_v{version}", MemoryMappedFileRights.Read);
                _view = OpenView(file, version);
                _file = file;
                _version = version;
                return;
            }
            catch (Exception ex)
            {
                file?.Dispose();
                last = ex;
            }
        }

        throw new InvalidOperationException(
            "未找到 CamPos 注入共享内存。请先运行 CamPos.Injector.exe 启动游戏并注入 CamPosHook.dll。",
            last);
    }

    internal InjectedCameraSource(MemoryMappedFile file, uint version)
    {
        _view = OpenView(file, version);
        _file = file;
        _version = version;
    }

    private static MemoryMappedViewAccessor OpenView(MemoryMappedFile file, uint version) =>
        file.CreateViewAccessor(0, version == 4 ? Marshal.SizeOf<TimedState>() : Marshal.SizeOf<State>(), MemoryMappedFileAccess.Read);

    public string Description => "注入模式 · 相机与角色坐标";

    public CameraReadResult Read() => Read(null);

    internal CameraReadResult Read(long? observationTime)
    {
        lock (_history) return ReadSnapshot(observationTime);
    }

    private CameraReadResult ReadSnapshot(long? observationTime)
    {
        if (!TryReadSnapshot(out var snapshot))
            return _version == 4 ? CameraReadResult.Fail("共享帧正在更新。") : InvalidFrame("共享帧正在更新。");
        var s = snapshot.Data;
        if (s.Magic != Magic || s.Version != _version)
            return InvalidFrame("共享内存版本不匹配。");
        if (s.Valid == 0)
            return InvalidFrame("等待有效的相机数据。");

        long now = observationTime ?? Environment.TickCount64;
        var age = unchecked((uint)now - s.Tick);
        if (age > 2000)
            return InvalidFrame("共享数据已过期（" + age + " ms）。");
        if (_version == 3 && _previous is { } previous && s.Tick == previous.Tick && s != previous)
            return InvalidFrame("共享帧内容尚未稳定。");
        _previous = s;

        var pos = new Vec3(s.Px, s.Py, s.Pz);
        var forward = new Vec3(s.Fx, s.Fy, s.Fz);
        var up = new Vec3(s.Ux, s.Uy, s.Uz);
        var right = new Vec3(s.Rx, s.Ry, s.Rz);
        if (!pos.IsFinite || !forward.IsFinite || !up.IsFinite || !right.IsFinite)
            return InvalidFrame("共享数据包含无效数值。");

        Vec3? character = null;
        if (s.PlayerValid != 0)
        {
            var p = new Vec3(s.PlayerX, s.PlayerY, s.PlayerZ);
            if (p.IsFinite)
                character = p;
        }

        long frameTime = now - age;
        uint originAge = _version == 4 ? unchecked(s.Tick - snapshot.OriginTick) : AbsolutePositionHistory.MaxOriginAge;
        uint playerAge = _version == 4 ? unchecked(s.Tick - snapshot.PlayerTick) : AbsolutePositionHistory.MaxPlayerAge;
        if (playerAge > AbsolutePositionHistory.MaxPlayerAge)
            character = null;
        AbsolutePositionHistory.Sample? confirmed = null;
        if (s.AbsoluteValid != 0 &&
            originAge <= AbsolutePositionHistory.MaxOriginAge && float.IsFinite(s.OriginX) && float.IsFinite(s.OriginZ))
        {
            confirmed = _history.Observe(frameTime, new Vec3(s.OriginX, 0, s.OriginZ), snapshot.OriginGeneration,
                frameTime - originAge, pos, character, frameTime - playerAge);
        }
        else
            _history.Clear();

        var q = PoseMath.FromBasis(right, up, forward);
        var frame = new CameraFrame(
            pos, q, PoseMath.ToEuler(q),
            forward.Normalized(), up.Normalized(), right.Normalized(),
            s.Fov, Description, 0, DateTimeOffset.Now.AddMilliseconds(-age))
        {
            CharacterPosition = character,
            AbsolutePosition = confirmed?.Camera,
            CharacterAbsolutePosition = confirmed?.Player,
            AbsoluteFrameDelay = confirmed is { } c ? TimeSpan.FromMilliseconds(frameTime - c.FrameTime) : TimeSpan.Zero
        };
        return CameraReadResult.Ok(frame);
    }

    private bool TryReadSnapshot(out TimedState snapshot)
    {
        snapshot = default;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (_version == 3)
            {
                // Legacy mappings have no publication sequence. Reject observable partial writes.
                _view.Read(0, out State first);
                Thread.MemoryBarrier();
                _view.Read(0, out snapshot.Data);
                if (first == snapshot.Data) return true;
            }
            else
            {
                uint sequence = _view.ReadUInt32(SequenceOffset);
                if ((sequence & 1) != 0) continue;
                Thread.MemoryBarrier();
                _view.Read(0, out snapshot);
                Thread.MemoryBarrier();
                if (sequence == snapshot.Data.Sequence && sequence == _view.ReadUInt32(SequenceOffset)) return true;
            }
        }
        return false;
    }

    private CameraReadResult InvalidFrame(string error)
    {
        _previous = null;
        _history.Clear();
        return CameraReadResult.Fail(error);
    }

    public void Dispose()
    {
        lock (_history)
        {
            _view.Dispose();
            _file.Dispose();
        }
    }
}
