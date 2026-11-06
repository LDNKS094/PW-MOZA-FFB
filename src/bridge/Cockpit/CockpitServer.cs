using System.Net;
using System.Net.Sockets;
using System.Text;

sealed class CockpitServer : IDisposable {
    static readonly byte[] neutral = Encoding.ASCII.GetBytes(FlightTelemetry.SafeFrame());
    readonly TcpListener listener;
    readonly object gate = new();
    byte[] latest = neutral;
    bool active;
    long updatedAt;

    public CockpitServer() {
        listener = new TcpListener(IPAddress.Loopback, 3476);
        try { listener.Start(); }
        catch (SocketException error) {
            throw new IOException($"Binding 127.0.0.1:3476 failed: {error.SocketErrorCode} ({error.NativeErrorCode}).", error);
        }
    }
    public void Publish(string frame, bool valid) {
        byte[] bytes = valid ? Encoding.ASCII.GetBytes(frame) : neutral;
        lock (gate) { latest = bytes; active = valid; updatedAt = Environment.TickCount64; }
    }
    public void Clear() { lock (gate) { latest = neutral; active = false; } }
    (byte[] Frame, bool Active) Snapshot() {
        lock (gate) return active && Environment.TickCount64 - updatedAt <= 500 ? (latest, true) : (neutral, false);
    }
    public async Task Run(CancellationToken token) {
        try {
            while (!token.IsCancellationRequested) {
                Console.WriteLine("[cockpit] Waiting for MOZA Cockpit on 127.0.0.1:3476...");
                using var client = await listener.AcceptTcpClientAsync(token);
                Console.WriteLine("[cockpit] Connected (PW Default).");
                client.NoDelay = true;
                using var stream = client.GetStream();
                try {
                    while (!token.IsCancellationRequested) {
                        var state = Snapshot();
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(500);
                        await stream.WriteAsync(state.Frame, deadline.Token);
                        await Task.Delay(state.Active ? 16 : 50, token);
                    }
                } catch (OperationCanceledException error) when (!token.IsCancellationRequested) {
                    ErrorLog.Write("Cockpit write timed out", error);
                } catch (Exception error) when (error is IOException or SocketException) {
                    // Closing/restarting Cockpit is a normal connection boundary.
                } finally {
                    Console.WriteLine("[cockpit] Disconnected.");
                    try {
                        using var deadline = new CancellationTokenSource(200);
                        await stream.WriteAsync(neutral, deadline.Token);
                    } catch (Exception error) when (error is IOException or SocketException or OperationCanceledException) { }
                }
            }
        } finally { Clear(); listener.Stop(); }
    }
    public void Dispose() => listener.Stop();
}
