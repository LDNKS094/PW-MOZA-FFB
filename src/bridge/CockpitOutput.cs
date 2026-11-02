using System.Net;
using System.Net.Sockets;
using System.Text;

// Same loopback DCS telemetry endpoint as the AC7 reference; no device/torque API.
sealed class CockpitOutput : IDisposable {
    readonly TcpListener listener;
    readonly object gate = new();
    string latest = FlightTelemetry.SafeFrame();
    long updatedAt;
    bool active;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public CockpitOutput(int port = 3476) {
        listener = new TcpListener(IPAddress.Loopback, port);
        try { listener.Start(); }
        catch (SocketException ex) {
            throw new IOException($"Cannot listen on 127.0.0.1:{port}; check for another DCS/AC7 bridge. No other process was stopped.", ex);
        }
    }

    public void Publish(string frame, bool valid) {
        lock (gate) {
            latest = valid ? frame : FlightTelemetry.SafeFrame();
            active = valid; updatedAt = Environment.TickCount64;
        }
    }

    public void Clear() => Publish(FlightTelemetry.SafeFrame(), false);

    public (string Frame, bool Active) Snapshot() {
        lock (gate) {
            bool fresh = active && Environment.TickCount64 - updatedAt <= 500;
            return (fresh ? latest : FlightTelemetry.SafeFrame(), fresh);
        }
    }

    public async Task Run(CancellationToken token) {
        try {
            while (!token.IsCancellationRequested) {
                Console.WriteLine($"[tcp] waiting for MOZA Cockpit on 127.0.0.1:{Port}...");
                using var client = await listener.AcceptTcpClientAsync(token);
                client.NoDelay = true;
                Console.WriteLine("[tcp] Cockpit client connected");
                using var stream = client.GetStream();
                try {
                    while (!token.IsCancellationRequested) {
                        var state = Snapshot();
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(500);
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(state.Frame), deadline.Token);
                        await Task.Delay(state.Active ? 16 : 50, token);
                    }
                } catch (Exception ex) when (ex is IOException or SocketException ||
                    ex is OperationCanceledException && !token.IsCancellationRequested) {
                    Console.WriteLine("[tcp] client disconnected or write timed out");
                } finally {
                    // Best effort only: on controlled shutdown send neutral telemetry before closing TCP.
                    try {
                        using var deadline = new CancellationTokenSource(200);
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(FlightTelemetry.SafeFrame()), deadline.Token);
                    } catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException) { }
                }
            }
        } finally { Clear(); listener.Stop(); }
    }

    public void Dispose() => listener.Stop();
}
