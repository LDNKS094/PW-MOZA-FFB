static class Program {
    static async Task<int> Main(string[] args) {
        if (args.Length != 0 && args is not ["--vibration-only"]) {
            Console.Error.WriteLine("Usage: DCS.exe [--vibration-only]");
            WaitBeforeExit();
            return 2;
        }
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try {
            Console.WriteLine("PW MOZA Flight Feedback | " + (args.Length == 0 ? "Flight forces + vibration" : "Vibration only") + " | Vibration gain 1.5 | Ctrl+C exits");
            using var cockpit = args.Length == 0 ? new CockpitServer() : null;
            var tasks = new List<Task> { TelemetryPipe.Receive(stop.Token, cockpit), VibrationHost.Watch(stop.Token) };
            if (cockpit != null) tasks.Add(cockpit.Run(stop.Token));
            try { await await Task.WhenAny(tasks); }
            finally {
                stop.Cancel();
                try { await Task.WhenAll(tasks); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
            return 0;
        } catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception error) {
            ErrorLog.Write("Bridge stopped", error);
            WaitBeforeExit();
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
    static void WaitBeforeExit() {
        if (Console.IsInputRedirected) return;
        Console.Error.WriteLine("Press any key to close this window...");
        Console.ReadKey(intercept: true);
    }
}
