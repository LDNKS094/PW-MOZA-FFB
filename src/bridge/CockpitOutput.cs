using System.Net;
using System.Net.Sockets;
using System.Text;

// Same loopback DCS telemetry endpoint as the AC7 reference; no device/torque API.
sealed class CockpitOutput : IDisposable {
    public sealed record State(string Frame, bool Active, long InputId, Sample? Source, string Reason);
    readonly TcpListener listener;
    readonly SessionCapture? capture;
    readonly object gate = new();
    State latest = new(FlightTelemetry.SafeFrame(), false, 0, null, "initial");
    long updatedAt;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public CockpitOutput(int port = 3476, SessionCapture? capture = null) {
        this.capture = capture;
        listener = new TcpListener(IPAddress.Loopback, port);
        try { listener.Start(); }
        catch (SocketException ex) {
            throw new IOException($"Cannot listen on 127.0.0.1:{port}; check for another DCS/AC7 bridge. No other process was stopped.", ex);
        }
    }

    public void Publish(string frame, bool valid, long inputId, Sample? source, string reason) {
        lock (gate) {
            latest = new(valid ? frame : FlightTelemetry.SafeFrame(), valid, inputId, source, reason);
            updatedAt = Environment.TickCount64;
        }
    }

    public void Clear(string reason) => Publish(FlightTelemetry.SafeFrame(), false, 0, null, reason);

    public State Snapshot() {
        lock (gate) {
            return latest.Active && Environment.TickCount64 - updatedAt > 500
                ? latest with { Frame = FlightTelemetry.SafeFrame(), Active = false, Reason = "stale" }
                : latest;
        }
    }

    async Task Write(NetworkStream stream, State state, int connection, CancellationToken token) {
        double started = capture?.Now ?? 0;
        try {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(state.Frame), token);
            capture?.Sent(state, connection, started, "succeeded");
        } catch {
            capture?.Sent(state, connection, started, "failed_or_partial");
            throw;
        }
    }

    public async Task Run(CancellationToken token) {
        int connection = 0;
        try {
            while (!token.IsCancellationRequested) {
                Console.WriteLine($"[tcp] waiting for MOZA Cockpit on 127.0.0.1:{Port}...");
                using var client = await listener.AcceptTcpClientAsync(token);
                connection++;
                client.NoDelay = true;
                Console.WriteLine("[tcp] Cockpit client connected");
                using var stream = client.GetStream();
                try {
                    while (!token.IsCancellationRequested) {
                        var state = Snapshot();
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(500);
                        await Write(stream, state, connection, deadline.Token);
                        await Task.Delay(state.Active ? 16 : 50, token);
                    }
                } catch (Exception ex) when (ex is IOException or SocketException ||
                    ex is OperationCanceledException && !token.IsCancellationRequested) {
                    Console.WriteLine("[tcp] client disconnected or write timed out");
                } finally {
                    // Best effort only: on controlled shutdown send neutral telemetry before closing TCP.
                    try {
                        using var deadline = new CancellationTokenSource(200);
                        await Write(stream, new State(FlightTelemetry.SafeFrame(), false, 0, null, "tcp_final_neutral"),
                            connection, deadline.Token);
                    } catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException) { }
                }
            }
        } finally { Clear("shutdown"); listener.Stop(); }
    }

    public void Dispose() => listener.Stop();
}
