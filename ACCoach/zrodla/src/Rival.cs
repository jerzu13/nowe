using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ACCoach
{
    // A rival's times from Assetto Corsa server result files (the JSON the server writes after each session),
    // dropped into the "rywale" folder. The fastest driver in the same car on the same track becomes the target.
    public class Rival
    {
        public string Name;
        public int BestLapMs;
        public int[] BestLapSectors;     // sectors of the best lap, ms
        public int[] BestSectors;        // best of each sector over all clean laps, ms
        public float[] AvgSectors;       // average over clean laps, ms
        public int Laps, CleanLaps, CutLaps;
        public string Source;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public int SectorCount { get { return BestLapSectors.Length; } }
        public int IdealMs { get { return BestSectors.Sum(); } }

        public static Rival Load(string dataDir, string car, string track, string config)
        {
            string dir = Path.Combine(dataDir, "rywale");
            if (!Directory.Exists(dir)) return null;
            // every lap of every driver in this car on this track, from all files
            var laps = new List<Tuple<string, int, int[], int, string>>();   // name, lap ms, sectors, cuts, file
            JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (string f in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    Dictionary<string, object> root = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8));
                    if (S(root, "TrackName") != track || (S(root, "TrackConfig") ?? "") != (config ?? "")) continue;
                    object lapsObj;
                    if (!root.TryGetValue("Laps", out lapsObj) || !(lapsObj is object[] || lapsObj is System.Collections.ArrayList)) continue;
                    foreach (object o in (System.Collections.IEnumerable)lapsObj)
                    {
                        Dictionary<string, object> l = o as Dictionary<string, object>;
                        if (l == null || S(l, "CarModel") != car) continue;
                        int ms = I(l, "LapTime");
                        object secObj;
                        if (ms <= 0 || !l.TryGetValue("Sectors", out secObj)) continue;
                        int[] sec = ((System.Collections.IEnumerable)secObj).Cast<object>().Select(x => Convert.ToInt32(x, Inv)).ToArray();
                        if (sec.Length < 2 || sec.Any(x => x <= 0)) continue;
                        laps.Add(Tuple.Create(S(l, "DriverName") ?? "?", ms, sec, I(l, "Cuts"), Path.GetFileName(f)));
                    }
                }
                catch (Exception) { }
            }
            if (laps.Count == 0) return null;

            // the driver with the fastest clean lap
            var bestLap = laps.Where(x => x.Item4 == 0).OrderBy(x => x.Item2).FirstOrDefault() ?? laps.OrderBy(x => x.Item2).First();
            string name = bestLap.Item1;
            var mine = laps.Where(x => x.Item1 == name && x.Item3.Length == bestLap.Item3.Length).ToList();
            // clean = no cuts and within 107% of the best (out laps, crashes and pit laps are left out)
            var clean = mine.Where(x => x.Item4 == 0 && x.Item2 <= bestLap.Item2 * 1.07).ToList();
            if (clean.Count == 0) clean = new List<Tuple<string, int, int[], int, string>> { bestLap };
            int n = bestLap.Item3.Length;
            Rival r = new Rival();
            r.Name = name;
            r.BestLapMs = bestLap.Item2;
            r.BestLapSectors = bestLap.Item3;
            r.BestSectors = Enumerable.Range(0, n).Select(i => clean.Min(x => x.Item3[i])).ToArray();
            r.AvgSectors = Enumerable.Range(0, n).Select(i => (float)clean.Average(x => x.Item3[i])).ToArray();
            r.Laps = mine.Count;
            r.CleanLaps = clean.Count;
            r.CutLaps = mine.Count(x => x.Item4 > 0);
            r.Source = string.Join(", ", mine.Select(x => x.Item5).Distinct().ToArray());
            return r;
        }

        // Laps recorded by a rival with AC Coach, copied into rywale\<name>\ (the .csv files from his laps folder):
        // the full telemetry, so the report can lay his line, speed and pedals over yours. Up to 3 fastest valid
        // laps per rival, keyed by the folder name.
        public static List<KeyValuePair<string, Lap>> LoadLaps(string dataDir, string car, string track, string config)
        {
            List<KeyValuePair<string, Lap>> r = new List<KeyValuePair<string, Lap>>();
            string dir = Path.Combine(dataDir, "rywale");
            if (!Directory.Exists(dir)) return r;
            foreach (string sub in Directory.GetDirectories(dir))
            {
                List<Lap> laps = new List<Lap>();
                foreach (string f in Directory.GetFiles(sub, "*.csv", SearchOption.AllDirectories))
                {
                    try
                    {
                        Lap l = Lap.Load(f);
                        if (l.Car == car && l.Track == track && (l.Config ?? "") == (config ?? "") && l.LapMs > 0 && l.Samples.Count > 100) laps.Add(l);
                    }
                    catch (Exception) { }
                }
                string name = Path.GetFileName(sub);
                foreach (Lap l in laps.Where(l => l.Valid).OrderBy(l => l.LapMs).Take(3))
                    r.Add(new KeyValuePair<string, Lap>(name, l));
            }
            return r;
        }

        static string S(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? v.ToString() : null; }
        static int I(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToInt32(v, Inv) : 0; }

        // "+0,6" style for speech, from seconds
        public static string Spoken(float s)
        {
            return (s >= 0 ? "plus " : "minus ") + Math.Abs(s).ToString(Math.Abs(s) < 0.1f ? "0.00" : "0.0", CultureInfo.GetCultureInfo("pl-PL"));
        }
    }

    // Where the timing sectors start along the lap (metres), read from the track's timing gates
    // (AC_TIME_1_L/R, AC_TIME_2_L/R ... in the track's .kn5 models) and cached in sektory.txt.
    public static class Sectors
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Start distance of every sector after the first (sector 0 starts at the line). Null when unknown.
        public static float[] Bounds(string comboDir, string track, string config, Trace refTrace)
        {
            string f = Path.Combine(comboDir, "sektory.txt");
            if (File.Exists(f))
            {
                List<float> b = new List<float>();
                foreach (string line in File.ReadAllLines(f))
                {
                    float v;
                    if (!line.StartsWith("#") && float.TryParse(line.Trim(), NumberStyles.Float, Inv, out v)) b.Add(v);
                }
                if (b.Count > 0) return b.ToArray();
            }
            if (refTrace == null) return null;
            float[] found = FromTrack(TrackAi.FindAcRoot(), track, config, refTrace);
            if (found == null || found.Length == 0) return null;
            List<string> lines = new List<string> { "# początki sektorów (metry od linii mety), z linii pomiaru czasu toru (AC_TIME_*)" };
            lines.AddRange(found.Select(x => x.ToString("0", Inv)));
            try { File.WriteAllLines(f, lines); } catch (IOException) { }
            return found;
        }

        // Sector times of a lap (ms) from its distance trace; null when the trace does not cover the lap.
        public static int[] Times(Trace tr, float[] bounds, int lapMs)
        {
            if (bounds == null || tr == null || tr.Filled < tr.N - 5 || lapMs <= 0) return null;
            int[] r = new int[bounds.Length + 1];
            float prev = 0;
            for (int i = 0; i < bounds.Length; i++)
            {
                float t = tr.TimeAt(bounds[i]) * 1000;
                r[i] = (int)Math.Round(t - prev);
                prev = t;
            }
            r[bounds.Length] = (int)Math.Round(lapMs - prev);
            return r.Any(x => x <= 0) ? null : r;
        }

        static float[] FromTrack(string acRoot, string track, string config, Trace refTrace)
        {
            if (acRoot == null || string.IsNullOrEmpty(track)) return null;
            string dir = Path.Combine(acRoot, "content", "tracks", track);
            if (!Directory.Exists(dir)) return null;
            // the layout's models (models_<config>.ini), or every model of a single-layout track
            List<string> files = new List<string>();
            string ini = Path.Combine(dir, string.IsNullOrEmpty(config) ? "models.ini" : "models_" + config + ".ini");
            if (File.Exists(ini))
                foreach (string line in File.ReadAllLines(ini))
                    if (line.Trim().StartsWith("FILE=", StringComparison.OrdinalIgnoreCase)) files.Add(Path.Combine(dir, line.Trim().Substring(5)));
            if (files.Count == 0) files.AddRange(Directory.GetFiles(dir, "*.kn5"));

            Dictionary<string, float[]> gates = new Dictionary<string, float[]>();
            foreach (string f in files.Where(File.Exists))
                foreach (var g in Gates(f)) gates[g.Key] = g.Value;

            List<float> bounds = new List<float>();
            for (int n = 1; n < 20; n++)
            {
                float[] l, r;
                if (!gates.TryGetValue("AC_TIME_" + n + "_L", out l) || !gates.TryGetValue("AC_TIME_" + n + "_R", out r)) break;
                // where the reference lap crosses the gate (closest approach to the line between its two posts)
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < refTrace.Filled; i++)
                {
                    float d = SegDist(refTrace.X[i], refTrace.Z[i], l[0], l[1], r[0], r[1]);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best < 0 || bd > 25) return null;
                bounds.Add(best * Trace.Step);
            }
            bounds.Sort();
            return bounds.ToArray();
        }

        static float SegDist(float px, float pz, float ax, float az, float bx, float bz)
        {
            float vx = bx - ax, vz = bz - az, len = vx * vx + vz * vz;
            float t = len < 1e-6f ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * vx + (pz - az) * vz) / len));
            float cx = ax + t * vx - px, cz = az + t * vz - pz;
            return (float)Math.Sqrt(cx * cx + cz * cz);
        }

        // Dummy nodes named AC_TIME_* in a .kn5: node type 1, name, child count, active flag, 4x4 matrix
        // (the translation is its last row). Returns name -> {x, z}.
        static Dictionary<string, float[]> Gates(string kn5)
        {
            Dictionary<string, float[]> r = new Dictionary<string, float[]>();
            byte[] pat = Encoding.ASCII.GetBytes("AC_TIME_");
            try
            {
                using (FileStream fs = File.OpenRead(kn5))
                {
                    byte[] buf = new byte[8 << 20];
                    long pos = 0;
                    while (pos < fs.Length)
                    {
                        fs.Position = pos;
                        int n = fs.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        for (int i = 8; i <= n - 100; i++)
                        {
                            if (buf[i] != pat[0]) continue;
                            bool ok = true;
                            for (int j = 1; j < pat.Length && ok; j++) ok = buf[i + j] == pat[j];
                            if (!ok) continue;
                            int nl = BitConverter.ToInt32(buf, i - 4), type = BitConverter.ToInt32(buf, i - 8);
                            if (type != 1 || nl < 10 || nl > 40 || i + nl + 70 > n) continue;
                            string name = Encoding.ASCII.GetString(buf, i, nl);
                            int m = i + nl + 4 + 1;
                            r[name] = new[] { BitConverter.ToSingle(buf, m + 48), BitConverter.ToSingle(buf, m + 56) };
                        }
                        if (n < buf.Length) break;
                        pos += n - 100;
                    }
                }
            }
            catch (Exception) { }
            return r;
        }
    }
}
