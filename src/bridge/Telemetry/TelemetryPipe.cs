using System.IO.Pipes;
using System.Text;

static class TelemetryPipe {
    public static async Task Receive(CancellationToken token, CockpitServer? cockpit) {
        while (!token.IsCancellationRequested) {
            Console.WriteLine("[telemetry] Waiting for PWTelemetry...");
            using var pipe = new NamedPipeServerStream("PWTelemetry", PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(token);
            Console.WriteLine("[telemetry] Connected; waiting for flight data.");
            var telemetry = new FlightTelemetry();
            long lastSequence = 0, lastSession = 0, freshAt = Environment.TickCount64;
            bool inactive = true;
            using var reader = new StreamReader(pipe, Encoding.ASCII, false, 4096, leaveOpen: true);
            var buffer = new char[FlightSample.MaxLine + 1];
            try {
                while (!token.IsCancellationRequested) {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    if (!inactive) deadline.CancelAfter((int)Math.Clamp(500 - (Environment.TickCount64 - freshAt), 1, 500));
                    string? line;
                    try { line = await ReadFrame(reader, buffer, deadline.Token); }
                    catch (OperationCanceledException error) when (!token.IsCancellationRequested) {
                        if (!inactive) ErrorLog.Write("Flight telemetry timed out", error);
                        break;
                    }
                    if (line == null) break;
                    FlightSample? sample = FlightSample.Parse(line);
                    if (sample == null) {
                        if (line != "none" && !line.StartsWith("v=1;event=destroyed;", StringComparison.Ordinal))
                            throw new InvalidDataException("Unrecognized flight telemetry frame.");
                        telemetry.Reset();
                        if (!inactive) {
                            Console.WriteLine("[telemetry] Flight data ended; flight output neutral.");
                        }
                        inactive = true;
                        cockpit?.Clear();
                    } else {
                        if (sample.Session == lastSession && sample.Seq <= lastSequence) continue;
                        lastSession = sample.Session; lastSequence = sample.Seq; freshAt = Environment.TickCount64;
                        if (inactive) Console.WriteLine("[telemetry] Receiving flight data.");
                        inactive = false;
                        if (cockpit != null) {
                            string frame = telemetry.Frame(sample);
                            cockpit.Publish(frame, telemetry.Active);
                        }
                    }
                    VibrationHost.Observe(sample, line);
                }
            } catch (InvalidDataException error) { ErrorLog.Write("Invalid flight telemetry", error); }
            catch (IOException) { } // The game closes its pipe on exit/reload.
            finally {
                VibrationHost.Observe(null); cockpit?.Clear();
                Console.WriteLine("[telemetry] Disconnected; flight output neutral.");
            }
        }
    }
    static async Task<string?> ReadFrame(StreamReader reader, char[] buffer, CancellationToken token) {
        int count = 0;
        while (await reader.ReadAsync(buffer.AsMemory(count, 1), token) != 0) {
            if (buffer[count] == '\n') return new string(buffer, 0, count).TrimEnd('\r');
            if (++count == buffer.Length) throw new InvalidDataException("Oversized telemetry frame.");
        }
        if (count != 0) throw new InvalidDataException("Truncated telemetry frame.");
        return null;
    }
}
