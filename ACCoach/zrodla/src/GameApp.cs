using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ACCoach
{
    // The in-game app (assettocorsa\apps\python\ACCoach): AC Coach copies it into the game, switches it on in
    // python.ini and writes what it shows – data\<car>__<track>[__<config>].txt per combo, data\settings.txt.
    public static class GameApp
    {
        public const string Name = "ACCoach";

        public const string ComboCopy = "aplikacja_w_grze.txt";   // the same data in <combo>, for the track map
        public const string LogFile = "aplikacja_log.txt";        // what the app showed on every lap, for the track map

        // The AC font may lack Polish letters: the app gets plain ones.
        public static string Ascii(string s)
        {
            if (s == null) return null;
            const string from = "ąćęłńóśźżĄĆĘŁŃÓŚŹŻ–", to = "acelnoszzACELNOSZZ-";
            StringBuilder b = new StringBuilder(s.Length);
            foreach (char c in s) { int i = from.IndexOf(c); b.Append(i >= 0 ? to[i] : c < 128 ? c : '?'); }
            return b.ToString();
        }

        public static string AppDir(string acRoot) { return Path.Combine(acRoot, "apps", "python", Name); }

        // Copies the app into the game when it is missing or older, writes its settings. Returns the app folder or null.
        public static string Install(Settings st, bool verbose)
        {
            string ac = TrackAi.FindAcRoot();
            if (ac == null)
            {
                if (verbose) Console.WriteLine(L.T("Aplikacja w grze: nie znaleziono folderu Assetto Corsa – wskaż go w Ustawieniach.", "In-game app: Assetto Corsa folder not found – pick it in Settings."));
                return null;
            }
            string dir = AppDir(ac);
            try
            {
                string py = Resource("ACCoach.acapp.py");
                string f = Path.Combine(dir, Name + ".py");
                bool fresh = !File.Exists(f);
                Directory.CreateDirectory(Path.Combine(dir, "data"));
                if (fresh || File.ReadAllText(f, Encoding.UTF8) != py)
                {
                    File.WriteAllText(f, py, new UTF8Encoding(false));
                    Console.WriteLine((fresh ? L.T("Aplikacja w grze zainstalowana: ", "In-game app installed: ") : L.T("Aplikacja w grze zaktualizowana: ", "In-game app updated: ")) + dir);
                }
                else if (verbose) Console.WriteLine(L.T("Aplikacja w grze jest aktualna: ", "In-game app is up to date: ") + dir);
                if (Enable() || fresh)
                    Console.WriteLine(L.T("   W grze: na torze kliknij pasek aplikacji po prawej stronie ekranu i wybierz „AC Coach”.",
                        "   In the game: on track open the app bar on the right side of the screen and pick “AC Coach”."));
                WriteSettings(dir, st);
                return dir;
            }
            catch (Exception e)
            {
                Console.WriteLine(L.T("Aplikacja w grze: nie udało się zapisać do ", "In-game app: could not write to ") + dir + " (" + e.Message + ")");
                return null;
            }
        }

        // Switches the app on in Documents\Assetto Corsa\cfg\python.ini (what the AC launcher and Content Manager
        // do with their "Python apps" ticks) unless the user has a line for it already. True when added.
        static bool Enable()
        {
            try
            {
                string cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Assetto Corsa", "cfg");
                if (!Directory.Exists(cfg)) return false;
                string f = Path.Combine(cfg, "python.ini");
                List<string> lines = File.Exists(f) ? File.ReadAllLines(f).ToList() : new List<string>();
                if (lines.Any(l => l.Trim().Equals("[" + Name + "]", StringComparison.OrdinalIgnoreCase))) return false;
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add("[" + Name.ToUpperInvariant() + "]");
                lines.Add("ACTIVE=1");
                File.WriteAllLines(f, lines);
                Console.WriteLine(L.T("Aplikacja w grze włączona w ", "In-game app switched on in ") + f);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static void WriteSettings(string dir, Settings st)
        {
            if (dir == null) return;
            try
            {
                Write(Path.Combine(dir, "data", "settings.txt"), new List<string> {
                    "# AC Coach – settings of the in-game app (change them in the AC Coach window, Settings tab)",
                    "lang " + st.Lang,
                    "countdown " + (st.GameCountdown ? "1" : "0"),
                    "tips " + (st.GameTips ? "1" : "0"),
                    "delta " + (st.GameDelta ? "1" : "0") });
            }
            catch (Exception) { }
        }

        // Where the trainer writes the data for one car + track; null when the game or the app is not there.
        public static string DataFile(string comboName)
        {
            string ac = TrackAi.FindAcRoot();
            if (ac == null) return null;
            string dir = Path.Combine(AppDir(ac), "data");
            return Directory.Exists(dir) ? Path.Combine(dir, comboName + ".txt") : null;
        }

        // Written to a temporary file first, so the game never reads half a file.
        public static void Write(string f, List<string> lines)
        {
            string tmp = f + ".tmp";
            File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
            if (File.Exists(f)) File.Delete(f);
            File.Move(tmp, f);
        }

        // <combo>\aplikacja_w_grze.txt for the track map: corners, lead and the grid as arrays; null when missing.
        public static Dictionary<string, object> ReadForMap(string comboDir)
        {
            string f = Path.Combine(comboDir, ComboCopy);
            if (!File.Exists(f)) return null;
            try
            {
                System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
                Func<string, float> F = x => float.Parse(x, inv);
                List<object> corners = new List<object>(), turns = new List<object>();
                List<float> t = new List<float>(), spd = new List<float>();
                float length = 0, step = 2;
                bool inGrid = false, v2 = false;
                foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    string[] p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length == 0 || p[0].StartsWith("#")) continue;
                    if (inGrid) { t.Add(F(p[0])); spd.Add(F(p[1])); continue; }
                    if (p[0] == "accoach") v2 = p.Length > 1 && p[1] != "1";
                    else if (p[0] == "length") length = F(p[1]);
                    else if (p[0] == "grid") { step = F(p[1]); inGrid = true; }
                    else if (p[0] == "turn" && p.Length >= 4) turns.Add(new object[] { int.Parse(p[1], inv), Math.Round(F(p[2]), 1), Math.Round(F(p[3]), 1) });
                    else if (p[0] == "corner" && p.Length >= 8)
                        corners.Add(new object[] { int.Parse(p[1], inv), Math.Round(F(p[2]), 1), Math.Round(F(p[3]), 1), Math.Round(F(p[4]), 1), Math.Round(F(p[5]), 1), Math.Round(F(p[6]), 1), Math.Round(F(p[7]), 2) });
                }
                if (!v2 || length <= 0 || t.Count < 10) return null;   // 1.8.x data: drive with this version first
                return new Dictionary<string, object> {
                    { "length", Math.Round(length, 1) }, { "step", step }, { "turns", turns }, { "corners", corners }, { "file", ComboCopy },
                    { "date", File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm", inv) }, { "t", Round(t, 3) }, { "spd", Round(spd, 0) } };
            }
            catch (Exception) { return null; }
        }

        // aplikacja_log.txt: lap file -> what the app showed on that lap: [metres, "tip"/"result", turn, text, kind]
        public static Dictionary<string, List<object>> ReadLog(string comboDir)
        {
            Dictionary<string, List<object>> r = new Dictionary<string, List<object>>();
            string f = Path.Combine(comboDir, LogFile);
            if (!File.Exists(f)) return r;
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
            {
                string[] p = line.Split('\t');
                float d; int num;
                if (p.Length < 5 || !float.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out d) || !int.TryParse(p[3], out num)) continue;
                if (!r.ContainsKey(p[0])) r[p[0]] = new List<object>();
                if (p[2] == "result" && p.Length >= 6) r[p[0]].Add(new object[] { d, "result", num, p[5], p[4] });
                else if (p[2] == "tip") r[p[0]].Add(new object[] { d, "tip", num, p[4], null });
            }
            return r;
        }

        static double[] Round(List<float> a, int digits) { return a.Select(v => Math.Round((double)v, digits)).ToArray(); }

        static string Resource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd().Replace("\r\n", "\n");
        }
    }
}
