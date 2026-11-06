static class ErrorLog {
    static readonly object gate = new();
    static readonly HashSet<string> reported = new(StringComparer.Ordinal);
    public static void Write(string context, Exception error) {
        string message = context + ": " + error.Message;
        lock (gate) {
            if (!reported.Add(message)) return;
            Console.Error.WriteLine(message);
            try {
                string directory = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"bridge-errors-{Environment.ProcessId}.log");
                File.AppendAllText(path,
                    $"{DateTimeOffset.Now:O} {context}{Environment.NewLine}{error}{Environment.NewLine}");
                Console.Error.WriteLine("Error log: " + path);
            } catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) {
                Console.Error.WriteLine("Error log write failed: " + logError.Message);
            }
        }
    }
}
