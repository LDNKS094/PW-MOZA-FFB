using System.Globalization;

// Wire v1: world positions in metres, speed in m/s, rotations in degrees, times in seconds.
sealed record FlightSample(long Session, long Seq, double T, double RealT,
    double X, double Y, double Z, double Pitch, double Yaw, double Roll, double Forward) {
    static readonly string[] Keys = ["v", "s", "q", "t", "rt", "x", "y", "z", "p", "yaw", "r", "spd", "hp", "dead"];
    public const int MaxLine = 2048;
    public double Health { get; init; } = double.NaN;
    bool InRange => T >= 0 && RealT >= 0 &&
        T < 1e9 && RealT < 1e9 && Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z))) < 1e9 &&
        Math.Abs(Pitch) <= 90.01 && Math.Abs(Yaw) <= 360.01 && Math.Abs(Roll) <= 360.01 && Forward is >= 0 and <= 5000;

    public static FlightSample? Parse(string line) {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string field in line.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            var pair = field.Split('=');
            if (pair.Length != 2 || !Keys.Contains(pair[0]) ||
                !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ||
                !double.IsFinite(v) || !values.TryAdd(pair[0], v)) return null;
        }
        if (values.Count != Keys.Length || values["v"] != 1 || values["hp"] <= 0 || values["dead"] != 0) return null;
        bool Id(double v) => v >= 1 && v <= 9007199254740991 && v == Math.Truncate(v);
        if (!Id(values["s"]) || !Id(values["q"])) return null;
        var sample = new FlightSample((long)values["s"], (long)values["q"], values["t"], values["rt"],
            values["x"], values["y"], values["z"], values["p"], values["yaw"], values["r"], values["spd"]) { Health = values["hp"] };
        return sample.InRange ? sample : null;
    }
}
