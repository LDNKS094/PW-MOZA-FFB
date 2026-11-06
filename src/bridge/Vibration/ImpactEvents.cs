using System.Globalization;

// Flight frames and terminal events both come from the locally controlled pawn.
sealed class ImpactEvents {
    long session, sequence;
    double time, realTime, health = double.NaN;
    public void Reset() { session = sequence = 0; health = double.NaN; }
    public Hit? Observe(FlightSample? sample, long nowMs, string? line = null) {
        if (sample == null && line is not null and not "none") {
            if (!Destroyed(line, out long deadSession, out long deadSequence, out double deadTime, out double deadReal))
                throw new InvalidDataException("Malformed aircraft destruction event.");
            bool terminal = deadSession == session && sequence > 0 && deadSequence > sequence
                && deadTime - time is >= 0 and <= .3 && deadReal - realTime is >= 0 and <= .5;
            Reset();
            return terminal ? new Hit(nowMs, 1, ImpactKind.Destroyed) : null;
        }
        if (sample == null) { Reset(); return null; }
        double elapsed = sample.T - time, loss = health - sample.Health;
        bool damage = sample.Session == session && sequence > 0 && elapsed is > 0 and <= .3 && loss > 0;
        double ratio = damage ? loss / health : 0;
        session = sample.Session; sequence = sample.Seq; time = sample.T; realTime = sample.RealT; health = sample.Health;
        return damage ? new Hit(nowMs, (float)(.25 + .75 * Math.Sqrt(ratio)), ratio >= .2 ? ImpactKind.HeavyDamage : ImpactKind.Damage) : null;
    }
    static bool Destroyed(string line, out long session, out long sequence, out double time, out double realTime) {
        session = sequence = 0; time = realTime = 0;
        string[] fields = line.Split(';', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length == 6 && fields[0] == "v=1" && fields[1] == "event=destroyed"
            && fields[2].StartsWith("s=", StringComparison.Ordinal) && long.TryParse(fields[2].AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out session) && session > 0
            && fields[3].StartsWith("q=", StringComparison.Ordinal) && long.TryParse(fields[3].AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out sequence) && sequence > 0
            && fields[4].StartsWith("t=", StringComparison.Ordinal) && double.TryParse(fields[4].AsSpan(2), NumberStyles.Float, CultureInfo.InvariantCulture, out time) && double.IsFinite(time) && time is >= 0 and < 1e9
            && fields[5].StartsWith("rt=", StringComparison.Ordinal) && double.TryParse(fields[5].AsSpan(3), NumberStyles.Float, CultureInfo.InvariantCulture, out realTime) && double.IsFinite(realTime) && realTime is >= 0 and < 1e9;
    }
    internal enum ImpactKind { Damage = 1, HeavyDamage = 2, Destroyed = 3 }
    internal readonly record struct Hit(long AtMs, float Strength, ImpactKind Kind = ImpactKind.Damage) {
        int Duration => Kind is ImpactKind.HeavyDamage or ImpactKind.Destroyed ? 800 : 200;
        public uint Packed => ((uint)Kind << 16) | (uint)Math.Round(Strength * 65535);
        public float Remaining(long nowMs) => AtMs > 0 && nowMs >= AtMs && nowMs - AtMs < Duration
            ? Strength * (Duration == 800 ? Math.Min(1, (Duration - (nowMs - AtMs)) / 600f)
                : 1 - (nowMs - AtMs) / (float)Duration) : 0;
    }
}
