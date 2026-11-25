using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using CamPos.Core.Camera;
using CamPos.Core.Monitor;

if (args is ["--publish-test-map", var mapName, var versionText])
{
    using var publisher = new TestMapping(uint.Parse(versionText), mapName);
    var stop = Task.Run(() => Console.ReadLine());
    do
    {
        publisher.Publish(Environment.TickCount64, Vec3.UnitX, Vec3.UnitZ, Vec3.Zero);
    } while (!stop.Wait(33));
    return;
}

var windriseOrigin = new Vec3(-1934.3658f, 0, 1266.3422f);
var lakeOrigin = new Vec3(-2564.5361f, 0, 1329.8206f);
var atWindrise = new Vec3(0, 195.57f, 0);
var atLake = new Vec3(0, 213.57f, 0);

CheckTimedTransitions();
CheckLegacyTransitions();
CheckInterruptedWritesAndWrap();
CheckPausedPublisher();
CheckSourceSwitch();
CheckNamedConnection(4);
CheckNamedConnection(3);
CheckNamedHeaderRecovery();
Console.WriteLine("Passed coordinate checks and cross-process v4/v3 connection checks.");

void CheckNamedConnection(uint version)
{
    string baseName = "CamPosChecks_" + Guid.NewGuid().ToString("N");
    string name = $@"Local\{baseName}_v{version}";
    var source = new InjectedCameraSource(baseName);
    using var monitor = new CameraMonitor();
    var received = new TaskCompletionSource<CameraFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
    monitor.Updated += result =>
    {
        if (result.Frame is { } frame) received.TrySetResult(frame);
    };
    monitor.SetSource(source);
    var missing = monitor.ReadOnce();
    Expect(!missing.Success && missing.Error.Contains("未找到") && source.MappingName is null,
        "missing mappings must be reported while the monitor keeps polling");

    // A higher-priority mapping can still be initializing when a legacy publisher is ready.
    using var incompleteV4 = version == 3 ? MemoryMappedFile.CreateNew($@"Local\{baseName}_v4", 116) : null;
    var start = new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true
    };
    if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(typeof(TestMapping).Assembly.Location);
    start.ArgumentList.Add("--publish-test-map");
    start.ArgumentList.Add(name);
    start.ArgumentList.Add(version.ToString());
    using var child = Process.Start(start)!;
    try
    {
        var frame = received.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        Expect(source.MappingName == name && frame.Position == Vec3.UnitX && frame.CharacterPosition == Vec3.UnitZ,
            $"the running reader must find the late v{version} publisher and read its frame");
    }
    finally
    {
        child.StandardInput.Close();
        if (!child.WaitForExit(5000))
        {
            child.Kill();
            throw new TimeoutException("Test publisher did not exit.");
        }
    }
    Expect(child.ExitCode == 0, "test publisher exited successfully");
}

void CheckNamedHeaderRecovery()
{
    string baseName = "CamPosChecks_" + Guid.NewGuid().ToString("N");
    string name = $@"Local\{baseName}_v4";
    using var file = MemoryMappedFile.CreateNew(name, 116);
    using var view = file.CreateViewAccessor();
    using var source = new InjectedCameraSource(baseName);
    Expect(source.Read(0).Error.Contains("等待写入协议头"), "zeroed mapping must report initialization");
    view.Write(0, 0x43414D50u);
    view.Write(4, 3u);
    var mismatch = source.Read(500);
    Expect(mismatch.Error.Contains(name) && mismatch.Error.Contains("版本 3") && source.MappingName is null,
        "report the incompatible header and keep looking");
    view.Write(4, 4u);
    Expect(source.Read(1000).Error.Contains("等待有效的相机数据") && source.MappingName == name,
        "report the connection separately from the first valid frame");
}

