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
                    "bars " + (st.GameBars ? "1" : "0"),
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

        static string Resource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd().Replace("\r\n", "\n");
        }
    }
}
