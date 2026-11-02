using System.Globalization;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

static class SelfTest {
    static void Check(bool value, string message) { if (!value) throw new Exception("self-test: " + message); }
    static void Near(double a, double b, double tolerance, string message) => Check(Math.Abs(a - b) <= tolerance, message + $" ({a} vs {b})");
    static string Wire(int q = 1) => FormattableString.Invariant($"v=1;s=1;q={q};t={q * .02};rt={q * .02};x={q * 2};y=0;z=780;p=0;yaw=0;r=0;spd=100;hp=100;dead=0;");
    static Sample At(int q) => Sample.Parse(Wire(q))!;
    static double Field(string frame, string name) => double.Parse(frame.Split(';').Single(x => x.StartsWith(name + ",", StringComparison.Ordinal)).Split(',')[1], CultureInfo.InvariantCulture);
    static void Numeric(string frame) {
        foreach (string field in frame.Split(';', StringSplitOptions.RemoveEmptyEntries).Skip(1))
            Check(double.IsFinite(double.Parse(field.Split(',')[1], CultureInfo.InvariantCulture)), "nonfinite output");
    }

    public static async Task Run() {
        var culture = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Check(Sample.Parse(Wire()) != null, "invariant parser");
            foreach (string bad in new[] { "none", "", Wire().Replace("x=2;", ""), Wire() + "x=2;", Wire() + "extra=1;",
                Wire().Replace("x=2;", "x=NaN;"), Wire().Replace("x=2;", "x=Infinity;"), Wire().Replace("v=1;", "v=2;"),
                Wire().Replace("dead=0", "dead=1"), Wire().Replace("hp=100", "hp=0"), Wire().Replace("s=1;", "s=1.5;"),
                Wire().Replace("spd=100", "spd=-1"), new string('x', Sample.MaxLine + 1) })
                Check(Sample.Parse(bad) == null, "reject bad input " + bad[..Math.Min(30, bad.Length)]);
            var telemetry = new FlightTelemetry();
            Check(telemetry.Frame(At(1)) == FlightTelemetry.SafeFrame(), "first sample warmup");
            telemetry.Frame(At(2));
            string frame = telemetry.Frame(At(3));
            Check(telemetry.Reason == "ok", "steady flight valid"); Numeric(frame);
            Near(Field(frame, "tas"), 100, 1e-5, "m/s speed");
            Near(Field(frame, "acc_y"), 1, 1e-5, "level flight 1G");
            Near(Field(frame, "acc_x"), 0, 1e-5, "no longitudinal acceleration");
            Near(Field(frame, "ias"), 100, 1e-5, "IAS game-speed proxy matches reference");
            Check(!frame.Contains("engine_rpm") && !frame.Contains("cannon_shells"), "unknown fields omitted");
            telemetry.Reset("test"); telemetry.Frame(At(1));
            telemetry.Frame(At(2) with { X = 3 }); frame = telemetry.Frame(At(3) with { X = 7 });
            Check(telemetry.Reason == "ok", "tick phase jitter must not cause acceleration resets");
            Near(Field(frame, "tas"), 100, 1e-5, "game speed magnitude despite pose jitter");
            Near(Field(frame, "acc_x"), 0, 1e-5, "pose phase jitter creates no false longitudinal G");
            telemetry.Reset("test"); telemetry.Frame(At(1) with { X = 0 });
            telemetry.Frame(At(2) with { X = 0 });
            Check(telemetry.Reason == "stale_position", "stale pose must not create zero velocity");
            telemetry.Frame(At(3)); frame = telemetry.Frame(At(4));
            Check(telemetry.Reason == "ok", "recover after stale pose");
            Near(Field(frame, "acc_x"), 0, 1e-5, "no stale pose recovery impulse");
            telemetry.Frame(At(4) with { Session = 2 }); Check(telemetry.Reason == "warmup", "session reset");
            telemetry.Frame(At(4) with { Session = 2 }); Check(telemetry.Reason == "sequence", "duplicate reset");
            telemetry.Reset("test"); telemetry.Frame(At(1));
            telemetry.Frame(At(2) with { T = 2, RealT = 2 }); Check(telemetry.Reason == "time_gap", "long gap");
            telemetry.Reset("test"); telemetry.Frame(At(1));
            telemetry.Frame(At(2) with { RealT = 5 }); Check(telemetry.Reason == "time_gap", "pause gap");
            telemetry.Reset("test"); telemetry.Frame(At(1));
            telemetry.Frame(At(2) with { T = 0 }); Check(telemetry.Reason == "time_gap", "backward clock");
            telemetry.Reset("test"); telemetry.Frame(At(1));
            telemetry.Frame(At(2) with { X = 100000 }); Check(telemetry.Reason == "position_jump", "teleport");
            telemetry.Reset("test"); telemetry.Frame(At(1)); telemetry.Frame(At(2));
            telemetry.Frame(At(3) with { Forward = 120 }); Check(telemetry.Reason == "acceleration_jump", "acceleration spike");
            Check(telemetry.Frame(At(4) with { X = double.NaN }) == FlightTelemetry.SafeFrame(), "invalid record reset");
            telemetry.Reset("test");
            telemetry.Frame(At(1) with { Yaw = 179.8 }); telemetry.Frame(At(2) with { Yaw = 179.9 });
            frame = telemetry.Frame(At(3) with { Yaw = -179.9 });
            Check(telemetry.Reason == "ok" && Math.Abs(Field(frame, "euler_vy")) < 1, "angle wrap");
            telemetry.Reset("test");
            telemetry.Frame(At(1) with { Yaw = 360 }); telemetry.Frame(At(2) with { Yaw = 360 });
            frame = telemetry.Frame(At(3) with { Yaw = -360 });
            Near(Field(frame, "euler_vy"), 0, 1e-5, "full-circle wrap");
            telemetry.Reset("test");
            for (int q = 1; q <= 100; q++) {
                double t = q * .02;
                frame = telemetry.Frame(At(q) with { X = 100 * t + .5 * 9.81 * t * t, Forward = 100 + 9.81 * t });
                Numeric(frame);
            }
            Near(Field(frame, "acc_x"), 1, .01, "constant acceleration");
            telemetry.Reset("test");
            for (int q = 1; q <= 100; q++) frame = telemetry.Frame(At(q) with { Roll = 90 });
            Near(Field(frame, "acc_y"), 0, .01, "banked vertical projection");
            Near(Field(frame, "acc_z"), -1, .01, "banked lateral projection");
            await FramingTests();
            await CockpitTests();
            await PipelineCaptureTest();
            Console.WriteLine("PASS: telemetry math, pipe/TCP, raw-to-sent correlation, stale/shutdown recording, provenance, no-client detection; temporary captures cleaned");
        } finally { CultureInfo.CurrentCulture = culture; }
    }

    static async Task CockpitTests() {
        string directory = Path.Combine(Path.GetTempPath(), "PWCapture-Test-" + Guid.NewGuid().ToString("N"));
        try {
            await using (var capture = new SessionCapture(directory, "cockpit")) {
                using var output = new CockpitOutput(0, capture); // Never bind Cockpit's discovery port in tests.
                Check(output.Snapshot().Frame == FlightTelemetry.SafeFrame(), "initial TCP state");
                bool conflict = false;
                try { using var other = new CockpitOutput(output.Port); } catch (IOException) { conflict = true; }
                Check(conflict, "port conflict must not pick a different endpoint");
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                Task service = output.Run(stop.Token);
                var telemetry = new FlightTelemetry();
                capture.Connected(1);
                for (int q = 1; q <= 3; q++) capture.Input(Wire(q), At(q), 1, capture.Now);
                telemetry.Frame(At(1)); telemetry.Frame(At(2));
                string active = telemetry.Frame(At(3));
                try {
                    using (var client = new TcpClient()) {
                        await client.ConnectAsync(IPAddress.Loopback, output.Port, stop.Token);
                        using var stream = client.GetStream();
                        await ReadUntil(stream, FlightTelemetry.SafeFrame());
                        output.Publish(active, true, 3, At(3), "ok");
                        await ReadUntil(stream, active);
                        output.Clear("test_clear");
                        Check(!output.Snapshot().Active, "clear TCP activity");
                        await ReadUntil(stream, FlightTelemetry.SafeFrame());
                        output.Publish(active, true, 3, At(3), "ok");
                        await ReadUntil(stream, active);
                        await Task.Delay(550);
                        Check(!output.Snapshot().Active, "500ms TCP freshness gate");
                        await ReadUntil(stream, FlightTelemetry.SafeFrame());
                    }
                    using var reconnected = new TcpClient();
                    await reconnected.ConnectAsync(IPAddress.Loopback, output.Port, stop.Token);
                    output.Publish(active, true, 3, At(3), "ok");
                    using var next = reconnected.GetStream();
                    await ReadUntil(next, active);
                    stop.Cancel();
                    await ReadUntil(next, FlightTelemetry.SafeFrame());
                } finally {
                    stop.Cancel();
                    try { await service; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                    capture.Boundary(1, "test_complete");
                }
            }
            var raw = File.ReadAllLines(Path.Combine(directory, "raw.jsonl"))
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
            var frames = File.ReadAllLines(Path.Combine(directory, "frames.jsonl"))
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
            Check(raw.Length == 3 && raw[2].GetProperty("input_id").GetInt64() == 3, "raw input IDs");
            Check(raw[2].GetProperty("raw").GetString() == Wire(3), "raw bytes preserved");
            var activeFrames = frames.Where(f => f.GetProperty("active").GetBoolean()).ToArray();
            Check(activeFrames.Length > 1, "duplicate sends retained, not recomputed");
            foreach (var frame in activeFrames) {
                Check(frame.GetProperty("input_id").GetInt64() == 3 && frame.GetProperty("source_seq").GetInt64() == 3, "sent/source identity");
                Near(frame.GetProperty("sample_game_t").GetDouble(), .06, 1e-9, "sent/source clock");
                Check(frame.GetProperty("write_started_s").GetDouble() >= raw[2].GetProperty("received_s").GetDouble(), "shared monotonic clock");
                Check(frame.GetProperty("frame").GetString()!.Contains("tas,100;", StringComparison.Ordinal), "actual frame text");
            }
            Check(frames.Any(f => f.GetProperty("reason").GetString() == "stale" && !f.GetProperty("active").GetBoolean()), "stale output recorded");
            Check(frames.Any(f => f.GetProperty("reason").GetString() == "tcp_final_neutral"), "final neutral recorded");
            var summary = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(directory, "summary.json")));
            Check(summary.GetProperty("complete").GetBoolean() && summary.GetProperty("dropped").GetInt64() == 0, "capture integrity");
            Check(summary.GetProperty("valid_sent").GetInt64() > 0, "active TCP output observed");
            var metadata = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(directory, "metadata.json")));
            Check(metadata.GetProperty("source_sha256").TryGetProperty("FlightTelemetry", out var hash) && hash.GetString()!.Length == 64, "compiled source provenance");
            Check(metadata.GetProperty("program_sha256").GetString() == metadata.GetProperty("archived_sha256").GetString(), "archived program identity");
            Check(File.ReadAllLines(Path.Combine(directory, "capture.txt")).Last() == "none", "legacy replay boundary");
            bool refused = false;
            try { await using var duplicate = new SessionCapture(directory, "cockpit"); } catch (IOException) { refused = true; }
            Check(refused, "must not overwrite old capture files");
            string noClient = Path.Combine(directory, "no-client");
            await using (var capture = new SessionCapture(noClient, "cockpit")) capture.Input(Wire(), At(1), 1, capture.Now);
            var idle = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(noClient, "summary.json")));
            Check(!idle.GetProperty("has_active_tcp_output").GetBoolean(), "input-only capture must not count as sent data");
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    static async Task PipelineCaptureTest() {
        string directory = Path.Combine(Path.GetTempPath(), "PWPipeline-Test-" + Guid.NewGuid().ToString("N"));
        try {
            await using (var capture = new SessionCapture(directory, "cockpit")) {
                using var output = new CockpitOutput(0, capture);
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                string pipeName = "PWPipeline-" + Guid.NewGuid().ToString("N");
                Task receiver = Program.Receive(stop.Token, output, capture, pipeName);
                Task sender = output.Run(stop.Token);
                try {
                    using var cockpit = new TcpClient();
                    await cockpit.ConnectAsync(IPAddress.Loopback, output.Port, stop.Token);
                    using var network = cockpit.GetStream();
                    var telemetry = new FlightTelemetry();
                    telemetry.Frame(At(1)); telemetry.Frame(At(2));
                    string expected = telemetry.Frame(At(3));
                    using (var source = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous)) {
                        await source.ConnectAsync(stop.Token);
                        using var writer = new StreamWriter(source, Encoding.ASCII, 4096, leaveOpen: true) { AutoFlush = true };
                        for (int q = 1; q <= 3; q++) await writer.WriteLineAsync(Wire(q).AsMemory(), stop.Token);
                        await ReadUntil(network, expected);
                        Check(output.Snapshot().InputId == 3 && output.Snapshot().Source?.Seq == 3, "live receiver source correlation");
                    }
                    await ReadUntil(network, FlightTelemetry.SafeFrame());
                } finally {
                    stop.Cancel();
                    try { await Task.WhenAll(receiver, sender); }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                }
            }
            var raw = File.ReadAllLines(Path.Combine(directory, "raw.jsonl"));
            var frames = File.ReadAllLines(Path.Combine(directory, "frames.jsonl"))
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
            Check(raw.Length == 3, "live pipeline input recording");
            Check(frames.Any(f => f.GetProperty("active").GetBoolean() &&
                f.GetProperty("input_id").GetInt64() == 3 && f.GetProperty("source_seq").GetInt64() == 3 &&
                f.GetProperty("write_status").GetString() == "succeeded"), "live socket frame recorded after successful write");
            var summary = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(directory, "summary.json")));
            Check(summary.GetProperty("complete").GetBoolean() && summary.GetProperty("valid_sent").GetInt64() > 0, "live pipeline capture complete");
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    static async Task ReadUntil(NetworkStream stream, string expected) {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[4096]; var text = new StringBuilder();
        while (text.Length < 65536) {
            int count = await stream.ReadAsync(buffer, limit.Token);
            Check(count != 0, "TCP closed before expected frame");
            text.Append(Encoding.ASCII.GetString(buffer, 0, count));
            if (text.ToString().Contains(expected, StringComparison.Ordinal)) return;
        }
        Check(false, "expected TCP frame not found");
    }

    static async Task FramingTests() {
        foreach (string bad in new[] { new string('x', Sample.MaxLine + 1) + "\n", "partial" }) {
            using var r = new StreamReader(new MemoryStream(Encoding.ASCII.GetBytes(bad)));
            bool rejected = false;
            try { await Program.ReadLine(r, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "bounded/truncated frame");
        }
        string name = "PWTelemetry-Test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task wait = server.WaitForConnectionAsync(deadline.Token);
        await client.ConnectAsync(deadline.Token); await wait;
        using var writer = new StreamWriter(client, Encoding.ASCII, 4096, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(server, Encoding.ASCII, false, 4096, leaveOpen: true);
        Task<string?> incoming = Program.ReadLine(reader, deadline.Token);
        await writer.WriteLineAsync(Wire().AsMemory(), deadline.Token);
        Check(Sample.Parse((await incoming)!) != null, "actual named pipe roundtrip");
        using var idle = new CancellationTokenSource(100);
        bool cancelled = false;
        try { await Program.ReadLine(reader, idle.Token); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "idle read cancellation");
    }
}
