using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ACCoach
{
    public struct Sample
    {
        public int T;            // ms since lap start
        public float Pos;        // normalized position on track 0..1
        public float Speed, Gas, Brake, Clutch, Steer, LatG, LonG, X, Z;
        public int Gear, Rpm, TyresOut;
    }

    // A lap resampled onto a fixed distance grid (every Step metres), so laps can be compared point by point.
    // Can be filled progressively (live) or all at once (from a saved lap).
    public class Trace
    {
        public const float Step = 2f;
        public float Length;
        public int N;
        public float[] T, Spd, Gas, Brk, Str, X, Z;
        public int Filled;        // grid points [0, Filled) are valid

        bool hasPrev;
        float pd, pt, ps, pg, pb, pst, px, pz;

        public Trace(float length)
        {
            Length = length;
            N = (int)(length / Step) + 1;
            T = new float[N]; Spd = new float[N]; Gas = new float[N]; Brk = new float[N]; Str = new float[N]; X = new float[N]; Z = new float[N];
        }

        public float FilledDistance { get { return Filled == 0 ? -1 : (Filled - 1) * Step; } }

        // d in metres, t in seconds
        public void Add(float d, float t, float spd, float gas, float brk, float str, float x, float z)
        {
            if (d < 0) return;
            if (d > Length) d = Length;
            if (!hasPrev)
            {
                // Grid points before the first sample: hold values, extrapolate time backwards.
                float v = Math.Max(spd / 3.6f, 1f);
                pd = d; pt = t; ps = spd; pg = gas; pb = brk; pst = str; px = x; pz = z; hasPrev = true;
                int upto = (int)(d / Step);
                for (int i = Filled; i <= upto && i < N; i++)
                {
                    float gd = i * Step;
                    Set(i, t - (d - gd) / v, spd, gas, brk, str, x, z);
                }
                Filled = Math.Min(N, upto + 1);
                return;
            }
            if (d <= pd) return;
            int last = Math.Min(N - 1, (int)(d / Step));
            for (int i = Filled; i <= last; i++)
            {
                float gd = i * Step;
                float f = (gd - pd) / (d - pd);
                if (f < 0) f = 0;
                Set(i, pt + (t - pt) * f, ps + (spd - ps) * f, pg + (gas - pg) * f, pb + (brk - pb) * f, pst + (str - pst) * f, px + (x - px) * f, pz + (z - pz) * f);
            }
            if (last + 1 > Filled) Filled = last + 1;
            pd = d; pt = t; ps = spd; pg = gas; pb = brk; pst = str; px = x; pz = z;
        }

        void Set(int i, float t, float s, float g, float b, float st, float x, float z)
        {
            T[i] = t; Spd[i] = s; Gas[i] = g; Brk[i] = b; Str[i] = st; X[i] = x; Z[i] = z;
        }

        public int Idx(float d)
        {
            int i = (int)Math.Round(d / Step);
            if (i < 0) i = 0;
            if (i >= N) i = N - 1;
            return i;
        }

        public float TimeAt(float d) { return T[Idx(d)]; }
        public float SpeedAt(float d) { return Spd[Idx(d)]; }
    }

    public class Lap
    {
        public string File;
        public string Car = "", Track = "", Config = "";
        public int LapMs;
        public bool Valid;
        public float SplineLength;
        public DateTime Date;
        public List<Sample> Samples = new List<Sample>();
        public Trace Trace;
        public List<float> OffTrackD;   // where 3+ wheels left the track (metres); kept after Compact()

        // Keeps only the distance trace and the off-track spots, so hundreds of laps fit in memory.
        public void Compact(float length)
        {
            if (Trace == null || Math.Abs(Trace.Length - length) > 1) BuildTrace(length);
            OffTrackD = new List<float>();
            for (int i = 0; i < Samples.Count; i++)
                if (Samples[i].TyresOut >= 3) OffTrackD.Add(UnwrapPos(Samples[i].Pos, i, Samples.Count) * length);
            Samples = new List<Sample>();
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Track length: the game's spline length when sane, otherwise integrated from speed.
        public float EstimateLength()
        {
            if (SplineLength > 200 && SplineLength < 50000) return SplineLength;
            double dist = 0;
            for (int i = 1; i < Samples.Count; i++)
                dist += Samples[i].Speed / 3.6 * (Samples[i].T - Samples[i - 1].T) / 1000.0;
            return (float)Math.Max(dist, 200);
        }

        // Position fixed up for the start/finish wrap (first samples may still read 0.99x, last ones 0.00x).
        public static float UnwrapPos(float pos, int index, int count)
        {
            if (index < count / 10 && pos > 0.5f) return pos - 1f;
            if (index > count * 9 / 10 && pos < 0.5f) return pos + 1f;
            return pos;
        }

        public void BuildTrace(float length)
        {
            if (Samples.Count == 0 && Trace != null) return;   // compacted: the trace is all there is
            Trace = new Trace(length);
            int n = Samples.Count;
            for (int i = 0; i < n; i++)
            {
                Sample s = Samples[i];
                float d = UnwrapPos(s.Pos, i, n) * length;
                Trace.Add(d, s.T / 1000f, s.Speed, s.Gas, s.Brake, s.Steer, s.X, s.Z);
            }
            if (n > 0)
            {
                Sample l = Samples[n - 1];
                Trace.Add(length, LapMs / 1000f, l.Speed, l.Gas, l.Brake, l.Steer, l.X, l.Z);
            }
        }

        public static string FormatTime(int ms)
        {
            if (ms <= 0) return "--:--.---";
            return string.Format(Inv, "{0}:{1:00}.{2:000}", ms / 60000, (ms / 1000) % 60, ms % 1000);
        }

        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(Inv, "# car={0};track={1};config={2};lap_ms={3};valid={4};spline={5};date={6}\n",
                Car, Track, Config, LapMs, Valid ? 1 : 0, SplineLength, Date.ToString("yyyy-MM-ddTHH:mm:ss", Inv));
            sb.Append("t_ms,pos,speed,gas,brake,clutch,steer,gear,rpm,lat_g,lon_g,x,z,tyres_out\n");
            foreach (Sample s in Samples)
            {
                sb.AppendFormat(Inv, "{0},{1:0.000000},{2:0.00},{3:0.000},{4:0.000},{5:0.000},{6:0.0000},{7},{8},{9:0.000},{10:0.000},{11:0.00},{12:0.00},{13}\n",
                    s.T, s.Pos, s.Speed, s.Gas, s.Brake, s.Clutch, s.Steer, s.Gear, s.Rpm, s.LatG, s.LonG, s.X, s.Z, s.TyresOut);
            }
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            File = path;
        }

        public static Lap Load(string path)
        {
            Lap lap = new Lap();
            lap.File = path;
            string[] lines = System.IO.File.ReadAllLines(path);
            foreach (string line in lines)
            {
                if (line.StartsWith("#"))
                {
                    foreach (string kv in line.Substring(1).Trim().Split(';'))
                    {
                        int eq = kv.IndexOf('=');
                        if (eq < 0) continue;
                        string k = kv.Substring(0, eq), v = kv.Substring(eq + 1);
                        if (k == "car") lap.Car = v;
                        else if (k == "track") lap.Track = v;
                        else if (k == "config") lap.Config = v;
                        else if (k == "lap_ms") lap.LapMs = int.Parse(v, Inv);
                        else if (k == "valid") lap.Valid = v == "1";
                        else if (k == "spline") lap.SplineLength = float.Parse(v, Inv);
                        else if (k == "date") DateTime.TryParse(v, Inv, DateTimeStyles.None, out lap.Date);
                    }
                    continue;
                }
                if (line.Length == 0 || char.IsLetter(line[0])) continue;
                string[] p = line.Split(',');
                if (p.Length < 14) continue;
                Sample s = new Sample();
                s.T = int.Parse(p[0], Inv);
                s.Pos = float.Parse(p[1], Inv);
                s.Speed = float.Parse(p[2], Inv);
                s.Gas = float.Parse(p[3], Inv);
                s.Brake = float.Parse(p[4], Inv);
                s.Clutch = float.Parse(p[5], Inv);
                s.Steer = float.Parse(p[6], Inv);
                s.Gear = int.Parse(p[7], Inv);
                s.Rpm = int.Parse(p[8], Inv);
                s.LatG = float.Parse(p[9], Inv);
                s.LonG = float.Parse(p[10], Inv);
                s.X = float.Parse(p[11], Inv);
                s.Z = float.Parse(p[12], Inv);
                s.TyresOut = int.Parse(p[13], Inv);
                lap.Samples.Add(s);
            }
            return lap;
        }

        public static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || char.IsControl(c) ? '_' : c);
            return sb.ToString();
        }

        public static string ComboDir(string dataDir, string car, string track, string config)
        {
            string name = SafeName(car) + "__" + SafeName(track);
            if (!string.IsNullOrEmpty(config)) name += "__" + SafeName(config);
            return Path.Combine(dataDir, name);
        }

        // compactLength > 0: compact every lap right after loading it (the live coach only needs traces)
        public static List<Lap> LoadAll(string comboDir, float compactLength = 0)
        {
            List<Lap> laps = new List<Lap>();
            string dir = Path.Combine(comboDir, "laps");
            if (!Directory.Exists(dir)) return laps;
            foreach (string f in Directory.GetFiles(dir, "*.csv"))
            {
                try
                {
                    Lap l = Lap.Load(f);
                    if (compactLength > 0) l.Compact(compactLength);
                    laps.Add(l);
                }
                catch (Exception) { }
            }
            laps.Sort(delegate (Lap a, Lap b) { return a.Date.CompareTo(b.Date); });
            return laps;
        }

        public static Lap BestValid(List<Lap> laps)
        {
            Lap best = null;
            foreach (Lap l in laps)
                if (l.Valid && l.LapMs > 0 && (best == null || l.LapMs < best.LapMs)) best = l;
            return best;
        }
    }
}
