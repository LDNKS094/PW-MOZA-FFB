using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

static class Program {
    public static async Task<int> Main(string[] args) {
        try {
            if (args is ["--self-test"]) { await SelfTest.Run(); return 0; }
            if (args is ["--replay", var path]) { await Replay(path); return 0; }
            bool cockpit = args is ["--cockpit"] || args.Length == 0 && typeof(Program).Assembly.GetName().Name == "DCS";
            if (args.Length != 0 && args is not ["--cockpit"]) {
                Console.Error.WriteLine("PWBridge [--cockpit | --self-test | --replay capture.txt]"); return 2;
            }
            using var stop = new CancellationTokenSource();
            string directory = Path.Combine(AppContext.BaseDirectory, "captures",
                "session-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
            await using var capture = new SessionCapture(directory, cockpit ? "cockpit" : "diagnostic");
            Console.WriteLine("[capture] session: " + capture.DirectoryPath);
            using var output = cockpit ? new CockpitOutput(capture: capture) : null;
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
            Console.CancelKeyPress += cancel;
            try {
                Task pipe = Receive(stop.Token, output, capture, "PWTelemetry");
                if (output == null) await pipe;
                else {
                    Task tcp = output.Run(stop.Token);
                    try { Task completed = await Task.WhenAny(pipe, tcp); await completed; }
                    finally {
                        stop.Cancel();
                        try { await Task.WhenAll(pipe, tcp); }
                        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                    }
                }
            } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally { Console.CancelKeyPress -= cancel; }
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine("[bridge] " + ex.Message); return 1; }
    }

    // ReadLineAsync has no length limit; bound allocations and reject truncated pipe frames.
    public static async Task<string?> ReadLine(StreamReader reader, CancellationToken token) {
        var chars = new char[Sample.MaxLine + 1];
        int count = 0;
        while (await reader.ReadAsync(chars.AsMemory(count, 1), token) != 0) {
            if (chars[count] == '\n') return new string(chars, 0, count).TrimEnd('\r');
            if (++count > Sample.MaxLine) throw new InvalidDataException("oversized frame");
        }
        if (count != 0) throw new InvalidDataException("truncated frame");
        return null;
    }

    internal static async Task Receive(CancellationToken token, CockpitOutput? output, SessionCapture capture, string pipeName) {
        var clock = Stopwatch.StartNew();
        long lastReport = -1000;
        int connection = 0;
        Console.WriteLine(output == null
            ? "[bridge] diagnostic input recording; no Cockpit output"
            : $"[bridge] PW telemetry -> MOZA Cockpit; DCS port {output.Port}");
        while (!token.IsCancellationRequested) {
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            Console.WriteLine("[pipe] waiting for game mod...");
            await pipe.WaitForConnectionAsync(token);
            Console.WriteLine("[pipe] game mod connected");
            capture.Connected(++connection);
            string endReason = "pipe_closed";
            var telemetry = new FlightTelemetry();
            long lastSeq = 0, lastSession = 0, freshAt = clock.ElapsedMilliseconds;
            string lastReason = "";
            using var reader = new StreamReader(pipe, Encoding.ASCII, false, 4096, leaveOpen: true);
            try {
                while (!token.IsCancellationRequested) {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    // Duplicates do not extend the freshness deadline.
                    deadline.CancelAfter((int)Math.Clamp(500 - (clock.ElapsedMilliseconds - freshAt), 1, 500));
                    string? line;
                    try { line = await ReadLine(reader, deadline.Token); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                        endReason = "timeout";
                        Console.WriteLine("[pipe] telemetry timed out; cleared"); break;
                    }
                    if (line == null) break;
                    double receivedAt = capture.Now;
                    Sample? sample = Sample.Parse(line);
                    long inputId = capture.Input(line, sample, connection, receivedAt);
                    string frame;
                    if (sample == null) {
                        telemetry.Reset("inactive_or_invalid"); frame = FlightTelemetry.SafeFrame();
                    } else if (sample.Session == lastSession && sample.Seq <= lastSeq) {
                        if (clock.ElapsedMilliseconds - freshAt >= 500) break;
                        continue;
                    } else {
                        lastSession = sample.Session; lastSeq = sample.Seq; freshAt = clock.ElapsedMilliseconds;
                        frame = telemetry.Frame(sample);
                    }
                    output?.Publish(frame, telemetry.Reason == "ok", inputId, sample, telemetry.Reason);
                    if (output == null && (clock.ElapsedMilliseconds - lastReport >= 1000 || lastReason != telemetry.Reason)) {
                        Console.WriteLine($"[bridge] state={telemetry.Reason} {frame}");
                        lastReport = clock.ElapsedMilliseconds; lastReason = telemetry.Reason;
                    }
                }
            } catch (IOException ex) { endReason = "io_error"; Console.WriteLine("[pipe] " + ex.Message); }
            finally {
                telemetry.Reset("disconnected"); output?.Clear(endReason);
                capture.Boundary(connection, token.IsCancellationRequested ? "shutdown" : endReason);
                Console.WriteLine("[pipe] game mod disconnected");
            }
        }
    }

    static async Task Replay(string path) {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.ASCII);
        var telemetry = new FlightTelemetry();
        var counts = new Dictionary<string, int>();
        string? line;
        long lastSeq = 0, lastSession = 0;
        while ((line = await ReadLine(reader, CancellationToken.None)) != null) {
            var sample = Sample.Parse(line);
            if (sample == null) { telemetry.Reset("inactive_or_invalid"); lastSeq = lastSession = 0; }
            else if (sample.Session == lastSession && sample.Seq <= lastSeq) {
                counts["duplicate"] = counts.GetValueOrDefault("duplicate") + 1;
                continue;
            } else {
                lastSession = sample.Session; lastSeq = sample.Seq;
                telemetry.Frame(sample);
            }
            counts[telemetry.Reason] = counts.GetValueOrDefault(telemetry.Reason) + 1;
        }
        Console.WriteLine("[replay] " + string.Join(", ", counts.Select(x => $"{x.Key}={x.Value}")));
    }
}
