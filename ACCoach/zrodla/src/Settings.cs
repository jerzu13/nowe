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
        public bool Train = true;            // brake beeps + line cues
        public bool Quiet;                   // voice: lap times and notes only
        public bool Focus;                   // voice about one corner at a time
        public int FocusCorner;              // 0 = the corner that costs the most
        public string VoiceName = "";        // "" = first Polish voice
        public int Rate = 1;
        public int Volume = 100;
        public int BeepVolume = 100;
        public bool Mute;
        public bool LearnLead = true;        // learn the final beep's lead from the driver's reactions
        public float BeepLead = 0.15f;
        public bool OpenMap = true;
        public bool AutoStart = true;        // start the trainer as soon as the window opens
        public string DataDir = "";          // "" = default (see Program.DefaultDataDir)
        public string AcRoot = "";           // "" = find automatically
        public string Lang = L.InstallerDefault();   // "en" | "pl"

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
                        // "Tryb" is the old single mode setting (up to 1.4); the switches below replace it
                        case "Tryb": s.Train = v == "trening" || v == "fokus"; s.Quiet = v == "cichy"; s.Focus = v == "fokus"; break;
                        case "Piknięcia": s.Train = v != "0"; break;
                        case "TylkoCzasy": s.Quiet = v == "1"; break;
                        case "Fokus": s.Focus = v == "1"; break;
                        case "FokusZakret": if (int.TryParse(v, NumberStyles.Integer, Inv, out n)) s.FocusCorner = Math.Max(0, n); break;
                        case "Glos": s.VoiceName = v; break;
                        case "Tempo": if (int.TryParse(v, NumberStyles.Integer, Inv, out n)) s.Rate = Math.Max(-10, Math.Min(10, n)); break;
                        case "Glosnosc": if (int.TryParse(v, NumberStyles.Integer, Inv, out n)) s.Volume = Math.Max(0, Math.Min(100, n)); break;
                        case "GlosnoscPikniec": if (int.TryParse(v, NumberStyles.Integer, Inv, out n)) s.BeepVolume = Math.Max(0, Math.Min(100, n)); break;
                        case "Wycisz": s.Mute = v == "1"; break;
                        case "PikniecieAuto": s.LearnLead = v != "0"; break;
                        case "PikniecieSek": if (float.TryParse(v.Replace(',', '.'), NumberStyles.Float, Inv, out f)) s.BeepLead = Math.Max(0f, Math.Min(1f, f)); break;
                        case "OtworzMape": s.OpenMap = v != "0"; break;
                        case "AutoStart": s.AutoStart = v != "0"; break;
                        case "FolderDanych": s.DataDir = v; break;
                        case "FolderGry": s.AcRoot = v; break;
                        case "Jezyk": s.Lang = v == "pl" ? "pl" : "en"; break;
                    }
                }
            }
            catch (Exception e) { Console.WriteLine(L.T("Nie udało się wczytać ustawień (", "Could not read the settings (") + FilePath + "): " + e.Message); }
            return s;
        }

        public void Save()
        {
            List<string> l = new List<string>();
            l.Add("# AC Coach – settings of this Windows user (change them in the AC Coach window)");
            l.Add("Piknięcia=" + B(Train));
            l.Add("TylkoCzasy=" + B(Quiet));
            l.Add("Fokus=" + B(Focus));
            l.Add("FokusZakret=" + FocusCorner.ToString(Inv));
            l.Add("Glos=" + VoiceName);
            l.Add("Tempo=" + Rate.ToString(Inv));
            l.Add("Glosnosc=" + Volume.ToString(Inv));
            l.Add("GlosnoscPikniec=" + BeepVolume.ToString(Inv));
            l.Add("Wycisz=" + B(Mute));
            l.Add("PikniecieAuto=" + B(LearnLead));
            l.Add("PikniecieSek=" + BeepLead.ToString("0.00", Inv));
            l.Add("OtworzMape=" + B(OpenMap));
            l.Add("AutoStart=" + B(AutoStart));
            l.Add("FolderDanych=" + DataDir);
            l.Add("FolderGry=" + AcRoot);
            l.Add("Jezyk=" + Lang);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, l, new UTF8Encoding(false));
        }

        static string B(bool v) { return v ? "1" : "0"; }

        // Settings -> options; the command line is applied on top afterwards.
        public void Apply(Options o)
        {
            o.Train = Train;
            o.Quiet = Quiet;
            o.Focus = Focus;
            o.FocusCorner = FocusCorner;
            o.VoiceName = string.IsNullOrEmpty(VoiceName) ? null : VoiceName;
            o.Rate = Rate;
            o.Volume = Volume;
            o.BeepVolume = BeepVolume;
            o.Mute = Mute;
            o.BeepLead = LearnLead ? float.NaN : BeepLead;
            o.NoOpen = !OpenMap;
            TrackAi.AcRootOverride = string.IsNullOrEmpty(AcRoot) ? null : AcRoot;
        }
    }
}
