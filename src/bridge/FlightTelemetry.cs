// SPDX-License-Identifier: GPL-3.0-only
// Basis/projection, filtering and DCS field mapping adapted from ../AC7-MOZA-FFB/src/bridge/FlightTelemetry.cs.
using System.Globalization;

sealed class FlightTelemetry {
    Sample? previous;
    (double x, double y, double z)? previousVelocity;
    double velocityTime, ax, ay = 1, az, pitchRate, yawRate, rollRate;
    public string Reason { get; private set; } = "inactive";

    public void Reset(string reason) {
        previous = null; previousVelocity = null;
        velocityTime = ax = az = pitchRate = yawRate = rollRate = 0;
        ay = 1; Reason = reason;
    }

    string Reseed(Sample sample, string reason) {
        Reset(reason); previous = sample;
        return SafeFrame();
    }

    public string Frame(Sample sample) {
        if (!sample.Valid) { Reset("invalid"); return SafeFrame(); }
        if (previous == null || previous.Session != sample.Session) return Reseed(sample, "warmup");
        if (sample.Seq <= previous.Seq) { Reset("sequence"); return SafeFrame(); }
        double dt = sample.T - previous.T, realDt = sample.RealT - previous.RealT;
        if (dt < 1e-4 || dt > .25 || realDt < 1e-4 || realDt > .25 || Math.Abs(realDt - dt) > .1)
            return Reseed(sample, "time_gap");
        double dx = sample.X - previous.X, dy = sample.Y - previous.Y, dz = sample.Z - previous.Z;
        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        // Diagnostic sanity gate, not PW/hardware calibration: reject position jumps before differentiation.
        if (!double.IsFinite(distance) || distance / dt > 2 * Math.Max(sample.Forward, previous.Forward) + 50)
            return Reseed(sample, "position_jump");
        if (distance < 1e-4 && sample.Forward > 1) {
            Reason = "stale_position"; return SafeFrame();
        }
        // Tick phase jitters displacement/dt; use verified game speed with measured travel direction instead.
        double scale = distance >= 1e-4 ? sample.Forward / distance : 0;
        double vx = dx * scale, vy = dy * scale, vz = dz * scale, speed = sample.Forward;
        if (previousVelocity is not { } last) {
            previousVelocity = (vx, vy, vz); velocityTime = sample.T; previous = sample;
            Reason = "warmup"; return SafeFrame();
        }
        double accelerationDt = sample.T - velocityTime;
        double wx = (vx - last.x) / accelerationDt, wy = (vy - last.y) / accelerationDt, wz = (vz - last.z) / accelerationDt;
        if (!double.IsFinite(wx + wy + wz) || Math.Sqrt(wx * wx + wy * wy + wz * wz) > 50 * 9.81)
            return Reseed(sample, "acceleration_jump");
        wz += 9.81;
        var f = Fwd(sample.Pitch, sample.Yaw); var r = Right(sample.Pitch, sample.Yaw, sample.Roll);
        var u = Up(sample.Pitch, sample.Yaw, sample.Roll);
        double k = Math.Min(1, dt / .12);
        ax += (Math.Clamp(Dot(f, wx, wy, wz) / 9.81, -4, 4) - ax) * k;
        ay += (Math.Clamp(Dot(u, wx, wy, wz) / 9.81, -4, 12) - ay) * k;
        az += (Math.Clamp(Dot(r, wx, wy, wz) / 9.81, -4, 4) - az) * k;
        k = Math.Min(1, dt / .08);
        pitchRate += (Math.Clamp(Rad(Wrap(sample.Pitch - previous.Pitch)) / dt, -20, 20) - pitchRate) * k;
        yawRate += (Math.Clamp(Rad(Wrap(sample.Yaw - previous.Yaw)) / dt, -20, 20) - yawRate) * k;
        rollRate += (Math.Clamp(Rad(Wrap(sample.Roll - previous.Roll)) / dt, -20, 20) - rollRate) * k;
        previous = sample; previousVelocity = (vx, vy, vz); velocityTime = sample.T; Reason = "ok";
        double vf = Dot(f, vx, vy, vz), vu = Dot(u, vx, vy, vz), vr = Dot(r, vx, vy, vz);
        double aoa = vf > 1 ? Math.Atan2(-vu, vf) * 180 / Math.PI : 0;
        double aos = vf > 1 ? Math.Atan2(vr, vf) * 180 / Math.PI : 0;
        string I(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        // Like the reference, DCS TAS/IAS use an explicit game-speed proxy; no air-data source is available.
        return "aircraft_name,PW Default;" +
            $"acc_x,{I(ax)};acc_y,{I(ay)};acc_z,{I(az)};" +
            $"vector_velocity_x,{I(vx)};vector_velocity_y,{I(vz)};vector_velocity_z,{I(vy)};" +
            $"tas,{I(speed)};ias,{I(speed)};vertical_velocity_speed,{I(vz)};aoa,{I(aoa)};aos,{I(aos)};" +
            $"heading,{I(Rad(sample.Yaw))};pitch,{I(Rad(sample.Pitch))};bank,{I(Rad(sample.Roll))};" +
            $"euler_vx,{I(rollRate)};euler_vy,{I(yawRate)};euler_vz,{I(pitchRate)};";
    }

    public static string SafeFrame() => "aircraft_name,PW Default;acc_x,0;acc_y,1;acc_z,0;" +
        "vector_velocity_x,0;vector_velocity_y,0;vector_velocity_z,0;tas,0;ias,0;vertical_velocity_speed,0;" +
        "aoa,0;aos,0;heading,0;pitch,0;bank,0;euler_vx,0;euler_vy,0;euler_vz,0;";
    static double Rad(double d) => d * Math.PI / 180;
    static double Wrap(double d) => ((d + 180) % 360 + 360) % 360 - 180;
    static (double x, double y, double z) Fwd(double p, double y) {
        p = Rad(p); y = Rad(y); return (Math.Cos(p) * Math.Cos(y), Math.Cos(p) * Math.Sin(y), Math.Sin(p));
    }
    static (double x, double y, double z) Up(double p, double y, double r) {
        p = Rad(p); y = Rad(y); r = Rad(r);
        double cp = Math.Cos(p), sp = Math.Sin(p), cy = Math.Cos(y), sy = Math.Sin(y), cr = Math.Cos(r), sr = Math.Sin(r);
        return (-(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp);
    }
    static (double x, double y, double z) Right(double p, double y, double r) {
        p = Rad(p); y = Rad(y); r = Rad(r);
        double cp = Math.Cos(p), sp = Math.Sin(p), cy = Math.Cos(y), sy = Math.Sin(y), cr = Math.Cos(r), sr = Math.Sin(r);
        return (sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, -sr * cp);
    }
    static double Dot((double x, double y, double z) a, double x, double y, double z) => a.x * x + a.y * y + a.z * z;
}
