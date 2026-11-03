using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

// One controller program owns the injected module; no companion launcher process.
static class NativeFeedback {
    const string Game = "ProjectWingman-Win64-Shipping";
    const string ExpectedHash = "092e84225624a4de9c05d2404ff55269bd4a2aa2ff0548f2a183c6bf36abc85a";
    const string DllName = "PWNativeFeedback.dll";
    static readonly object eventGate = new();
    static readonly FeedbackEvents events = new();
    static FeedbackEvents.Hit damage;
    static long eventSession;
    internal static void OnSample(Sample? sample, string? line = null) {
        lock (eventGate) {
            long now = Environment.TickCount64;
            if (sample != null && sample.Session != eventSession) { damage = default; eventSession = sample.Session; }
            if (line == null && sample == null) eventSession = 0;
            var hit = events.Observe(sample, now, line);
            if (hit is { } next && next.Strength >= damage.Remaining(now)) {
                damage = next;
                Console.WriteLine($"[vib] impact={next.Kind} strength={next.Strength:F2}");
            } else if (sample == null && line == null) damage = default;
        }
    }
    static string Name(string kind, int pid) => $@"Local\PWNativeFeedback-{kind}-{pid}";

    internal static string ExtractModule() {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(DllName)
            ?? throw new IOException("Native feedback module is missing; publish with NativeFeedbackDll set.");
        using var buffer = new MemoryStream(); resource.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string folder = Path.Combine(AppContext.BaseDirectory, "runtime", hash);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, DllName);
        if (!File.Exists(path)) {
            string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, false); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (HashFile(path) != hash) throw new IOException("Extracted module hash mismatch; nothing injected.");
        return path;
    }
    static string HashFile(string path) {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    public static async Task Watch(CancellationToken token) {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("Native feedback requires Windows x64.");
        string dll = ExtractModule();
        Console.WriteLine("[vib] waiting for PW; gain 1.5, device expiration 150 ms");
        while (!token.IsCancellationRequested) {
            Process[] processes = Process.GetProcessesByName(Game);
            try {
                if (processes.Length != 1) { await Task.Delay(500, token); continue; }
                Process game = processes[0];
                try { await AttachAndWait(game, dll, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) { if (!game.HasExited) Console.Error.WriteLine("[vib] " + error.Message + "; restart PW before retrying"); }
                if (!game.HasExited) await game.WaitForExitAsync(token);
                Console.WriteLine("[vib] PW exited; waiting for next session");
            } finally { foreach (var process in processes) process.Dispose(); }
        }
    }
    static async Task Heartbeat(MemoryMappedViewAccessor view, CancellationToken token) {
        uint sequence = 0;
        while (!token.IsCancellationRequested) {
            view.Write(8, Environment.TickCount64);
            FeedbackEvents.Hit current;
            lock (eventGate) current = damage;
            view.Write(28, ++sequence); Thread.MemoryBarrier();
            view.Write(24, current.Packed);
            view.Write(16, current.AtMs);
            Thread.MemoryBarrier(); view.Write(28, ++sequence);
            await Task.Delay(10, token);
        }
    }
    static async Task AttachAndWait(Process game, string dll, CancellationToken token) {
        string? image = null;
        for (int retry = 0; retry < 50 && !game.HasExited; ++retry) {
            token.ThrowIfCancellationRequested();
            try { image = game.MainModule?.FileName; if (image != null) break; }
            catch (Win32Exception error) when (error.NativeErrorCode is 24 or 299 or 998) { }
            await Task.Delay(200, token); game.Refresh();
        }
        if (image == null) throw new IOException("Cannot identify PW image.");
        if (HashFile(image) != ExpectedHash) throw new IOException("Unsupported PW image; nothing injected");
        while (!game.HasExited) {
            game.Refresh();
            if (game.MainWindowHandle != 0) break;
            await Task.Delay(200, token);
        }
        if (game.HasExited) return;
        if (game.Modules.Cast<ProcessModule>().Any(m => string.Equals(m.ModuleName, DllName, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Feedback module already loaded; it will not be injected twice");
        using var launchLock = new Semaphore(1, 1, Name("Attach", game.Id));
        if (!launchLock.WaitOne(0)) throw new IOException("Another feedback attachment is in progress");
        try { await AttachOwned(game, dll, token); }
        finally { launchLock.Release(); }
    }
    static async Task AttachOwned(Process game, string dll, CancellationToken token) {
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, Name("Ready", game.Id));
        using var owner = MemoryMappedFile.CreateNew(Name("Owner", game.Id), 32, MemoryMappedFileAccess.ReadWrite);
        using var view = owner.CreateViewAccessor(0, 32, MemoryMappedFileAccess.ReadWrite);
        OnSample(null);
        view.Write(0, Environment.ProcessId); view.Write(4, 3); view.Write(8, Environment.TickCount64);
        ready.Reset();
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task heartbeat = Heartbeat(view, heartbeatStop.Token);
        bool attempted = false;
        try {
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
                attempted = true; threadFinished = false;
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
            await Task.Run(() => {
                long until = Environment.TickCount64 + 10000;
                while (Environment.TickCount64 < until && !game.HasExited) {
                    if (ready.WaitOne(100)) return;
                    token.ThrowIfCancellationRequested();
                }
                throw new IOException("Feedback hook did not become ready; check native log");
            }, token);
            Console.WriteLine($"[vib] hook ready, PID {game.Id}; device initialization is recorded separately");
            Console.WriteLine("[vib] log: " + Path.Combine(Path.GetDirectoryName(dll)!, $"PWNativeFeedback-{game.Id}.log"));
            await game.WaitForExitAsync(token);
        } finally {
            if (attempted) SignalStop(game.Id);
            heartbeatStop.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested) { }
        }
    }
    static void SignalStop(int pid) {
        if (!OperatingSystem.IsWindows()) return;
        try { using var stop = EventWaitHandle.OpenExisting(Name("Stop", pid)); stop.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
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
        throw new IOException("Cannot resolve target LoadLibrary module");
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
