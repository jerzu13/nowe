using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ACCoach
{
    static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            // language first: it decides the number format (spoken "0.12" / "0,12"), whatever the Windows region settings are
            Options o = new Options();
            Settings st = Settings.Load();
            L.En = st.Lang != "pl";
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--lang") L.En = args[i + 1] != "pl";
            CultureInfo.DefaultThreadCurrentCulture = L.Culture;
            System.Threading.Thread.CurrentThread.CurrentCulture = L.Culture;
            st.Apply(o);
            string dataArg = string.IsNullOrEmpty(st.DataDir) ? null : st.DataDir;
            string mode = "live", combo = null;
            bool modeArg = false;   // the first --train/--no-train/--quiet/--fokus replaces the mode from the settings
            bool driveArgs = false; // any driving option on the command line: start the trainer right away

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "analyze" || a == "analiza") mode = "analyze";
                else if (a == "voicetest") mode = "voicetest";
                else if (a == "config" || a == "konfiguracja") mode = "config";
                else if (a == "--pause") o.Pause = true;
                else if (a == "--mute") o.Mute = true;
                else if (a == "--quiet") { ResetMode(o, ref modeArg); o.Quiet = true; driveArgs = true; }
                else if (a == "--train" || a == "--trening") { ResetMode(o, ref modeArg); o.Train = true; driveArgs = true; }
                else if (a == "--no-train" || a == "--bez-treningu") { ResetMode(o, ref modeArg); o.Train = false; driveArgs = true; }
                else if (a == "--no-open") o.NoOpen = true;
                else if (a == "--test-shm") o.TestShm = true;
                else if (a == "--fokus" || a == "--focus")
                {
                    ResetMode(o, ref modeArg);
                    o.Focus = true;
                    driveArgs = true;
                    int num;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1].TrimStart('Z', 'z'), out num)) { o.FocusCorner = num; i++; }
                }
                else if (a == "--once") o.Once = true;
                else if (a == "--data" && i + 1 < args.Length) dataArg = args[++i];
                else if ((a == "--cel" || a == "--target") && i + 1 < args.Length) o.TargetMs = TargetPlan.ParseTime(args[++i]);
                else if (a == "--lang" && i + 1 < args.Length) i++;
                else if (a == "--ac" && i + 1 < args.Length) TrackAi.AcRootOverride = args[++i];
                else if (a == "--rate" && i + 1 < args.Length) o.Rate = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a == "--beep-lead" && i + 1 < args.Length) o.BeepLead = float.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (mode == "analyze" && !a.StartsWith("--")) combo = a;
                else
                {
                    Console.WriteLine(L.T("Użycie: ACCoach.exe [analyze [folder]] [voicetest] [config] [--train | --no-train] [--fokus [N]] [--quiet] [--mute] [--rate N] [--beep-lead sekundy] [--data folder] [--ac folder_gry] [--cel 1:35.000]", "Usage: ACCoach.exe [analyze [folder]] [voicetest] [config] [--train | --no-train] [--focus [N]] [--quiet] [--mute] [--rate N] [--beep-lead seconds] [--data folder] [--ac game_folder] [--target 1:35.000] [--lang en|pl]"));
                    return 2;
                }
            }
            o.DataDir = dataArg ?? DefaultDataDir();

            // typed in a command prompt: analysis and the voice test print there, as before
            bool console = (mode == "analyze" || mode == "voicetest") && ParentConsole();
            if (!console)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(st, o, o.TargetMs, dataArg == st.DataDir || dataArg == "" ? null : dataArg, driveArgs || (mode == "live" && st.AutoStart), mode));
                return 0;
            }
            try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }
            Console.WriteLine();
            Directory.CreateDirectory(o.DataDir);

            if (mode == "analyze")
            {
                Console.WriteLine(L.T("Dane: ", "Data: ") + o.DataDir);
                int r = Analyzer.Run(o.DataDir, combo, !o.NoOpen, o.TargetMs);
                PauseIf(o);
                return r;
            }
            if (mode == "voicetest")
            {
                VoiceTest(o);
                PauseIf(o);
                return 0;
            }
            return 0;
        }

        static bool ParentConsole()
        {
            try { return AttachConsole(-1); }
            catch (Exception) { return true; }   // not Windows (Mono test runs): plain console output
        }

        static void ResetMode(Options o, ref bool modeArg)
        {
            if (modeArg) return;
            modeArg = true;
            o.Train = true;
            o.Quiet = false;
            o.Focus = false;
            o.FocusCorner = 0;
        }

        static void PauseIf(Options o)
        {
            if (!o.Pause) return;
            Console.WriteLine();
            Console.WriteLine(L.T("Naciśnij dowolny klawisz, aby zamknąć okno...", "Press any key to close this window..."));
            try { Console.ReadKey(true); } catch (InvalidOperationException) { }
        }

        public static void VoiceTest(Options o)
        {
            using (Voice v = new Voice(o.Mute, o.Rate, o.VoiceName, o.Volume))
            {
                Console.WriteLine(L.T("Głos: ", "Voice: ") + v.VoiceName);
                v.SayAndWait(L.T("Zakręt 3, hamujesz za wcześnie, o 15 metrów", "Turn 3, braking too early, by 15 metres"));
                v.SayAndWait(L.T("Czas 1 47,3, minus 0,4", "Time 1 47.3, minus 0.4"));
                v.SayAndWait(L.T("Teraz piknięcia do hamowania. Hamuj na ostatnim. Ostatnie mówi, jak mocno.", "Now the braking beeps. Brake on the last one. The last one tells you how hard."));
                string[] names = { L.T("Lekko albo krótko", "Light or short"), L.T("Średnio", "Medium"), L.T("Cały hamulec", "Full brake") };
                using (Beeper b = new Beeper(o.Mute, o.BeepVolume))
                    for (int force = 2; force >= 0; force--)
                    {
                        v.SayAndWait(names[force]);
                        for (int i = 0; i < 3; i++)
                        {
                            b.Beep(i == 2, force);
                            System.Threading.Thread.Sleep(500);
                        }
                        System.Threading.Thread.Sleep(400);
                    }
            }
        }

        // Portable: data lives next to ACCoach.exe, so the folder can be copied to any PC, user or drive.
        // If that folder is read-only (e.g. Program Files), fall back to Documents\AC Coach of the current user.
        // An installed copy (the installer leaves a ".zainstalowany" marker) keeps data in Documents instead,
        // so uninstalling or updating never touches the laps.
        public static string DefaultDataDir()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (!Installed && Writable(exeDir)) return exeDir;
            string docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AC Coach");
            string rivals = Path.Combine(exeDir, "rywale");
            try
            {
                if (Directory.Exists(rivals) && !Directory.Exists(Path.Combine(docs, "rywale")))
                    foreach (string f in Directory.GetFiles(rivals, "*", SearchOption.AllDirectories))
                    {
                        string dst = Path.Combine(docs, "rywale", f.Substring(rivals.Length).TrimStart('\\'));
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        File.Copy(f, dst, false);
                    }
            }
            catch (Exception) { }
            return docs;
        }

        public static bool Installed
        {
            get { return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".zainstalowany")); }
        }

        static bool Writable(string dir)
        {
            try
            {
                string f = Path.Combine(dir, ".zapis_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(f, "");
                File.Delete(f);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