void CheckTimedTransitions()
{
    using var f = new TestMapping(4);
    CameraFrame Send(long t, Vec3 camera, Vec3? player, Vec3? origin, uint generation,
        long? originTick = null, long? playerTick = null)
    {
        f.Publish(t, camera, player, origin, generation, originTick, playerTick);
        return f.Frame(t);
    }

    Send(5000, atWindrise, atWindrise, windriseOrigin, 1);
    Send(5033, atWindrise, atWindrise, windriseOrigin, 1);
    var a = Send(5066, atWindrise, atWindrise, windriseOrigin, 1);
    Expect(a.CharacterAbsolutePosition == atWindrise - windriseOrigin, "Windrise landing and measured height");
    Expect(a.AbsoluteFrameDelay.TotalMilliseconds == 33, "timed sample delay");

    var shifted = atLake + new Vec3(20, 0, 5);
    var waiting = Send(5099, shifted, shifted, windriseOrigin, 1, originTick: 5066);
    Expect(waiting.Position == shifted && waiting.AbsolutePosition == atWindrise - windriseOrigin,
        "fresh scene position must not be combined with the cached old origin");
    var changed = Send(5132, atLake, atWindrise, lakeOrigin, 2, playerTick: 5066);
    Expect(changed.AbsolutePosition is null && changed.CharacterAbsolutePosition is null, "origin change");
    var oldPlayer = Send(5165, atLake, atLake, lakeOrigin, 2);
    Expect(oldPlayer.AbsolutePosition == atLake - lakeOrigin && oldPlayer.CharacterAbsolutePosition is null,
        "old player must not be combined with the new origin");
    var b = Send(5198, atLake, atLake, lakeOrigin, 2);
    Expect(b.CharacterAbsolutePosition == atLake - lakeOrigin, "Starfell Lake landing and measured height");

    var missingPlayer = Send(5231, atLake, null, lakeOrigin, 2);
    Expect(missingPlayer.AbsolutePosition.HasValue && missingPlayer.CharacterAbsolutePosition is null, "missing player");
    Expect(Send(5264, atLake, atLake, lakeOrigin, 3).AbsolutePosition is null, "same numeric origin in a new generation");
    Expect(Send(5297, atLake, atLake, null, 3).AbsolutePosition is null, "missing reference");
    Expect(Send(5330, atLake, atLake, lakeOrigin, 3, originTick: 3000).AbsolutePosition is null, "expired reference");
}

void CheckLegacyTransitions()
{
    using var f = new TestMapping(3);
    for (long t = 0; t <= 3000; t += 50)
    {
        f.Publish(t, atWindrise, atWindrise, windriseOrigin);
        var frame = f.Frame(t);
        if (t <= 1500) Expect(frame.AbsolutePosition is null, "legacy reference cache horizon");
        if (t == 3000)
        {
            Expect(frame.CharacterAbsolutePosition == atWindrise - windriseOrigin, "legacy Windrise");
            Expect(frame.AbsoluteFrameDelay.TotalMilliseconds == 1550, "reported legacy delay");
        }
    }

    for (long t = 3050; t <= 4500; t += 50)
    {
        var moved = atLake + new Vec3(27, 0, -5);
        f.Publish(t, moved, moved, windriseOrigin);
        var frame = f.Frame(t);
        Expect(frame.Position == moved && frame.AbsolutePosition == atWindrise - windriseOrigin,
            "v3 can cache the reference for the full 1500 ms");
    }
    for (long t = 4550; t <= 7250; t += 50)
    {
        f.Publish(t, atLake, t <= 5550 ? atWindrise : atLake, lakeOrigin);
        var frame = f.Frame(t);
        Expect(frame.AbsolutePosition is null || frame.AbsolutePosition == atLake - lakeOrigin, "legacy camera recovery");
        Expect(frame.CharacterAbsolutePosition is null || frame.CharacterAbsolutePosition == atLake - lakeOrigin,
            "legacy player cache expiry");
        if (t == 7250) Expect(frame.CharacterAbsolutePosition.HasValue, "legacy player recovery");
    }
    // The old writer stores Tick before the other fields. A changed, already observed tick is suspect.
    f.Publish(7250, atWindrise, atWindrise, lakeOrigin);
    Expect(!f.Read(7250).Success, "observable torn legacy frame");
}

void CheckInterruptedWritesAndWrap()
{
    using var f = new TestMapping(4);
    long start = (long)uint.MaxValue - 40;
    for (int i = 0; i < 4; i++)
    {
        long t = start + i * 33;
        f.Publish(t, atLake, atLake, lakeOrigin);
        var frame = f.Frame(t);
        if (i >= 2) Expect(frame.CharacterAbsolutePosition == atLake - lakeOrigin, "32-bit tick rollover");
    }
    long last = start + 99;
    f.Sequence = 3;
    Expect(!f.Read(last).Success, "partially written snapshot");
    f.Sequence = 4;
    Expect(f.Frame(last).CharacterAbsolutePosition.HasValue, "retry previously complete frame");
    Expect(!f.Read(last + 2001).Success, "stopped writer");
    f.Publish(last + 2100, atLake, atLake, lakeOrigin);
    Expect(f.Frame(last + 2100).AbsolutePosition is null, "reset after an interruption");
}

