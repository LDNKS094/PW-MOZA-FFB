using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

// Bounded, nonblocking journal: capture failures must not delay telemetry or masquerade as complete data.
sealed class SessionCapture : IAsyncDisposable {
    sealed record Entry(int File, object Data, string? RawLine = null);
    readonly Channel<Entry> queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(8192) {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait
    });
    readonly List<StreamWriter> files = [];
    readonly long originTicks = Stopwatch.GetTimestamp();
    readonly DateTimeOffset originUtc = DateTimeOffset.UtcNow;
    readonly Task writer;
    readonly string mode;
    long inputId, sendId, dropped, writtenInputs, writtenFrames, validInputs, validSent, failedWrites;
    string? failure;
    public string DirectoryPath { get; }
    public double Now => Stopwatch.GetElapsedTime(originTicks).TotalSeconds;

    public SessionCapture(string directory, string mode) {
        DirectoryPath = Path.GetFullPath(directory); this.mode = mode;
        Directory.CreateDirectory(DirectoryPath);
        try {
            foreach (string name in new[] { "raw.jsonl", "frames.jsonl", "events.jsonl", "capture.txt" })
                files.Add(new StreamWriter(new FileStream(Path.Combine(DirectoryPath, name), FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read), new UTF8Encoding(false), 65536));
            var assembly = typeof(Program).Assembly;
            string entryName = assembly.GetName().Name!;
            string dll = Path.Combine(AppContext.BaseDirectory, entryName + ".dll");
            string program = File.Exists(dll) ? dll : Environment.ProcessPath!;
            string provenance = Path.Combine(DirectoryPath, "provenance");
            Directory.CreateDirectory(provenance);
            string archived = Path.Combine(provenance, Path.GetFileName(program) + ".bin");
            File.Copy(program, archived, false);
            foreach (string suffix in new[] { ".runtimeconfig.json", ".deps.json" }) {
                string config = Path.Combine(AppContext.BaseDirectory, entryName + suffix);
                if (File.Exists(config)) File.Copy(config, Path.Combine(provenance, Path.GetFileName(config)), false);
            }
            string preset = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MOZA Cockpit", "PresetLibrary", "{5a70e8ad-f59c-4b37-b598-2d8083691f5f}.preset");
            string? presetError = null;
            try { if (File.Exists(preset)) File.Copy(preset, Path.Combine(provenance, "PW.preset"), false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { presetError = ex.Message; }
            WriteJson("metadata.json", new {
                schema = 1, mode, started_utc = originUtc, time_unit = "seconds",
                stopwatch_origin_ticks = originTicks, stopwatch_frequency_hz = Stopwatch.Frequency,
                clock = "one shared monotonic clock for received_s and write times; UTC origin is approximate",
                process_path = Environment.ProcessPath, program_path = program,
                program_sha256 = Hash(program), archived_program = Path.GetRelativePath(DirectoryPath, archived),
                archived_sha256 = Hash(archived), preset_path = preset, preset_sha256 = Hash(preset),
                preset_note = "on-disk snapshot, not proof of runtime profile selection", preset_snapshot_error = presetError,
                assembly_name = entryName,
                source_sha256 = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Where(a => a.Key.StartsWith("SourceSha256.", StringComparison.Ordinal))
                    .ToDictionary(a => a.Key[13..], a => a.Value),
                input_units = "world position m; game speed m/s; rotation deg; game/real time s",
                output_units = "DCS fields: speed m/s; specific force G; attitude/rates rad,rad/s; aoa/aos deg",
                write_success_means = "NetworkStream.WriteAsync completed; not proof Cockpit parsed it"
            });
        } catch { foreach (var file in files) file.Dispose(); throw; }
        writer = Task.Run(WriteLoop);
    }

    public long Input(string line, Sample? sample, int connection, double receivedAt) {
        long id = Interlocked.Increment(ref inputId);
        if (sample != null) Interlocked.Increment(ref validInputs);
        Enqueue(new Entry(0, new { input_id = id, pipe_connection = connection,
            received_s = receivedAt, raw = line, parsed = sample }, line));
        return id;
    }

    public void Sent(CockpitOutput.State state, int connection, double started, string status) {
        Enqueue(new Entry(1, new { send_id = Interlocked.Increment(ref sendId), tcp_connection = connection,
            write_started_s = started, write_completed_s = Now, write_status = status,
            input_id = state.InputId, source_session = state.Source?.Session, source_seq = state.Source?.Seq,
            sample_game_t = state.Source?.T, sample_real_t = state.Source?.RealT,
            active = state.Active, reason = state.Reason, frame = state.Frame }));
        if (status == "succeeded" && state.Active) Interlocked.Increment(ref validSent);
        if (status != "succeeded") Interlocked.Increment(ref failedWrites);
    }

    public void Connected(int connection) => Enqueue(new Entry(2,
        new { at_s = Now, kind = "pipe_connected", pipe_connection = connection, game = GameInfo() }));

    public void Boundary(int connection, string reason) => Enqueue(new Entry(2,
        new { at_s = Now, kind = "pipe_disconnected", pipe_connection = connection, reason }, "none"));

    void Enqueue(Entry entry) {
        if (!queue.Writer.TryWrite(entry) && Interlocked.Increment(ref dropped) == 1)
            Console.Error.WriteLine("[capture] records dropped; this capture is incomplete, telemetry continues");
    }

    async Task WriteLoop() {
        try {
            await foreach (var entry in queue.Reader.ReadAllAsync()) {
                files[entry.File].WriteLine(JsonSerializer.Serialize(entry.Data));
                if (entry.RawLine != null) files[3].WriteLine(entry.RawLine);
                if (entry.File == 0) writtenInputs++;
                if (entry.File == 1) writtenFrames++;
                if (!queue.Reader.TryPeek(out _)) foreach (var file in files) file.Flush();
            }
        } catch (Exception ex) {
            failure = ex.Message; queue.Writer.TryComplete();
            Console.Error.WriteLine("[capture] recording failed; telemetry continues, this capture is incomplete: " + failure);
        } finally {
            foreach (var file in files) {
                try { file.Dispose(); } catch (IOException ex) { failure ??= ex.Message; }
            }
        }
    }

    public async ValueTask DisposeAsync() {
        queue.Writer.TryComplete(); await writer;
        WriteJson("summary.json", new { complete = failure == null && dropped == 0,
            duration_s = Now, inputs = inputId, valid_inputs = validInputs, send_attempts = sendId, written_inputs = writtenInputs,
            written_frames = writtenFrames, valid_sent = validSent, failed_or_partial_writes = failedWrites,
            has_active_tcp_output = validSent > 0, dropped, faulted = failure != null, error = failure });
        Console.WriteLine($"[capture] saved: inputs={writtenInputs}, writes={writtenFrames}, valid_sent={validSent}, dropped={dropped}");
        if (mode == "cockpit" && validSent == 0)
            Console.WriteLine("[capture] no active flight frame was sent to a TCP client; not a complete outgoing-flight capture");
    }

    void WriteJson(string name, object value) {
        using var file = new FileStream(Path.Combine(DirectoryPath, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true });
    }

    static string? Hash(string? path) {
        try { return path == null || !File.Exists(path) ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    static object GameInfo() {
        Process[] games;
        try { games = Process.GetProcessesByName("ProjectWingman-Win64-Shipping"); }
        catch (Exception ex) { return new { error = ex.Message }; }
        try {
            return games.Select(game => {
                string? path = null, error = null;
                try { path = game.MainModule?.FileName; } catch (Exception ex) { error = ex.Message; }
                string? lua = path == null ? null : Path.Combine(Path.GetDirectoryName(path)!, "Mods", "PWProbe", "Scripts", "main.lua");
                return new { pid = game.Id, executable = path, installed_lua = lua, lua_sha256 = Hash(lua), error };
            }).ToArray();
        } finally { foreach (var game in games) game.Dispose(); }
    }
}
