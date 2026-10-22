using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ACCoach
{
    // Per-Windows-user settings from the configuration window: %APPDATA%\AC Coach\ustawienia.ini.
    // Command-line options still win over them, so the .bat files keep working.
    public class Settings
    {
        public const string Train = "trening", NoTrain = "bez", Quiet = "cichy", Focus = "fokus";

        public string Mode = Train;
        public int FocusCorner;              // 0 = the corner that costs the most
        public string VoiceName = "";        // "" = first Polish voice
        public int Rate = 1;
        public int Volume = 100;
        public bool Mute;
        public bool LearnLead = true;        // learn the final beep's lead from the driver's reactions
        public float BeepLead = 0.15f;
        public bool OpenMap = true;
        public string DataDir = "";          // "" = default (see Program.DefaultDataDir)
        public string AcRoot = "";           // "" = find automatically

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AC Coach", "ustawienia.ini"); }
        }

        public static bool Exists { get { return File.Exists(FilePath); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            if (!File.Exists(FilePath)) return s;
            try
            {
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    int n; float f;
                    switch (k)
                    {
                        case "Tryb": if (v == Train || v == NoTrain || v == Quiet || v == Focus) s.Mode = v; break;
                        case "FokusZakret": if (int.TryParse(v, NumberStyles.Integer, Inv, out n)) s.FocusCorner = Math.Max(0, n); break;
                        case "Glos": s.VoiceName = v; break;
                        case "Tempo": if (int.TryParse(v, NumberStyles.Integer, Inv, out n)) s.Rate = Math.Max(-10, Math.Min(10, n)); break;
                        case "Glosnosc": if (int.TryParse(v, NumberStyles.Integer, Inv, out n)) s.Volume = Math.Max(0, Math.Min(100, n)); break;
                        case "Wycisz": s.Mute = v == "1"; break;
                        case "PikniecieAuto": s.LearnLead = v != "0"; break;
                        case "PikniecieSek": if (float.TryParse(v.Replace(',', '.'), NumberStyles.Float, Inv, out f)) s.BeepLead = Math.Max(0f, Math.Min(1f, f)); break;
                        case "OtworzMape": s.OpenMap = v != "0"; break;
                        case "FolderDanych": s.DataDir = v; break;
                        case "FolderGry": s.AcRoot = v; break;
                    }
                }
            }
            catch (Exception e) { Console.WriteLine("Nie udało się wczytać ustawień (" + FilePath + "): " + e.Message); }
            return s;
        }

        public void Save()
        {
            List<string> l = new List<string>();
            l.Add("# AC Coach – ustawienia tego użytkownika Windows (zmieniaj w oknie Konfiguracja)");
            l.Add("# Tryb: trening | bez | cichy | fokus");
            l.Add("Tryb=" + Mode);
            l.Add("FokusZakret=" + FocusCorner.ToString(Inv));
            l.Add("Glos=" + VoiceName);
            l.Add("Tempo=" + Rate.ToString(Inv));
            l.Add("Glosnosc=" + Volume.ToString(Inv));
            l.Add("Wycisz=" + (Mute ? "1" : "0"));
            l.Add("PikniecieAuto=" + (LearnLead ? "1" : "0"));
            l.Add("PikniecieSek=" + BeepLead.ToString("0.00", Inv));
            l.Add("OtworzMape=" + (OpenMap ? "1" : "0"));
            l.Add("FolderDanych=" + DataDir);
            l.Add("FolderGry=" + AcRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, l, new UTF8Encoding(false));
        }

        // Settings -> options; the command line is applied on top afterwards.
        public void Apply(Options o)
        {
            o.Train = Mode != NoTrain && Mode != Quiet;
            o.Quiet = Mode == Quiet;
            o.Focus = Mode == Focus;
            o.FocusCorner = Mode == Focus ? FocusCorner : 0;
            o.VoiceName = string.IsNullOrEmpty(VoiceName) ? null : VoiceName;
            o.Rate = Rate;
            o.Volume = Volume;
            o.Mute = Mute;
            o.BeepLead = LearnLead ? float.NaN : BeepLead;
            o.NoOpen = !OpenMap;
            if (!string.IsNullOrEmpty(AcRoot)) TrackAi.AcRootOverride = AcRoot;
        }
    }
}