void CheckPausedPublisher()
{
    using var f = new TestMapping(4);
    using var halfWritten = new ManualResetEventSlim();
    using var finish = new ManualResetEventSlim();
    f.Publish(100, atWindrise, atWindrise, windriseOrigin);
    var writing = Task.Run(() => f.Publish(133, atLake, atLake, lakeOrigin, 2, afterCamera: () =>
    {
        halfWritten.Set();
        if (!finish.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
    }));
    try
    {
        Expect(halfWritten.Wait(TimeSpan.FromSeconds(5)), "publisher reached the middle of its write");
        Expect(!f.Read(133).Success, "a new camera and old player in a half-written record must not be displayed");
    }
    finally
    {
        finish.Set();
        writing.GetAwaiter().GetResult();
    }
    var frame = f.Frame(133);
    Expect(frame.Position == atLake && frame.CharacterPosition == atLake && frame.AbsolutePosition is null,
        "reader recovers when the writer commits the complete record");
}

void CheckSourceSwitch()
{
    using var monitor = new CameraMonitor();
    monitor.SetSource(new DemoCameraSource());
    var queued = monitor.ReadOnce();
    Expect(queued.Success && queued.SourceGeneration == monitor.SourceGeneration, "source generation before switch");
    monitor.SetSource(null);
    Expect(queued.SourceGeneration != monitor.SourceGeneration && monitor.LastGoodFrame is null,
        "queued frame from stopped source is identifiable and cached frame cleared");
    monitor.SetSource(new DemoCameraSource());
    var next = monitor.ReadOnce();
    Expect(next.Success && next.SourceGeneration == monitor.SourceGeneration && next.SourceGeneration != queued.SourceGeneration,
        "same kind of source in a new session");
}

static void Expect(bool ok, string check)
{
    if (!ok) throw new InvalidOperationException(check);
}

// Writes the wire layout to an anonymous mapping or a uniquely named test mapping.
sealed class TestMapping : IDisposable
{
    private readonly uint _version;
    private readonly MemoryMappedViewAccessor _writer;
    private readonly InjectedCameraSource _reader;
    public uint Sequence { set => _writer.Write(12, value); }

    public TestMapping(uint version, string? name = null)
    {
        _version = version;
        var file = MemoryMappedFile.CreateNew(name, version == 4 ? 116 : 104);
        _writer = file.CreateViewAccessor();
        _reader = new InjectedCameraSource(file, version);
        _writer.Write(0, 0x43414D50u);
        _writer.Write(4, version);
        _writer.Write(8, 1u);
        WriteVector(32, Vec3.UnitZ);
        WriteVector(44, Vec3.UnitY);
        WriteVector(56, Vec3.UnitX);
        _writer.Write(68, 60f);
    }

    public void Publish(long tick, Vec3 camera, Vec3? player, Vec3? origin, uint generation = 1,
        long? originTick = null, long? playerTick = null, Action? afterCamera = null)
    {
        uint sequence = _writer.ReadUInt32(12);
        if (_version == 4) Sequence = sequence + 1;
        Thread.MemoryBarrier();
        _writer.Write(16, unchecked((uint)tick));
        WriteVector(20, camera);
        afterCamera?.Invoke();
        _writer.Write(76, player.HasValue ? 1u : 0u);
        WriteVector(80, player ?? Vec3.Zero);
        _writer.Write(92, origin.HasValue ? 1u : 0u);
        _writer.Write(96, origin?.X ?? 0f);
        _writer.Write(100, origin?.Z ?? 0f);
        if (_version == 4)
        {
            _writer.Write(104, unchecked((uint)(playerTick ?? tick)));
            _writer.Write(108, unchecked((uint)(originTick ?? tick)));
            _writer.Write(112, generation);
            Thread.MemoryBarrier();
            Sequence = sequence + 2;
        }
    }

    public CameraReadResult Read(long now) => _reader.Read(now);
    public CameraFrame Frame(long now)
    {
        var result = Read(now);
        return result.Frame ?? throw new InvalidOperationException(result.Error);
    }

    private void WriteVector(long offset, Vec3 p)
    {
        _writer.Write(offset, p.X);
        _writer.Write(offset + 4, p.Y);
        _writer.Write(offset + 8, p.Z);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _reader.Dispose();
    }
}
