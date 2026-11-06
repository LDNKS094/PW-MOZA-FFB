using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// One controller program owns the injected module; no companion launcher process.
static class VibrationHost {
    const string Game = "ProjectWingman-Win64-Shipping";
    const string DllName = "PWVibration.dll";
    static readonly object eventGate = new();
    static readonly ImpactEvents events = new();
    static ImpactEvents.Hit impact;
    static long eventSession;
    internal static void Observe(FlightSample? sample, string? line = null) {
        lock (eventGate) {
            long now = Environment.TickCount64;
            if (sample != null && sample.Session != eventSession) { impact = default; eventSession = sample.Session; }
            if (line == null && sample == null) eventSession = 0;
            var hit = events.Observe(sample, now, line);
            if (hit is { } next && next.Strength >= impact.Remaining(now)) impact = next;
            else if (sample == null && line == null) impact = default;
        }
    }
    static string Name(string kind, int pid) => $@"Local\PWVibration-{kind}-{pid}";

    public static async Task Watch(CancellationToken token) {
        Console.WriteLine("[game] Waiting for Project Wingman...");
        while (!token.IsCancellationRequested) {
            Process[] processes = Process.GetProcessesByName(Game);
            try {
                if (processes.Length != 1) { await Task.Delay(500, token); continue; }
                Process game = processes[0];
                Console.WriteLine($"[game] PW PID {game.Id}; preparing vibration module.");
                try { await AttachAndWait(game, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) { if (!game.HasExited) ErrorLog.Write("Vibration connection failed", error); }
                if (!game.HasExited) await game.WaitForExitAsync(token);
                Console.WriteLine("[game] PW exited. Waiting for Project Wingman...");
            } finally { foreach (var process in processes) process.Dispose(); }
        }
    }
    static async Task Heartbeat(MemoryMappedViewAccessor view, CancellationToken token) {
        uint sequence = 0;
        while (!token.IsCancellationRequested) {
            view.Write(8, Environment.TickCount64);
            ImpactEvents.Hit current;
            lock (eventGate) current = impact;
            view.Write(28, ++sequence); Thread.MemoryBarrier();
            view.Write(24, current.Packed);
            view.Write(16, current.AtMs);
            Thread.MemoryBarrier(); view.Write(28, ++sequence);
            await Task.Delay(10, token);
        }
    }
    static async Task AttachAndWait(Process game, CancellationToken token) {
        while (!game.HasExited && game.MainWindowHandle == 0) {
            await Task.Delay(200, token);
            game.Refresh();
        }
        if (game.HasExited) return;
        string image = game.MainModule?.FileName ?? throw new IOException("PW executable path lookup failed");
        string dll = Path.Combine(Path.GetDirectoryName(image)!, DllName);
        if (!File.Exists(dll)) throw new FileNotFoundException($"Vibration module file missing: {dll}", dll);
        using var launchLock = new Semaphore(1, 1, Name("Attach", game.Id));
        if (!launchLock.WaitOne(0)) throw new IOException("Vibration connection is owned by another bridge");
        try { await AttachOwned(game, dll, token); }
        finally { launchLock.Release(); }
    }
    static async Task AttachOwned(Process game, string dll, CancellationToken token) {
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, Name("Ready", game.Id));
        using var connect = new EventWaitHandle(false, EventResetMode.AutoReset, Name("Connect", game.Id));
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, Name("Stop", game.Id));
        using var owner = MemoryMappedFile.CreateOrOpen(Name("Owner", game.Id), 32, MemoryMappedFileAccess.ReadWrite);
        using var view = owner.CreateViewAccessor(0, 32, MemoryMappedFileAccess.ReadWrite);
        view.Write(16, 0L); view.Write(24, 0U); view.Write(28, 0U);
        view.Write(0, Environment.ProcessId); view.Write(4, 3); view.Write(8, Environment.TickCount64);
        ready.Reset();
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task heartbeat = Heartbeat(view, heartbeatStop.Token);
        bool connected = false;
        try {
            var loaded = game.Modules.Cast<ProcessModule>().FirstOrDefault(m => string.Equals(m.ModuleName, DllName, StringComparison.OrdinalIgnoreCase));
            if (loaded != null && !string.Equals(loaded.FileName, dll, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Loaded vibration module: {loaded.FileName}. Restart PW to load {dll}.");
            if (loaded == null) {
                using var process = OpenProcess(0x00100000 | 0x0002 | 0x0400 | 0x0008 | 0x0020 | 0x0010, false, game.Id);
                if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess");
                byte[] path = Encoding.Unicode.GetBytes(dll + '\0');
                nint memory = VirtualAllocEx(process, 0, (nuint)path.Length, 0x3000, 0x04);
                if (memory == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx");
                bool threadFinished = true;
                try {
                    if (!WriteProcessMemory(process, memory, path, (nuint)path.Length, out nuint written) || written != (nuint)path.Length)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory");
                    using var thread = CreateRemoteThread(process, 0, 0, RemoteLoader(game), memory, 0, 0);
                    if (thread.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibrary thread");
                    threadFinished = false;
                    await Task.Run(() => {
                        long until = Environment.TickCount64 + 10000;
                        while (Environment.TickCount64 < until) {
                            uint result = WaitForSingleObject(thread, 100);
                            if (result == 0) return;
                            if (result != 258) throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibrary wait");
                            token.ThrowIfCancellationRequested();
                        }
                        throw new TimeoutException("LoadLibrary timed out");
                    }, token);
                    threadFinished = true;
                } finally {
                    // If LoadLibrary still runs, it may still read its argument.
                    if (threadFinished) VirtualFreeEx(process, memory, 0, 0x8000);
                }
            }
            connect.Set(); connected = true;
            await Task.Run(() => {
                long until = Environment.TickCount64 + 10000;
                while (Environment.TickCount64 < until && !game.HasExited) {
                    if (ready.WaitOne(100)) return;
                    token.ThrowIfCancellationRequested();
                }
                throw new IOException("Vibration connection timed out");
            }, token);
            Console.WriteLine("[vibration] Connected; gain 1.5.");
            await game.WaitForExitAsync(token);
        } finally {
            if (connected) stop.Set();
            heartbeatStop.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested) { }
        }
    }
    static nint RemoteLoader(Process game) {
        nint address = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
        if (address == 0 || !GetModuleHandleExW(0x00000004 | 0x00000002, address, out nint owner))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibrary owner");
        var path = new StringBuilder(32768);
        if (GetModuleFileNameW(owner, path, path.Capacity) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        string name = Path.GetFileName(path.ToString());
        foreach (ProcessModule module in game.Modules)
            if (string.Equals(module.ModuleName, name, StringComparison.OrdinalIgnoreCase)) return module.BaseAddress + (address - owner);
        throw new IOException("Target LoadLibrary module lookup failed");
    }
    [DllImport("kernel32.dll", SetLastError=true)] static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] static extern nint VirtualAllocEx(SafeProcessHandle process, nint address, nuint size, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool VirtualFreeEx(SafeProcessHandle process, nint address, nuint size, uint type);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] bytes, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError=true)] static extern SafeWaitHandle CreateRemoteThread(SafeProcessHandle process, nint attributes, nuint stack, nint start, nint parameter, uint flags, nint threadId);
    [DllImport("kernel32.dll", SetLastError=true)] static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern nint GetModuleHandleW(string module);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true, SetLastError=true)] static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetModuleHandleExW(uint flags, nint address, out nint module);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern uint GetModuleFileNameW(nint module, StringBuilder path, int size);
}
