using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CamPos.Injector;

internal static class Program
{
    private const uint CreateSuspended = 0x4;
    private const uint CreateUnicodeEnvironment = 0x400;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageReadWrite = 0x04;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint WaitObject0 = 0;
    private const uint MemRelease = 0x8000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName, StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
        uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo, out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, UIntPtr dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr hThread, out UIntPtr lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    private static int Main(string[] args)
    {
        string? configPath = null, dll = null, extraArgs = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config": configPath = args[++i]; break;
                case "--dll": dll = args[++i]; break;
                case "--args": extraArgs = args[++i]; break;
                default:
                    Console.Error.WriteLine("unknown argument: " + args[i]);
                    return 2;
            }
        }

        configPath ??= Path.Combine(AppContext.BaseDirectory, "CamPos.Injector.json");
        InjectorConfig config;
        string game;
        try
        {
            config = InjectorConfig.Load(configPath);
            game = config.ResolveGameExecutable();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("配置错误: " + ex.Message);
            Console.Error.WriteLine("配置文件: " + Path.GetFullPath(configPath));
            Console.Error.WriteLine("请参考同目录的 CamPos.Injector.example.json。");
            return 2;
        }

        dll ??= Path.Combine(AppContext.BaseDirectory, "CamPosHook.dll");
        if (!File.Exists(dll))
        {
            Console.Error.WriteLine("找不到注入模块: " + dll);
            return 2;
        }
        var workDir = Path.GetDirectoryName(game)!;

        if (Process.GetProcessesByName("GenshinImpact").Length > 0 ||
            Process.GetProcessesByName("YuanShen").Length > 0)
        {
            Console.Error.WriteLine("游戏已经在运行。请先退出游戏；注入器需要在进程启动时加载模块。");
            return 3;
        }

        var commandArgs = extraArgs ?? config.Arguments;
        var cmd = new StringBuilder('"' + game + '"');
        if (!string.IsNullOrWhiteSpace(commandArgs))
        {
            cmd.Append(' ');
            cmd.Append(commandArgs);
        }

        Console.WriteLine("Config: " + Path.GetFullPath(configPath));
        Console.WriteLine("Game  : " + game);
        Console.WriteLine("DLL   : " + dll);
        var si = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcess(game, cmd, IntPtr.Zero, IntPtr.Zero, false,
                CreateSuspended | CreateUnicodeEnvironment, IntPtr.Zero, workDir, ref si, out var pi))
        {
            var err = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"CreateProcess 失败: {err} {new Win32Exception(err).Message}");
            return 1;
        }

        Console.WriteLine("PID: " + pi.dwProcessId);
        var resumed = false;
        try
        {
            var kernel32 = GetModuleHandle("kernel32.dll");
            var loadLibrary = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcAddress(LoadLibraryW)");

            var bytes = Encoding.Unicode.GetBytes(dll + "\0");
            var remote = VirtualAllocEx(pi.hProcess, IntPtr.Zero, (UIntPtr)bytes.Length, MemCommit | MemReserve, PageReadWrite);
            if (remote == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx");
            try
            {
                if (!WriteProcessMemory(pi.hProcess, remote, bytes, (UIntPtr)bytes.Length, out var written) ||
                    written.ToUInt64() != (ulong)bytes.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory");

                var thread = CreateRemoteThread(pi.hProcess, IntPtr.Zero, UIntPtr.Zero, loadLibrary, remote, 0, out _);
                if (thread == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread");
                try
                {
                    var wait = WaitForSingleObject(thread, 15000);
                    if (wait != WaitObject0) throw new TimeoutException("LoadLibraryW 超时。");
                    if (!GetExitCodeThread(thread, out var module))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeThread");
                    if (module == UIntPtr.Zero) throw new InvalidOperationException("LoadLibraryW 返回 NULL，注入模块加载失败。");
                    Console.WriteLine($"DLL base: 0x{module.ToUInt64():X}");
                }
                finally { CloseHandle(thread); }
            }
            finally { VirtualFreeEx(pi.hProcess, remote, UIntPtr.Zero, MemRelease); }

            if (ResumeThread(pi.hThread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread");
            resumed = true;
            Console.WriteLine("Resumed. 请进入大世界，然后打开 CamPos 点击“注入模式”。");
            Thread.Sleep(1200);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("注入失败: " + ex.Message);
            return 1;
        }
        finally
        {
            if (!resumed)
                TerminateProcess(pi.hProcess, 1);
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
        }
    }
}
