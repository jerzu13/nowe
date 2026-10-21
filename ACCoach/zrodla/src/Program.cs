using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ACCoach
{
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // Polish number format everywhere (spoken "0,12", reports), whatever the Windows region settings are.
            CultureInfo pl = CultureInfo.GetCultureInfo("pl-PL");
            CultureInfo.DefaultThreadCurrentCulture = pl;
            System.Threading.Thread.CurrentThread.CurrentCulture = pl;
            Options o = new Options();
            string dataArg = null;
            string mode = "live", combo = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "analyze" || a == "analiza") mode = "analyze";
                else if (a == "voicetest") mode = "voicetest";
                else if (a == "--mute") o.Mute = true;
                else if (a == "--quiet") o.Quiet = true;
                else if (a == "--train" || a == "--trening") o.Train = true;
                else if (a == "--no-train" || a == "--bez-treningu") o.Train = false;
                else if (a == "--no-open") o.NoOpen = true;
                else if (a == "--test-shm") o.TestShm = true;
                else if (a == "--fokus" || a == "--focus")
                {
                    o.Focus = true;
                    int num;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1].TrimStart('Z', 'z'), out num)) { o.FocusCorner = num; i++; }
                }
                else if (a == "--once") o.Once = true;
                else if (a == "--data" && i + 1 < args.Length) dataArg = args[++i];
                else if (a == "--ac" && i + 1 < args.Length) TrackAi.AcRootOverride = args[++i];
                else if (a == "--rate" && i + 1 < args.Length) o.Rate = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a == "--beep-lead" && i + 1 < args.Length) o.BeepLead = float.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (mode == "analyze" && !a.StartsWith("--")) combo = a;
                else
                {
                    Console.WriteLine("Użycie: ACCoach.exe [analyze [folder]] [voicetest] [--train | --no-train] [--fokus [N]] [--quiet] [--mute] [--rate N] [--beep-lead sekundy] [--data folder] [--ac folder_gry]");
                    return 2;
                }
            }
            o.DataDir = dataArg ?? DefaultDataDir();
            Directory.CreateDirectory(o.DataDir);
            Console.WriteLine("Dane: " + o.DataDir);

            if (mode == "analyze") return Analyzer.Run(o.DataDir, combo, !o.NoOpen);
            if (mode == "voicetest")
            {
                using (Voice v = new Voice(o.Mute, o.Rate))
                {
                    Console.WriteLine("Głos: " + v.VoiceName);
                    v.SayAndWait("Zakręt 3, hamujesz za wcześnie, o 15 metrów");
                    v.SayAndWait("Czas 1 47,3, minus 0,4");
                    v.SayAndWait("Teraz piknięcia do hamowania. Hamuj na ostatnim. Ostatnie mówi, jak mocno.");
                    string[] names = { "Lekko albo krótko", "Średnio", "Cały hamulec" };
                    using (Beeper b = new Beeper(o.Mute))
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
                return 0;
            }
            new LiveCoach(o).Run();
            return 0;
        }

        // Portable: data lives next to ACCoach.exe, so the folder can be copied to any PC, user or drive.
        // If that folder is read-only (e.g. Program Files), fall back to Documents\AC Coach of the current user.
        static string DefaultDataDir()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (Writable(exeDir)) return exeDir;
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
