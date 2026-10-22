using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ACCoach
{
    // The track's AI racing line from the game files (content\tracks\<track>\<config>\ai\fast_lane.ai):
    // points along the line plus the distance to the left and right track edge at each point.
    public class TrackAi
    {
        public float[] X, Z, Left, Right;
        public int N { get { return X.Length; } }

        public static string AcRootOverride;   // --ac folder
        static string acRoot;

        // The Assetto Corsa folder on this PC: --ac, the AC_ROOT variable or sciezka_ac.txt next to ACCoach.exe,
        // then the running game, Steam (registry + every Steam library) and the usual folders on every drive.
        public static string FindAcRoot(bool fresh = false)
        {
            if (acRoot != null && !fresh) return acRoot;
            List<string> cand = new List<string>();
            cand.Add(AcRootOverride);
            cand.Add(Environment.GetEnvironmentVariable("AC_ROOT"));
            try
            {
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sciezka_ac.txt");
                if (File.Exists(f))
                    foreach (string line in File.ReadAllLines(f))
                        if (line.Trim().Length > 0 && !line.Trim().StartsWith("#")) { cand.Add(line.Trim().Trim('"')); break; }
            }
            catch (Exception) { }
            try
            {
                foreach (System.Diagnostics.Process pr in System.Diagnostics.Process.GetProcessesByName("acs"))
                    try { cand.Add(Path.GetDirectoryName(pr.MainModule.FileName)); } catch (Exception) { }
            }
            catch (Exception) { }
            foreach (string key in new[] {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 244210",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 244210" })
                cand.Add(Reg(key, "InstallLocation"));

            List<string> steam = new List<string>();
            steam.Add(Reg(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"));
            steam.Add(Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            steam.Add(Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath"));
            steam.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            steam.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
            List<string> libs = new List<string>();
            foreach (string s in steam)
            {
                if (string.IsNullOrEmpty(s)) continue;
                string st = s.Replace('/', '\\');
                libs.Add(st);
                string vdf = Path.Combine(st, @"steamapps\libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (string line in File.ReadAllLines(vdf))
                    {
                        // new format: "path"  "D:\\SteamLibrary"; old format: "1"  "D:\\SteamLibrary"
                        string[] q = line.Trim().Split('"');
                        if (q.Length < 4) continue;
                        int num;
                        if ((q[1] == "path" || int.TryParse(q[1], out num)) && q[3].Contains(":")) libs.Add(q[3].Replace(@"\\", @"\"));
                    }
                }
                catch (Exception) { }
            }
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed) continue;
                    string r = d.Name;
                    foreach (string sub in new[] { "Steam", "SteamLibrary", @"Games\Steam", @"Games\SteamLibrary", @"Gry\Steam", @"Program Files (x86)\Steam", @"Program Files\Steam" })
                        libs.Add(Path.Combine(r, sub));
                    cand.Add(Path.Combine(r, "assettocorsa"));
                    cand.Add(Path.Combine(r, @"Games\assettocorsa"));
                }
            }
            catch (Exception) { }
            foreach (string lib in libs) cand.Add(Path.Combine(lib, @"steamapps\common\assettocorsa"));

            foreach (string c in cand)
            {
                if (string.IsNullOrEmpty(c)) continue;
                try
                {
                    if (Directory.Exists(Path.Combine(c, "content", "tracks"))) return acRoot = c;
                }
                catch (Exception) { }
            }
            return null;
        }

        static string Reg(string key, string name)
        {
            try
            {
                object v = Registry.GetValue(key, name, null);
                return v == null ? null : v.ToString();
            }
            catch (Exception) { return null; }
        }

        public static TrackAi Load(string acRoot, string track, string config)
        {
            if (acRoot == null || string.IsNullOrEmpty(track)) return null;
            string dir = Path.Combine(acRoot, "content", "tracks", track);
            if (!string.IsNullOrEmpty(config)) dir = Path.Combine(dir, config);
            string f = Path.Combine(dir, "ai", "fast_lane.ai");
            if (!File.Exists(f)) return null;
            try
            {
                byte[] b = File.ReadAllBytes(f);
                int n = BitConverter.ToInt32(b, 4);
                if (n < 10 || 16 + n * 20 + 4 > b.Length) return null;
                TrackAi ai = new TrackAi();
                ai.X = new float[n]; ai.Z = new float[n]; ai.Left = new float[n]; ai.Right = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int o = 16 + i * 20;            // x, y, z, length, id
                    ai.X[i] = BitConverter.ToSingle(b, o);
                    ai.Z[i] = BitConverter.ToSingle(b, o + 8);
                }
                int e = 16 + n * 20;
                bool sides = BitConverter.ToInt32(b, e) == n && e + 4 + n * 72 <= b.Length;
                for (int i = 0; i < n; i++)
                {
                    // extra data per point, 18 floats: speed, gas, brake, latG, radius, sideLeft, sideRight, ...
                    int o = e + 4 + i * 72;
                    ai.Left[i] = sides ? BitConverter.ToSingle(b, o + 20) : 6f;
                    ai.Right[i] = sides ? BitConverter.ToSingle(b, o + 24) : 6f;
                    if (ai.Left[i] <= 0 || ai.Left[i] > 40) ai.Left[i] = 6f;
                    if (ai.Right[i] <= 0 || ai.Right[i] > 40) ai.Right[i] = 6f;
                }
                return ai;
            }
            catch (Exception) { return null; }
        }

        // Unit normal at point i, 90° from the driving direction (sign is checked against a real lap).
        public void Normal(int i, out float nx, out float nz)
        {
            int a = (i - 1 + N) % N, c = (i + 1) % N;
            float tx = X[c] - X[a], tz = Z[c] - Z[a];
            float len = (float)Math.Sqrt(tx * tx + tz * tz);
            if (len < 1e-3f) { nx = 0; nz = 0; return; }
            nx = -tz / len; nz = tx / len;
        }

        // True when "Left" lies on the +normal side; decided by how many points of a real lap fall inside the edges.
        public bool LeftOnPlusSide(Trace lap)
        {
            int inPlus = 0, inMinus = 0;
            for (int g = 0; g < lap.Filled; g += 5)
            {
                float x = lap.X[g], z = lap.Z[g];
                if (x == 0 && z == 0) continue;
                int i = Nearest(x, z);
                float nx, nz;
                Normal(i, out nx, out nz);
                float off = (x - X[i]) * nx + (z - Z[i]) * nz;
                if (off <= Left[i] + 0.5f && -off <= Right[i] + 0.5f) inPlus++;
                if (off <= Right[i] + 0.5f && -off <= Left[i] + 0.5f) inMinus++;
            }
            return inPlus >= inMinus;
        }

        public int Nearest(float x, float z)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < N; i++)
            {
                float dx = X[i] - x, dz = Z[i] - z, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }
    }

    // Writes an HTML page with the track map: edges, AI line, your laps, braking points, beeps, off-track spots and tips.
    public static class TrackMap
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public class CornerInfo
        {
            public Corner Corner;
            public string Tip;     // most important thing to fix, or null
            public float Loss;     // average seconds lost vs reference in the session
        }

        static double R1(float v) { return Math.Round(v, 1); }

        public const int GridEvery = 2;   // laps are sent to the page every 2 trace points = 4 m

        // A lap on a uniform 4 m grid: x, z (null where the position is unknown), brake, throttle, speed, time, steering.
        static object[] Path2(Trace tr)
        {
            List<object> xs = new List<object>(), zs = new List<object>();
            List<float> br = new List<float>(), gs = new List<float>(), sp = new List<float>(), ts = new List<float>(), st = new List<float>();
            for (int g = 0; g < tr.Filled; g += GridEvery)
            {
                bool pos = tr.X[g] != 0 || tr.Z[g] != 0;
                xs.Add(pos ? (object)R1(tr.X[g]) : null); zs.Add(pos ? (object)R1(tr.Z[g]) : null);
                br.Add(tr.Brk[g]); gs.Add(tr.Gas[g]); sp.Add(tr.Spd[g]); ts.Add(tr.T[g]); st.Add(tr.Str[g]);
            }
            return new object[] { xs, zs, Round(br.ToArray(), 2), Round(gs.ToArray(), 2), Round(sp.ToArray(), 0), Round(ts.ToArray(), 3), Round(st.ToArray(), 2) };
        }

        static object Pt(Trace tr, float d)
        {
            if (float.IsNaN(d)) return null;
            int i = tr.Idx(d);
            if (i >= tr.Filled) return null;
            return new object[] { R1(tr.X[i]), R1(tr.Z[i]) };
        }

        static object[] Round(float[] a, int digits)
        {
            object[] r = new object[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = Math.Round(a[i], digits);
            return r;
        }

        // Beep log rows (pikniecia.csv) for one lap file: corner -> {beep_m, brake_m, reaction_s, tyres_out}
        static Dictionary<string, List<string[]>> ReadBeeps(string comboDir)
        {
            Dictionary<string, List<string[]>> r = new Dictionary<string, List<string[]>>();
            string f = Path.Combine(comboDir, "pikniecia.csv");
            if (!File.Exists(f)) return r;
            foreach (string line in File.ReadAllLines(f).Skip(1))
            {
                string[] p = line.Split(',');
                if (p.Length < 12) continue;
                if (!r.ContainsKey(p[0])) r[p[0]] = new List<string[]>();
                r[p[0]].Add(p);
            }
            return r;
        }

        static float F(string s) { float v; return float.TryParse(s, NumberStyles.Float, Inv, out v) ? v : float.NaN; }

        public static string Write(string comboDir, string outFile, float length, Lap refLap, List<CornerInfo> corners, List<Lap> session, List<string> tips, List<string> reaction, List<string> inputs, List<string> rivalText,
            float[] sectorBounds, Rival rival, List<KeyValuePair<string, Lap>> rivalLaps)
        {
            TrackAi ai = TrackAi.Load(TrackAi.FindAcRoot(), refLap.Track, refLap.Config);
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["title"] = refLap.Car + " – " + refLap.Track + (string.IsNullOrEmpty(refLap.Config) ? "" : " " + refLap.Config);
            root["ref"] = new Dictionary<string, object> { { "file", Path.GetFileName(refLap.File) }, { "time", Lap.FormatTime(refLap.LapMs) } };
            root["tips"] = tips;
            root["reaction"] = reaction;
            root["inputs"] = inputs;
            root["rival"] = rivalText;

            if (ai != null)
            {
                bool plus = ai.LeftOnPlusSide(refLap.Trace);
                List<object> lx = new List<object>(), lz = new List<object>(), rx = new List<object>(), rz = new List<object>(), ax = new List<object>(), az = new List<object>();
                for (int i = 0; i < ai.N; i += 2)
                {
                    float nx, nz;
                    ai.Normal(i, out nx, out nz);
                    float s = plus ? 1 : -1;
                    lx.Add(R1(ai.X[i] + nx * s * ai.Left[i])); lz.Add(R1(ai.Z[i] + nz * s * ai.Left[i]));
                    rx.Add(R1(ai.X[i] - nx * s * ai.Right[i])); rz.Add(R1(ai.Z[i] - nz * s * ai.Right[i]));
                    ax.Add(R1(ai.X[i])); az.Add(R1(ai.Z[i]));
                }
                root["edgeL"] = new object[] { lx, lz };
                root["edgeR"] = new object[] { rx, rz };
                root["ai"] = new object[] { ax, az };
            }

            List<object> cl = new List<object>();
            foreach (CornerInfo ci in corners)
            {
                Corner c = ci.Corner;
                cl.Add(new Dictionary<string, object> {
                    { "num", c.Num }, { "apexD", Math.Round(c.ApexD) }, { "apex", Pt(refLap.Trace, c.ApexD) }, { "brake", Pt(refLap.Trace, c.BrakeD) },
                    { "throttle", Pt(refLap.Trace, c.ThrottleD) }, { "minSpeed", Math.Round(c.MinSpeed) },
                    { "force", c.Braked ? c.ForceName : null }, { "forcePct", c.Braked ? (object)Math.Round(c.BrakeForce * 100) : null },
                    { "tip", ci.Tip }, { "loss", Math.Round(ci.Loss, 2) } });
            }
            root["corners"] = cl;

            Dictionary<string, List<string[]>> beeps = ReadBeeps(comboDir);
            List<object> laps = new List<object>();
            List<Lap> shown = new List<Lap>(session);
            if (!shown.Any(l => l.File == refLap.File)) shown.Insert(0, refLap);
            foreach (Lap l in shown) laps.Add(LapData(l, length, refLap, sectorBounds, beeps, null));
            if (rivalLaps != null)
                foreach (KeyValuePair<string, Lap> rl in rivalLaps) laps.Add(LapData(rl.Value, length, refLap, sectorBounds, beeps, rl.Key));
            root["laps"] = laps;
            root["step"] = Trace.Step * GridEvery;
            root["sectorBounds"] = sectorBounds;
            if (rival != null)
                root["rivalTimes"] = new Dictionary<string, object> {
                    { "name", rival.Name }, { "bestLapMs", rival.BestLapMs }, { "bestLapSectors", rival.BestLapSectors },
                    { "bestSectors", rival.BestSectors }, { "avgSectors", rival.AvgSectors.Select(x => Math.Round(x)).ToArray() } };

            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            string html = Template().Replace("/*__DATA__*/null", js.Serialize(root));
            File.WriteAllText(outFile, html, new UTF8Encoding(false));
            return ai == null ? "brak linii AI z gry (nie znaleziono Assetto Corsa) – mapa bez krawędzi toru" : null;
        }

        static object J(float v) { return float.IsNaN(v) ? null : (object)Math.Round(v); }

        // One lap for the page: path and inputs on the 4 m grid, sector times, off-track spots and beeps.
        static Dictionary<string, object> LapData(Lap l, float length, Lap refLap, float[] sectorBounds, Dictionary<string, List<string[]>> beeps, string rivalName)
        {
            if (l.Trace == null || Math.Abs(l.Trace.Length - length) > 1) l.BuildTrace(length);
            object[] p = Path2(l.Trace);
            List<object> off = new List<object>();
            int lastOff = -100;
            for (int i = 0; i < l.Samples.Count; i++)
                if (l.Samples[i].TyresOut >= 3 && i - lastOff > 40) { off.Add(new object[] { R1(l.Samples[i].X), R1(l.Samples[i].Z) }); lastOff = i; }
            List<object> bl = new List<object>();
            List<string[]> rows;
            if (rivalName == null && beeps.TryGetValue(Path.GetFileName(l.File), out rows))
                foreach (string[] r in rows)
                    bl.Add(new Dictionary<string, object> {
                        { "corner", int.Parse(r[1], Inv) }, { "beep", Pt(l.Trace, F(r[3])) }, { "brake", Pt(l.Trace, F(r[4])) },
                        { "reaction", float.IsNaN(F(r[5])) ? null : (object)Math.Round(F(r[5]), 2) },
                        { "beepM", J(F(r[3])) }, { "brakeM", J(F(r[4])) }, { "miss", r.Length > 13 ? J(F(r[13])) : null }, { "out", int.Parse(r[10], Inv) >= 3 } });
            string name = rivalName != null
                ? string.Format("{0}: {1}{2}", rivalName, Lap.FormatTime(l.LapMs), l.Valid ? "" : "  nieważne")
                : string.Format("{0:HH:mm}  {1}{2}{3}", l.Date, Lap.FormatTime(l.LapMs), l.Valid ? "" : "  nieważne", l.File == refLap.File ? "  ★ wzorzec" : "");
            return new Dictionary<string, object> {
                { "name", name }, { "isRef", rivalName == null && l.File == refLap.File }, { "rival", rivalName }, { "lapMs", l.LapMs }, { "valid", l.Valid },
                { "x", p[0] }, { "z", p[1] }, { "brk", p[2] }, { "gas", p[3] }, { "spd", p[4] }, { "t", p[5] }, { "str", p[6] },
                { "sectors", Sectors.Times(l.Trace, sectorBounds, l.LapMs) }, { "off", off }, { "beeps", bl } };
        }

        static string Template()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ACCoach.map_template.html"))
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }
    }
}
