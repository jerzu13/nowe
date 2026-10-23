using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace ACCoach
{
    public class Options
    {
        public string DataDir;
        public bool Mute, Quiet, Once;
        public bool Train = true;   // brake beeps + line cues; on unless --no-train
        public bool NoOpen;          // analyze: do not open the map in the browser
        public bool TestShm;         // read the test tools' memory (acpmf_test_*) instead of the game's
        public int Rate = 1;
        public string VoiceName;             // voice chosen in the settings window; null = first Polish voice
        public int Volume = 100;
        public int BeepVolume = 100;
        public int TargetMs;                 // --cel: target lap time; 0 = from <combo>\cel.txt
        public bool Pause;                   // wait for a key before closing (Start menu shortcuts)
        public float BeepLead = float.NaN;   // seconds the final beep is played early; NaN = learn from your reactions
        public bool Focus;                   // focus mode: speak only about one corner at a time
        public int FocusCorner;              // corner chosen with --fokus N; 0 = pick automatically
    }

    // Records every lap to CSV and, once a reference lap exists, gives short spoken tips after each corner.
    public class LiveCoach
    {
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly Options o;
        Voice voice;
        AcShm shm;
        StaticInfo si;
        string comboDir;
        volatile bool stop;

        // reference (best valid lap for this car + track)
        Lap refLap;
        List<Corner> refCorners;
        // target per corner: the fastest clean version of that corner from any recorded lap (ideal lap);
        // beeps and tips aim at it, the better/worse call compares with the reference lap
        List<Lap> allLaps = new List<Lap>();
        List<Corner> targetCorners;
        Trace[] targetTrace;
        float[] refSeg, targetSeg;
        string[] targetFrom;
        Dictionary<int, string> notes = new Dictionary<int, string>();
        float length;

        // lap in progress
        Lap cur;
        Trace live;
        bool curFromLine, curPit;
        int maxTyresOut, lastCompleted, lastT = -1, lastPacket = -1, nextEval, nextNote, lapsThisSession;
        float lastSpeed, lastBrake;
        int lastTyresOut;

        // training mode: brake countdown beeps and line cues, one slot per reference corner
        Beeper beeper;
        int[] beepStage;
        float[] beepPoint;
        bool[] entryCued;
        static readonly float[] BeepAt = { 1.0f, 0.5f, 0.0f };   // seconds before the braking point
        // what happened around each beep, for the instant "late/early" call and the beep log
        float[] finalBeepT, finalBeepD, brakeT, brakeD;   // seconds / metres, NaN = did not happen
        float[] reaction;       // brake press minus final beep, s; negative = before the final beep would have played
        int[] cornerTyresOut;
        bool[] brakeArmed;      // brake was released after the previous corner, so the next press belongs to this one
        float beepLead = DefaultLead;                       // all corners
        Dictionary<int, float> cornerLead = new Dictionary<int, float>();   // corner number -> lead, when enough data
        const float DefaultLead = 0.15f, MinLead = 0.10f, MaxLead = 0.60f;
        readonly List<string> beepLog = new List<string>();
        const float LateS = 0.35f, EarlyS = 0.35f;

        // safety margin: after an off-track in a corner its final beep comes earlier (metres), shrinking with clean runs
        readonly Dictionary<int, float> margin = new Dictionary<int, float>();
        bool[] offThisLap;
        const float MarginEntry = 10, MarginExit = 5, MarginMax = 30, MarginDecay = 3;

        // focus mode: one corner at a time
        int focusNum;                          // 0 = off / not chosen yet
        int focusStreak;                       // good runs in a row
        readonly HashSet<int> focusDone = new HashSet<int>();
        string focusTip;                       // what to fix there, from the last run
        bool focusReminded;
        const int FocusStreakToMaster = 3;

        // rival from the "rywale" folder: compared after every timing sector
        Rival rival;
        float[] sectorBounds;                  // start of sectors 2.. in metres
        int[] mySectorBest;                    // your best valid sector times, ms
        int nextSector;
        float sectorStartS;

        class Msg { public string Text; public DateTime At; }
        readonly List<Msg> queue = new List<Msg>();
        DateTime lastSpoke = DateTime.MinValue;

        public LiveCoach(Options opts) { o = opts; cliTarget = o.TargetMs > 0; }

        // ---------- live changes from the panel window ----------
        // The panel runs on its own thread; it posts changes that the coaching loop applies between ticks.
        readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();
        public volatile string StState = "Start…", StCombo = "", StRef = "", StLast = "", StFocus = "";
        public Options Opt { get { return o; } }
        public bool Running { get { return !stop; } }

        public volatile string StTarget = "", StStep = "";

        public void Post(Action a) { pending.Enqueue(a); }

        // target lap time from the panel; 0 = off. Saved per car + track in <combo>\cel.txt.
        public void SetTarget(int ms)
        {
            Post(delegate
            {
                o.TargetMs = ms;
                if (comboDir == null) panelTargetPending = ms;
                else TargetPlan.SaveTarget(comboDir, ms);
                Console.WriteLine(ms > 0 ? "[PANEL] Cel czasu: " + Lap.FormatTime(ms) : "[PANEL] Cel czasu wyłączony");
                RebuildPlan(true);
            });
        }

        // ---------- target lap time ----------
        LimitModel model;
        LimitModel.Line modelLine;
        bool modelTried;
        TargetPlan plan;              // the goal driven to now (the next intermediate step)
        TargetPlan finalPlan;         // the target itself
        int idealMs;
        int panelTargetPending = -1;
        Grip.TyreStats refTyres;      // tyre temperatures of the best lap: "cold" is colder than that
        bool tyresWereCold;
        bool cliTarget;

        // the limit model reads every saved lap: built off the coaching thread, swapped in between ticks
        void RebuildModel()
        {
            if (comboDir == null || refLap == null) return;
            string dir = comboDir;
            Trace tr = refLap.Trace;
            ThreadPool.QueueUserWorkItem(delegate
            {
                LimitModel m = null;
                LimitModel.Line line = null;
                try { m = LimitModel.Build(dir); line = LimitModel.LineOf(tr); } catch (Exception) { }
                Post(delegate
                {
                    if (dir != comboDir) return;
                    model = m;
                    modelLine = refLap != null && refLap.Trace != tr ? LimitModel.LineOf(refLap.Trace) : line;
                    modelTried = true;
                    RebuildPlan(true);
                });
            });
        }

        void RebuildPlan(bool announce)
        {
            plan = finalPlan = null;
            StStep = "";
            if (o.TargetMs <= 0) { StTarget = ""; return; }
            if (refLap == null) { StTarget = Lap.FormatTime(o.TargetMs) + " – czekam na wzorzec"; return; }
            if (model == null || modelLine == null)
            {
                StTarget = Lap.FormatTime(o.TargetMs) + (modelTried ? " – za mało danych (przejedź kilka czystych okrążeń)" : " – liczę model auta…");
                return;
            }
            finalPlan = TargetPlan.Make(model, modelLine, refLap.Trace, refLap.LapMs, idealMs, o.TargetMs);
            List<int> steps = finalPlan.Steps();
            int step = steps.Count > 0 ? steps[0] : o.TargetMs;
            plan = step == o.TargetMs ? finalPlan : TargetPlan.Make(model, modelLine, refLap.Trace, refLap.LapMs, idealMs, step);
            StTarget = Lap.FormatTime(o.TargetMs) + " – " + finalPlan.Verdict();
            StStep = finalPlan.Reached ? "cel osiągnięty" : step != o.TargetMs ? "teraz cel pośredni " + Lap.FormatTime(step) : "teraz cel końcowy";
            if (!announce) return;
            Console.ForegroundColor = ConsoleColor.Yellow;
            foreach (string l in finalPlan.Describe(model, refCorners)) Console.WriteLine(l);
            if (!finalPlan.Reached) Console.WriteLine("  " + StStep + " – głos podaje stratę do niego po każdym zakręcie i na mecie");
            Console.ResetColor();
            if (!o.Quiet)
                Enqueue(finalPlan.Reached ? "Cel osiągnięty" : step != o.TargetMs ? "Cel pośredni " + SpokenTime(step) : "Cel " + SpokenTime(step), false);
        }
        public void Stop() { stop = true; }

        public void SetTrain(bool on)
        {
            Post(delegate
            {
                o.Train = on;
                beeper.Muted = o.Mute || !on;
                Console.WriteLine(on ? "[PANEL] Piknięcia do hamowania WŁĄCZONE" : "[PANEL] Piknięcia do hamowania WYŁĄCZONE");
            });
        }

        public void SetQuiet(bool quiet)
        {
            Post(delegate { o.Quiet = quiet; Console.WriteLine(quiet ? "[PANEL] Tryb cichy: tylko czasy i notatki" : "[PANEL] Uwagi głosowe po zakrętach WŁĄCZONE"); });
        }

        public void SetFocusMode(bool on, int corner)
        {
            Post(delegate
            {
                o.Focus = on;
                o.FocusCorner = corner;
                focusNum = 0; focusStreak = 0; focusTip = null; focusReminded = false;
                focusDone.Clear();
                StFocus = on ? "wybieram…" : "";
                if (!on) Console.WriteLine("[PANEL] Fokus WYŁĄCZONY – uwagi o wszystkich zakrętach");
                else if (refCorners != null && refCorners.Count > 0) ChooseFocus(true);
                else Console.WriteLine("[PANEL] Fokus włączy się, gdy będzie wzorzec");
            });
        }

        public void SetMute(bool mute)
        {
            Post(delegate { o.Mute = mute; voice.Muted = mute; beeper.Muted = mute || !o.Train; Console.WriteLine(mute ? "[PANEL] Wyciszone" : "[PANEL] Dźwięk włączony"); });
        }

        public void SetVoice(string name) { Post(delegate { o.VoiceName = name; voice.SetVoice(name); Console.WriteLine("[PANEL] Głos: " + voice.VoiceName); }); }
        public void SetRate(int rate) { Post(delegate { o.Rate = rate; voice.SetRate(rate); }); }
        public void SetVolume(int vol) { Post(delegate { o.Volume = vol; voice.SetVolume(vol); }); }
        public void SetBeepVolume(int vol) { Post(delegate { o.BeepVolume = vol; beeper.SetVolume(vol); }); }

        public void SetBeepLead(float lead)   // NaN = learn from the reactions
        {
            Post(delegate { o.BeepLead = lead; if (comboDir != null) UpdateBeepLead(true); });
        }

        public void Run()
        {
            try { Console.CancelKeyPress += delegate (object s, ConsoleCancelEventArgs e) { e.Cancel = true; stop = true; }; } catch (Exception) { }   // no console in the window app
            try { timeBeginPeriod(1); } catch (Exception) { }   // finer Sleep on Windows
            voice = new Voice(false, o.Rate, o.VoiceName, o.Volume) { Muted = o.Mute };
            beeper = new Beeper(o.Mute || !o.Train, o.BeepVolume);
            Console.WriteLine("AC Coach – nagrywanie okrążeń i trener głosowy");
            Console.WriteLine("Dane: " + o.DataDir);
            Console.WriteLine("Głos: " + voice.VoiceName + (o.Quiet ? " (tryb cichy: tylko czasy i notatki)" : ""));
            if (o.Train) Console.WriteLine("TRYB TRENINGOWY: piknięcia do hamowania + uwagi o linii");
            else Console.WriteLine("Piknięcia do hamowania WYŁĄCZONE (--no-train)");
            if (o.Focus) Console.WriteLine("TRYB FOKUS: uwagi głosowe tylko o jednym zakręcie" + (o.FocusCorner > 0 ? " (Z" + o.FocusCorner + ")" : ", wybieranym automatycznie"));
            Console.WriteLine("Zatrzymanie: przycisk „Zatrzymaj trenera” albo zamknięcie okna\n");

            bool wasLive = false, waitingShown = false;
            try
            {
                while (!stop)
                {
                    Action change;
                    while (pending.TryDequeue(out change)) change();
                    if (shm == null)
                    {
                        shm = AcShm.TryOpen(o.TestShm ? "acpmf_test" : "acpmf");
                        if (shm == null)
                        {
                            if (!waitingShown) { Console.WriteLine("Czekam na Assetto Corsa..."); waitingShown = true; StState = "Czekam na Assetto Corsa…"; }
                            Thread.Sleep(1000);
                            continue;
                        }
                    }
                    Telemetry t = shm.Read();
                    if (t.Status != AcShm.STATUS_LIVE)
                    {
                        if (wasLive && t.Status == AcShm.STATUS_OFF)
                        {
                            wasLive = false;
                            Console.WriteLine("Sesja zakończona. Zapisanych okrążeń: " + lapsThisSession);
                            StState = "Sesja zakończona – czekam na następną";
                            if (o.Once) break;
                        }
                        Thread.Sleep(100);
                        continue;
                    }
                    if (!wasLive)
                    {
                        wasLive = true;
                        StState = "Jazda – nagrywam okrążenia";
                        SetupCombo(shm.ReadStatic());
                        lastCompleted = t.CompletedLaps;
                        StartLap(t, false);
                    }
                    if (t.GraphicsPacket == lastPacket) { Thread.Sleep(5); continue; }
                    lastPacket = t.GraphicsPacket;

                    if (t.CompletedLaps == lastCompleted + 1)
                    {
                        FinishLap(t);
                        lastCompleted = t.CompletedLaps;
                        StartLap(t, true);
                    }
                    else if (t.CompletedLaps != lastCompleted || (lastT >= 0 && t.CurrentTimeMs + 1000 < lastT))
                    {
                        // session restart, return to pits or a new session online
                        lastCompleted = t.CompletedLaps;
                        StartLap(t, false);
                    }
                    AddSample(t);
                    ProcessVoice();
                    Thread.Sleep(10);
                }
            }
            finally
            {
                try { timeEndPeriod(1); } catch (Exception) { }
                if (shm != null) shm.Dispose();
                voice.Dispose();
                beeper.Dispose();
            }
        }

        void SetupCombo(StaticInfo info)
        {
            si = info;
            comboDir = Lap.ComboDir(o.DataDir, si.Car, si.Track, si.Config);
            Directory.CreateDirectory(Path.Combine(comboDir, "laps"));
            Console.WriteLine(string.Format("Auto: {0}   Tor: {1} {2}", si.Car, si.Track, si.Config));
            StCombo = si.Car + "  |  " + si.Track + (string.IsNullOrEmpty(si.Config) ? "" : " " + si.Config);
            length = (si.SplineLength > 200 && si.SplineLength < 50000) ? si.SplineLength : 0;
            allLaps = Lap.LoadAll(comboDir, length);
            refLap = Lap.BestValid(allLaps);
            UpdateBeepLead(true);
            LoadMargins();
            if (refLap != null) SetReference(refLap, false);
            else Console.WriteLine("Brak wzorca – przejedź jedno czyste okrążenie, wtedy włączą się podpowiedzi.");
            if (o.Focus && focusNum == 0) ChooseFocus(true);
            SetupRival();
            if (panelTargetPending >= 0) { o.TargetMs = panelTargetPending; TargetPlan.SaveTarget(comboDir, o.TargetMs); panelTargetPending = -1; }
            else if (!cliTarget) o.TargetMs = TargetPlan.LoadTarget(comboDir);
            model = null; modelLine = null; modelTried = false;
            RebuildPlan(false);
            RebuildModel();
        }

        void SetReference(Lap lap, bool announce)
        {
            refLap = lap;
            if (length <= 0) length = lap.EstimateLength();
            lap.BuildTrace(length);
            refCorners = Corners.Detect(lap.Trace);
            try { refTyres = Grip.Tyres(lap.Samples.Count > 0 ? lap.Samples : Lap.Load(lap.File).Samples); } catch (Exception) { refTyres = null; }
            Corners.Renumber(refCorners, comboDir, lap.Trace);
            Console.WriteLine(string.Format("Wzorzec: {0}  ({1} m, zakrętów: {2})", Lap.FormatTime(lap.LapMs), (int)length, refCorners.Count));
            StRef = Lap.FormatTime(lap.LapMs) + "  (zakrętów: " + refCorners.Count + ")";
            if (announce)
                foreach (Corner c in refCorners)
                    Console.WriteLine(string.Format(Inv, "   Z{0,-2} szczyt {1,5:0} m  {2,4:0} km/h", c.Num, c.ApexD, c.MinSpeed));
            if (o.Train && !Corners.HasPositions(lap.Trace))
                Console.WriteLine("Uwaga: brak pozycji auta we wzorcu – uwagi o linii będą wyłączone.");
            BuildTargets();
            ResetTraining();
        }

        static float Seg(Trace tr, Corner c) { return tr.TimeAt(c.EndD) - tr.TimeAt(c.StartD); }

        // ---------- safety margin (margines.txt: "Z<num> <metres>") ----------

        // The corner a spot on track belongs to: from 50 m before its braking to 50 m before the next one's.
        int OwnerCorner(float d)
        {
            int k = refCorners.Count - 1;
            for (int j = 0; j < refCorners.Count; j++) if (d >= refCorners[j].OnsetD - 50) k = j;
            return k;
        }

        float MarginFor(int num)
        {
            float m;
            return margin.TryGetValue(num, out m) ? m : 0;
        }

        void LoadMargins()
        {
            margin.Clear();
            string f = Path.Combine(comboDir, "margines.txt");
            if (!File.Exists(f)) return;
            foreach (string line in File.ReadAllLines(f))
            {
                string[] p = line.Trim().TrimStart('Z', 'z').Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int num; float m;
                if (p.Length >= 2 && int.TryParse(p[0], out num) && float.TryParse(p[1], NumberStyles.Float, Inv, out m) && m > 0)
                    margin[num] = Math.Min(MarginMax, m);
            }
            if (margin.Count > 0)
                Console.WriteLine("Margines bezpieczeństwa (piknięcie wcześniej): " +
                    string.Join(", ", margin.OrderBy(x => x.Key).Select(x => string.Format(Inv, "Z{0} {1:0} m", x.Key, x.Value)).ToArray()));
        }

        void SaveMargins()
        {
            List<string> lines = new List<string> { "# margines bezpieczeństwa: o ile metrów wcześniej gra ostatnie piknięcie po wyjazdach z toru" };
            foreach (KeyValuePair<int, float> x in margin.Where(x => x.Value > 0).OrderBy(x => x.Key))
                lines.Add(string.Format(Inv, "Z{0} {1:0}", x.Key, x.Value));
            try { File.WriteAllLines(Path.Combine(comboDir, "margines.txt"), lines); } catch (IOException) { }
        }

        // First off-track of this lap in a corner: brake earlier there next time (more if it happened on the entry).
        void OnOffTrack(float d)
        {
            int k = OwnerCorner(d);
            if (offThisLap[k]) return;
            offThisLap[k] = true;
            Corner c = refCorners[k];
            bool entry = d < c.ApexD;
            Console.WriteLine(string.Format(Inv, "  [WYJAZD] Z{0} {1} ({2:0} m {3} szczytem)", c.Num, entry ? "na wejściu" : "na wyjściu", Math.Abs(d - c.ApexD), entry ? "przed" : "za"));
            if (o.Focus && c.Num == focusNum)
            {
                focusStreak = 0;
                focusTip = entry ? "hamuj wcześniej" : "spokojniej z gazem na wyjściu";
            }
            if (!o.Train || !targetCorners[k].Braked) return;
            float old = MarginFor(c.Num);
            float now = Math.Min(MarginMax, old + (entry ? MarginEntry : MarginExit));
            if (now <= old) return;
            margin[c.Num] = now;
            SaveMargins();
            Console.WriteLine(string.Format(Inv, "      margines Z{0}: ostatnie piknięcie {1:0} m wcześniej", c.Num, now));
            Enqueue(string.Format("Zakręt {0}, następnym razem piknę {1:0} metrów wcześniej", c.Num, now), false);
        }

        // A clean run through a corner (and the one before it) gives some of the margin back.
        void DecayMargin(int k)
        {
            if (offThisLap[k] || (k > 0 && offThisLap[k - 1])) return;
            int num = refCorners[k].Num;
            float old = MarginFor(num);
            if (old <= 0) return;
            float now = Math.Max(0, old - MarginDecay);
            if (now > 0) margin[num] = now; else margin.Remove(num);
            SaveMargins();
            Console.WriteLine(string.Format(Inv, "      margines Z{0}: {1:0} m{2}", num, now, now <= 0 ? " – zdjęty, czysty przejazd" : ""));
        }

        // ---------- rival ----------

        void SetupRival()
        {
            rival = Rival.Load(o.DataDir, si.Car, si.Track, si.Config);
            if (rival == null) return;
            sectorBounds = Sectors.Bounds(comboDir, si.Track, si.Config, refLap != null ? refLap.Trace : null);
            if (sectorBounds == null || sectorBounds.Length + 1 != rival.SectorCount)
            {
                Console.WriteLine(string.Format("Rywal {0}: nie znam granic sektorów tego toru – porównanie tylko na mecie.", rival.Name));
                sectorBounds = null;
                return;
            }
            mySectorBest = null;
            foreach (Lap l in allLaps.Where(l => l.Valid)) UpdateMySectors(l, false);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(string.Format(Inv, "RYWAL: {0} – najlepsze {1} ({2}), z najlepszych sektorów {3}",
                rival.Name, Lap.FormatTime(rival.BestLapMs), string.Join(" / ", rival.BestLapSectors.Select(x => (x / 1000.0).ToString("0.000", Inv)).ToArray()),
                Lap.FormatTime(rival.IdealMs)));
            Console.ResetColor();
            if (mySectorBest != null)
                Console.WriteLine(string.Format(Inv, "      Twoje najlepsze sektory: {0}, razem {1}",
                    string.Join(" / ", mySectorBest.Select(x => (x / 1000.0).ToString("0.000", Inv)).ToArray()), Lap.FormatTime(mySectorBest.Sum())));
        }

        void UpdateMySectors(Lap l, bool announce = true)
        {
            if (sectorBounds == null) return;
            if (l.Trace == null || Math.Abs(l.Trace.Length - length) > 1) l.BuildTrace(length);
            int[] s = Sectors.Times(l.Trace, sectorBounds, l.LapMs);
            if (s == null) return;
            if (mySectorBest == null) { mySectorBest = s; return; }
            for (int i = 0; i < s.Length; i++)
                if (s[i] < mySectorBest[i])
                {
                    mySectorBest[i] = s[i];
                    if (announce) Console.WriteLine(string.Format(Inv, "      Twój najlepszy sektor {0}: {1:0.000} s", i + 1, s[i] / 1000.0));
                }
        }

        // After each sector: your time against the rival's best lap (and your own best sector) on the console,
        // and "Sektor 1, plus 0,6 do rywala" by voice (the last sector goes into the lap-time call instead).
        void SectorDone(int i, float s, bool speak)
        {
            if (i >= rival.SectorCount) return;
            float gap = s - rival.BestLapSectors[i] / 1000f;
            Console.WriteLine(string.Format(Inv, "  [SEKTOR {0}] {1:0.000} s | {2} {3:0.000} (najlepszy {4:0.000}, średnio {5:0.000}) | {6:+0.00;-0.00}{7}",
                i + 1, s, rival.Name, rival.BestLapSectors[i] / 1000.0, rival.BestSectors[i] / 1000.0, rival.AvgSectors[i] / 1000.0, gap,
                mySectorBest != null ? string.Format(Inv, " | Twój najlepszy {0:0.000}", mySectorBest[i] / 1000.0) : ""));
            if (speak && !o.Quiet) Enqueue(string.Format("Sektor {0}, {1} do rywala", i + 1, Rival.Spoken(gap)), false);
        }

        // ---------- focus mode ----------

        bool SpeakAbout(int num) { return !o.Focus || focusNum == 0 || num == focusNum; }

        // The corner that costs the most over the last 30 laps: average loss of clean runs vs the best lap,
        // plus 1 s for every off-track per attempt, plus half of what the best run there already showed is possible.
        void ChooseFocus(bool announce)
        {
            if (refCorners == null || refCorners.Count == 0) return;
            int n = refCorners.Count;
            if (o.FocusCorner > 0 && focusNum == 0 && refCorners.Any(c => c.Num == o.FocusCorner) && !focusDone.Contains(o.FocusCorner))
            {
                SetFocus(o.FocusCorner, "wybrany ręcznie", announce);
                return;
            }
            List<Lap> recent = allLaps.Where(l => l.File != refLap.File && l.Trace != null && l.Trace.Filled >= l.Trace.N - 5).ToList();
            recent = recent.Skip(Math.Max(0, recent.Count - 30)).ToList();
            int best = -1; float bestScore = float.MinValue; string why = "";
            for (int k = 0; k < n; k++)
            {
                if (focusDone.Contains(refCorners[k].Num)) continue;
                List<float> deltas = new List<float>();
                int offs = 0;
                foreach (Lap l in recent)
                {
                    bool[] off = new bool[n];
                    if (l.OffTrackD != null) foreach (float d in l.OffTrackD) off[OwnerCorner(d)] = true;
                    if (off[k]) { offs++; continue; }
                    if (k > 0 && off[k - 1]) continue;
                    float dt = Seg(l.Trace, refCorners[k]) - refSeg[k];
                    if (Math.Abs(dt) < 3) deltas.Add(dt);
                }
                int attempts = deltas.Count + offs;
                if (attempts == 0) continue;
                float loss = deltas.Count > 0 ? deltas.Average() : 0, potential = refSeg[k] - targetSeg[k];
                float score = loss + (float)offs / attempts + 0.5f * potential;
                if (score > bestScore)
                {
                    bestScore = score; best = k;
                    why = string.Format(Inv, "średnio {0:+0.00;-0.00} s, wyjazdy {1}/{2}, Twój najlepszy przejazd szybszy o {3:0.00} s", loss, offs, attempts, potential);
                }
            }
            if (best >= 0) SetFocus(refCorners[best].Num, why, announce);
            else if (focusDone.Count >= n)
                StFocus = "wszystkie zakręty opanowane";
            else if (StFocus != "wybiorę po następnym okrążeniu")
            {
                StFocus = "wybiorę po następnym okrążeniu";
                Console.WriteLine("FOKUS: za mało okrążeń do porównania – wybiorę zakręt po następnym okrążeniu.");
            }
        }

        void SetFocus(int num, string why, bool announce)
        {
            focusNum = num; focusStreak = 0; focusTip = null; focusReminded = false;
            StFocus = "zakręt " + num;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(string.Format("FOKUS: zakręt {0} ({1}). Uwagi głosowe tylko o nim, piknięcia wszędzie.", num, why));
            Console.ResetColor();
            if (announce) Enqueue(string.Format("Fokus: zakręt {0}", num), false);
        }

        // After the focus corner: result, the streak of good runs, and moving on once it is mastered.
        string FocusCall(int k, CornerCmp cmp, string call, Tip tip)
        {
            bool good = cmp.SegDelta <= 0.03f && !offThisLap[k];
            focusStreak = good ? focusStreak + 1 : 0;
            if (tip != null) focusTip = tip.Action;
            else if (good) focusTip = null;
            if (good) call += string.Format(", dobrze {0} z {1}", focusStreak, FocusStreakToMaster);
            Console.WriteLine(string.Format("      fokus Z{0}: {1}, seria {2}/{3}", focusNum, good ? "dobrze" : "do poprawy", focusStreak, FocusStreakToMaster));
            if (focusStreak >= FocusStreakToMaster)
            {
                int done = focusNum;
                focusDone.Add(done);
                focusNum = 0;
                ChooseFocus(false);
                call += focusNum > 0 ? string.Format(". Zakręt {0} opanowany, nowy fokus: zakręt {1}", done, focusNum)
                                     : string.Format(". Zakręt {0} opanowany", done);
            }
            return call;
        }

        // Starts every corner's target at the reference lap, then lets every other recorded lap beat it.
        void BuildTargets()
        {
            int n = refCorners.Count;
            targetTrace = new Trace[n]; refSeg = new float[n]; targetSeg = new float[n]; targetFrom = new string[n];
            for (int k = 0; k < n; k++) { targetTrace[k] = refLap.Trace; refSeg[k] = targetSeg[k] = Seg(refLap.Trace, refCorners[k]); }
            targetCorners = new List<Corner>(refCorners);
            foreach (Lap l in allLaps)
                if (l != refLap && l.File != refLap.File) ConsiderForTargets(l);
            float gain = 0;
            for (int k = 0; k < n; k++)
                if (targetFrom[k] != null)
                {
                    gain += refSeg[k] - targetSeg[k];
                    Console.WriteLine(string.Format(Inv, "   Z{0}: cel = Twój najlepszy przejazd tego zakrętu ({1}), szybszy o {2:0.00} s",
                        refCorners[k].Num, targetFrom[k], refSeg[k] - targetSeg[k]));
                }
            idealMs = gain > 0.005f ? refLap.LapMs - (int)Math.Round(gain * 1000) : 0;
            if (gain > 0.005f)
                Console.WriteLine(string.Format(Inv, "Idealne okrążenie z Twoich najlepszych zakrętów: {0} (wzorzec {1})",
                    Lap.FormatTime(refLap.LapMs - (int)Math.Round(gain * 1000)), Lap.FormatTime(refLap.LapMs)));
        }

        // A lap's corner becomes the target when it is faster than the current target and was driven on track.
        // Returns the corners whose target changed.
        List<int> ConsiderForTargets(Lap l)
        {
            List<int> changed = new List<int>();
            if (l.Trace == null || Math.Abs(l.Trace.Length - length) > 1) l.BuildTrace(length);
            if (l.Trace.Filled < l.Trace.N - 5) return changed;
            int n = refCorners.Count;
            if (l.OffTrackD == null) l.Compact(length);
            bool[] dirty = new bool[n];
            foreach (float d in l.OffTrackD)
            {
                for (int k = 0; k < n; k++)
                    if (d >= refCorners[k].StartD - 30 && d <= refCorners[k].EndD + 150) dirty[k] = true;   // incl. a spin right after the exit
            }
            for (int k = 0; k < n; k++)
            {
                float s = Seg(l.Trace, refCorners[k]);
                if (dirty[k] || s < refSeg[k] - 1.5f || s >= targetSeg[k] - 0.01f) continue;   // off track, glitch, or not faster
                targetSeg[k] = s;
                targetTrace[k] = l.Trace;
                targetFrom[k] = l.Date.ToString("HH:mm", Inv) + " " + Lap.FormatTime(l.LapMs);
                Corner rc = refCorners[k];
                Corner tc = Corners.Measure(l.Trace, rc.ApexD, k > 0 ? refCorners[k - 1].ApexD : 0, k < n - 1 ? refCorners[k + 1].ApexD : length);
                tc.Num = rc.Num; tc.StartD = rc.StartD; tc.EndD = rc.EndD; tc.ExitD = rc.ExitD; tc.TurnSign = rc.TurnSign;
                tc.ExitSpeed = l.Trace.SpeedAt(rc.ExitD);
                targetCorners[k] = tc;
                changed.Add(k);
            }
            return changed;
        }

        void ResetTraining()
        {
            int n = refCorners == null ? 0 : refCorners.Count;
            beepStage = new int[n];
            beepPoint = new float[n];
            entryCued = new bool[n];
            finalBeepT = NaNs(n); finalBeepD = NaNs(n); brakeT = NaNs(n); brakeD = NaNs(n);
            reaction = NaNs(n);
            cornerTyresOut = new int[n];
            brakeArmed = new bool[n];
            offThisLap = new bool[n];
            focusReminded = false;
            beepLog.Clear();
        }

        static float[] NaNs(int n)
        {
            float[] a = new float[n];
            for (int i = 0; i < n; i++) a[i] = float.NaN;
            return a;
        }

        void LoadNotes()
        {
            notes.Clear();
            string f = Path.Combine(comboDir, "notes.txt");
            if (!File.Exists(f)) return;
            foreach (string raw in File.ReadAllLines(f))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim().TrimStart('Z', 'z');
                int num;
                if (int.TryParse(key, out num)) notes[num] = line.Substring(colon + 1).Trim();
            }
        }

        void StartLap(Telemetry t, bool fromLine)
        {
            if (si != null)
            {
                StaticInfo now = shm.ReadStatic();
                if (now.Car != si.Car || now.Track != si.Track || now.Config != si.Config) SetupCombo(now);
            }
            cur = new Lap();
            cur.Car = si.Car; cur.Track = si.Track; cur.Config = si.Config; cur.SplineLength = si.SplineLength;
            curFromLine = fromLine;
            curPit = t.IsInPitLane != 0;
            maxTyresOut = 0;
            lastT = -1;
            nextEval = 0;
            nextNote = 0;
            nextSector = 0;
            sectorStartS = 0;
            live = (refLap != null && length > 0) ? new Trace(length) : null;
            ResetTraining();
            LoadNotes();
        }

        void AddSample(Telemetry t)
        {
            if (t.CurrentTimeMs == lastT) return;
            lastT = t.CurrentTimeMs;
            Sample s = new Sample();
            s.T = t.CurrentTimeMs; s.Pos = t.Pos; s.Speed = t.SpeedKmh; s.Gas = t.Gas; s.Brake = t.Brake;
            s.Clutch = t.Clutch; s.Steer = t.Steer; s.Gear = t.Gear; s.Rpm = t.Rpm; s.LatG = t.LatG; s.LonG = t.LonG;
            s.X = t.X; s.Z = t.Z; s.TyresOut = t.TyresOut;
            s.Ext = t.Ext;
            cur.Samples.Add(s);
            lastSpeed = t.SpeedKmh;
            lastBrake = t.Brake;
            lastTyresOut = t.TyresOut;
            if (t.TyresOut > maxTyresOut) maxTyresOut = t.TyresOut;
            if (t.IsInPitLane != 0) curPit = true;
            if (live != null)
            {
                float p = t.Pos;
                if (t.CurrentTimeMs < 5000 && p > 0.5f) return;   // still reading the previous lap's end
                live.Add(p * length, t.CurrentTimeMs / 1000f, t.SpeedKmh, t.Gas, t.Brake, t.Steer, t.X, t.Z);
                if (curFromLine && refCorners != null && t.TyresOut >= 3) OnOffTrack(p * length);
            }
        }

        void FinishLap(Telemetry t)
        {
            if (!curFromLine || cur.Samples.Count < 100)
            {
                Console.WriteLine("Okrążenie wyjazdowe / niepełne – pomijam.");
                return;
            }
            int lastSampleT = cur.Samples[cur.Samples.Count - 1].T;
            int lapMs = t.LastTimeMs;
            if (lapMs <= 0 || Math.Abs(lapMs - lastSampleT) > 3000) lapMs = lastSampleT;
            cur.LapMs = lapMs;
            cur.Valid = !curPit && maxTyresOut <= 2;
            cur.Date = DateTime.Now;
            string name = cur.Date.ToString("yyyyMMdd_HHmmss", Inv) + "_" + Lap.FormatTime(lapMs).Replace(':', '-') + (cur.Valid ? "" : "_niewazne") + ".csv";
            cur.Save(Path.Combine(comboDir, "laps", name));
            cur.File = Path.Combine(comboDir, "laps", name);
            SaveBeepLog(name);
            UpdateBeepLead(false);
            lapsThisSession++;

            string delta = "";
            string spoken = "Czas " + SpokenTime(lapMs);
            bool newBest = cur.Valid && (refLap == null || lapMs < refLap.LapMs);
            if (refLap != null)
            {
                float d = (lapMs - refLap.LapMs) / 1000f;
                delta = "  (" + Corners.Sec2(d) + ")";
                if (!newBest) spoken += (d >= 0 ? ", plus " : ", minus ") + Corners.Sec(d);
            }
            if (newBest) spoken += refLap == null ? ". Mam wzorzec" : ". Nowy rekord!";
            if (!cur.Valid) spoken += ", nieważne";
            Grip.TyreStats tyres = Grip.Tyres(cur.Samples);
            if (tyres != null)
            {
                bool cold = Grip.Cold(tyres, refTyres);
                if (cold) spoken += ", opony zimne";
                else if (tyresWereCold) spoken += ", opony rozgrzane";
                tyresWereCold = cold;
            }
            string targetTxt = "";
            if (plan != null)
            {
                float gt = (lapMs - plan.TargetMs) / 1000f;
                bool final = cur.Valid && o.TargetMs > 0 && lapMs <= o.TargetMs;
                spoken += final ? ", cel końcowy osiągnięty!" : gt <= 0 ? ", cel osiągnięty" : ", do celu " + Corners.Sec(gt);
                targetTxt = string.Format(Inv, "  cel {0}: {1}", Lap.FormatTime(plan.TargetMs), Corners.Sec2(gt));
            }
            if (rival != null && sectorBounds != null && nextSector == sectorBounds.Length)
            {
                SectorDone(nextSector, lapMs / 1000f - sectorStartS, false);
                spoken += ", do rywala " + Rival.Spoken((lapMs - rival.BestLapMs) / 1000f);
                Console.WriteLine(string.Format(Inv, "  rywal {0}: {1:+0.000;-0.000} s (najlepsze rywala {2})", rival.Name, (lapMs - rival.BestLapMs) / 1000f, Lap.FormatTime(rival.BestLapMs)));
                if (cur.Valid) UpdateMySectors(cur);
            }
            Console.ForegroundColor = newBest ? ConsoleColor.Green : (cur.Valid ? ConsoleColor.White : ConsoleColor.DarkGray);
            Console.WriteLine(string.Format("Okrążenie {0}: {1}{2}{3}{4}{5}", t.CompletedLaps, Lap.FormatTime(lapMs), delta,
                cur.Valid ? "" : "  NIEWAŻNE (" + (curPit ? "pit" : "poza torem") + ")", newBest ? "  ★ NOWY WZORZEC" : "", targetTxt));
            Console.ResetColor();
            if (tyres != null) Console.WriteLine("  " + Grip.Text(tyres) + (tyresWereCold ? "  – ZIMNE (chłodniejsze niż na najlepszym okrążeniu)" : ""));
            Enqueue(spoken, true);
            StLast = Lap.FormatTime(lapMs) + delta + (cur.Valid ? "" : "  nieważne") + (newBest ? "  ★ nowy wzorzec" : "");
            allLaps.Add(cur);
            if (newBest) { SetReference(cur, true); RebuildModel(); }
            else if (refCorners != null)
                foreach (int k in ConsiderForTargets(cur))
                    Console.WriteLine(string.Format(Inv, "   Z{0}: nowy najlepszy przejazd tego zakrętu, cel szybszy o {1:0.00} s od wzorca", refCorners[k].Num, refSeg[k] - targetSeg[k]));
            if (o.Focus && focusNum == 0) ChooseFocus(true);   // also when focus was switched on from the panel
        }

        static string SpokenTime(int ms)
        {
            int m = ms / 60000;
            double s = (ms % 60000) / 1000.0;
            string ss = s.ToString("0.0", CultureInfo.GetCultureInfo("pl-PL"));
            return m > 0 ? m + " " + ss : ss;
        }

        void Enqueue(string text, bool front)
        {
            Msg m = new Msg(); m.Text = text; m.At = DateTime.Now;
            if (front) queue.Insert(0, m); else queue.Add(m);
            while (queue.Count > 3) queue.RemoveAt(queue.Count - 1);
        }

        void ProcessVoice()
        {
            float d = live != null ? live.FilledDistance : -1;
            if (live != null && refCorners != null && curFromLine)
            {
                if (o.Train) Training(d);
                while (nextNote < refCorners.Count && d >= refCorners[nextNote].OnsetD - 300)
                {
                    Corner c = refCorners[nextNote];
                    string note;
                    if (notes.TryGetValue(c.Num, out note) && d < c.OnsetD - 120 && SpeakAbout(c.Num)) Enqueue(note, false);
                    nextNote++;
                }
                if (o.Focus && focusNum > 0 && !focusReminded && !o.Quiet)
                {
                    Corner fc = refCorners.FirstOrDefault(c => c.Num == focusNum);
                    if (fc != null && d >= fc.OnsetD - 350 && d < fc.OnsetD - 100)
                    {
                        focusReminded = true;
                        Enqueue(focusTip != null ? string.Format("Zakręt {0}: {1}", focusNum, focusTip) : string.Format("Fokus, zakręt {0}", focusNum), true);
                    }
                }
                if (rival != null && sectorBounds != null)
                    while (nextSector < sectorBounds.Length && d >= sectorBounds[nextSector])
                    {
                        float ts = live.TimeAt(sectorBounds[nextSector]);
                        SectorDone(nextSector, ts - sectorStartS, true);
                        sectorStartS = ts;
                        nextSector++;
                    }
                // judge each corner soon after the exit (60 m past full throttle), not at the end of its segment
                while (nextEval < refCorners.Count && d >= EvalD(refCorners[nextEval]))
                {
                    int k = nextEval;
                    Corner rc = refCorners[k];
                    float evalD = EvalD(rc);
                    // better/worse: against the best lap; what to fix: against the best version of this corner
                    CornerCmp cmp = Corners.Compare(refLap.Trace, refCorners, k, live, evalD);
                    bool ownTarget = targetTrace[k] != refLap.Trace;
                    CornerCmp tcmp = ownTarget ? Corners.Compare(targetTrace[k], targetCorners, k, live, evalD) : cmp;
                    Tip tip = Corners.MakeTip(tcmp, o.Train);
                    float total = live.TimeAt(evalD) - refLap.Trace.TimeAt(evalD);
                    List<WheelEvent> wev = CornerWheelEvents(rc.StartD - 30, evalD);
                    string wheelTxt = wev.Count > 0 ? " | koła: " + string.Join(", ", wev.Select(w => Grip.Name(w.Kind) + string.Format(Inv, " {0:0.00} s", w.Dur))) : "";
                    string wheelKind = wev.Count > 0 ? Grip.Kinds.First(k2 => wev.Any(w => w.Kind == k2)) : null;
                    float vsPlan = float.NaN;
                    string planHint = null, planTxt = "";
                    if (plan != null)
                    {
                        vsPlan = (live.TimeAt(evalD) - live.TimeAt(rc.StartD)) - (plan.TimeAt(evalD) - plan.TimeAt(rc.StartD));
                        float faster = plan.MinSpeed(rc.StartD, evalD) - LiveMinSpeed(rc.StartD, evalD);
                        if (faster >= 4 && vsPlan >= 0.05f) planHint = string.Format(Inv, "szczyt szybciej o {0:0}", faster);
                        planTxt = string.Format(Inv, " | cel {0} (łącznie {1}){2}", Corners.Sec2(vsPlan), Corners.Sec2(live.TimeAt(evalD) - plan.TimeAt(evalD)),
                            planHint != null ? ", " + planHint + " km/h" : "");
                    }
                    Console.WriteLine(string.Format(Inv, "  Z{0,-2} {1,6} s | hamowanie {2,6} siła {6,5} | szczyt {3,4:+0;-0;0} km/h | gaz {4,6} | linia szczyt {5} | łącznie {7}{8}",
                        cmp.Ref.Num, Corners.Sec2(cmp.SegDelta), Metres(tcmp.BrakeDiff), tcmp.MinSpdDiff, Metres(tcmp.ThrDiff), LineText(cmp.ApexOff), Pct(tcmp.BrakeForceDiff), Corners.Sec2(total), planTxt + wheelTxt));
                    if (o.Train && targetCorners[k].Braked) LogBeep(k, tcmp);
                    DecayMargin(k);
                    if (!o.Quiet)
                    {
                        string call = CornerCall(rc.Num, cmp.SegDelta, ownTarget ? tcmp.SegDelta : cmp.SegDelta, tip, vsPlan, planHint, wheelKind);
                        if (o.Focus && rc.Num == focusNum) Enqueue(FocusCall(k, cmp, call, tip), false);
                        else if (SpeakAbout(rc.Num)) Enqueue(call, false);
                    }
                    nextEval++;
                }
            }
            if (queue.Count == 0) return;
            if ((DateTime.Now - queue[0].At).TotalSeconds > 8) { queue.RemoveAt(0); return; }
            // tips are spoken right away, anywhere on track; the beeps simply play over the voice
            if (voice.Busy || (DateTime.Now - lastSpoke).TotalSeconds < 1) return;
            voice.Say(queue[0].Text);
            queue.RemoveAt(0);
            lastSpoke = DateTime.Now;
        }

        // "Zakręt 3, lepiej o 0,12" / "Zakręt 3, gorzej o 0,2, hamujesz za wcześnie" / "Zakręt 3, równo".
        // vsBest = against the best lap; vsTarget = against the best version of this corner (record / what to fix).
        // vsBest: against the best lap; vsTarget: against the best run of this corner; vsPlan: against the
        // lap-time target's plan (NaN = no target)
        static string CornerCall(int num, float vsBest, float vsTarget, Tip tip, float vsPlan = float.NaN, string planHint = null, string wheel = null)
        {
            string s = "Zakręt " + num + ", ";
            if (vsBest <= -0.03f) s += "lepiej o " + SpokenDelta(-vsBest);
            else if (vsBest >= 0.03f) s += "gorzej o " + SpokenDelta(vsBest);
            else s += "równo";
            bool tipSaid = false;
            if (vsTarget < -0.02f && vsBest < 0.03f) s += ", rekord zakrętu";
            else if (tip != null && vsTarget > 0.05f) { s += ", " + tip.Action; tipSaid = true; }
            // what the tyres did: a locked wheel always, the rest when the corner cost time
            bool lost = vsBest >= 0.05f || vsTarget > 0.05f || (!float.IsNaN(vsPlan) && vsPlan >= 0.05f);
            if (wheel != null && !(wheel == "over" && tipSaid && tip.Action.Contains("uślizg")) && (wheel.StartsWith("lock") || (lost && !tipSaid)))
            {
                s += ", " + Grip.Spoken(wheel);
                tipSaid = true;
            }
            if (!float.IsNaN(vsPlan))
            {
                if (vsPlan <= 0.02f) s += ", w celu";
                else if (vsPlan >= 0.05f)
                {
                    s += ", do celu " + SpokenDelta(vsPlan);
                    if (planHint != null && !tipSaid) s += ", " + planHint;
                }
            }
            return s;
        }

        // wheel events of the current lap between two distances (scans back from the newest sample)
        List<WheelEvent> CornerWheelEvents(float from, float to)
        {
            List<Sample> s = cur.Samples;
            Func<int, float> d = i => { float p = s[i].Pos; if (s[i].T < 5000 && p > 0.5f) p -= 1; return p * length; };
            int a = s.Count - 1;
            while (a > 0 && d(a) > from) a--;
            int b = s.Count;
            while (b > a && d(b - 1) > to) b--;
            return Grip.Detect(s, a, b, d);
        }

        float LiveMinSpeed(float from, float to)
        {
            int a = Math.Max(0, (int)(from / Trace.Step)), b = Math.Min(live.Filled - 1, (int)(to / Trace.Step));
            float m = float.MaxValue;
            for (int i = a; i <= b; i++) m = Math.Min(m, live.Spd[i]);
            return m == float.MaxValue ? 0 : m;
        }

        static string SpokenDelta(float s)
        {
            return s.ToString(s < 0.1f ? "0.00" : "0.0", CultureInfo.GetCultureInfo("pl-PL"));
        }

        static float EvalD(Corner c)
        {
            return Math.Min(c.EndD, (float.IsNaN(c.ThrottleD) ? c.ApexD + 40 : c.ThrottleD) + 60);
        }

        // One line per braked corner: when the final beep played vs when the brake went on, where it went on
        // vs the point the beep aimed at, and what came of it.
        void LogBeep(int k, CornerCmp cmp)
        {
            Corner c = targetCorners[k];
            float target = beepPoint[k] > 0 ? beepPoint[k] : float.NaN;
            float miss = brakeD[k] - target;
            if (!float.IsNaN(reaction[k]) || !float.IsNaN(miss))
                Console.WriteLine(string.Format(Inv, "      piknięcie -> hamulec: {0} | punkt hamowania: {1} | siła {2:0}% (cel {3:0}%){4}",
                    float.IsNaN(reaction[k]) ? "–" : reaction[k].ToString("+0.00;-0.00", Inv) + " s",
                    float.IsNaN(miss) ? "–" : miss.ToString("+0;-0;0", Inv) + " m", cmp.Lap.BrakeForce * 100, c.BrakeForce * 100,
                    cornerTyresOut[k] >= 3 ? ", WYJAZD POZA TOR" : ""));
            beepLog.Add(string.Format(Inv, "{0},{1:0},{2:0},{3:0},{4:0.00},{5:0},{6:0},{7:0},{8:0},{9},{10:0.00},{11:0},{12:0},{13:0.00}",
                c.Num, c.BrakeD, finalBeepD[k], brakeD[k], reaction[k], c.BrakeForce * 100, cmp.Lap.BrakeForce * 100,
                cmp.Lap.MinSpeed, c.MinSpeed, cornerTyresOut[k], cmp.SegDelta, target, miss, LeadFor(c.Num)));
        }

        // Reaction time from pikniecia.csv: how long after the final beep the brake usually goes on (negative =
        // just before it, on the rhythm). The final beep is played that much early so the brake lands on the point:
        // the median of the last 40 brakings for all corners, and per corner from its last 8 once it has 4 or more.
        // --beep-lead fixes it for every corner.
        void UpdateBeepLead(bool announce)
        {
            float old = beepLead;
            Dictionary<int, float> oldCorner = new Dictionary<int, float>(cornerLead);
            cornerLead.Clear();
            if (!float.IsNaN(o.BeepLead)) beepLead = o.BeepLead;
            else
            {
                List<float> all = new List<float>();
                Dictionary<int, List<float>> per = new Dictionary<int, List<float>>();
                string f = Path.Combine(comboDir, "pikniecia.csv");
                if (File.Exists(f))
                    foreach (string line in File.ReadAllLines(f).Skip(1))
                    {
                        string[] p = line.Split(',');
                        float x; int num;
                        if (p.Length <= 5 || !int.TryParse(p[1], out num)) continue;
                        if (!float.TryParse(p[5], NumberStyles.Float, Inv, out x) || x < -0.4f || x > 0.9f) continue;
                        all.Add(x);
                        if (!per.ContainsKey(num)) per[num] = new List<float>();
                        per[num].Add(x);
                    }
                beepLead = all.Count >= 5 ? ClampLead(Median(all.Skip(Math.Max(0, all.Count - 40)))) : DefaultLead;
                foreach (KeyValuePair<int, List<float>> kv in per)
                    if (kv.Value.Count >= 4) cornerLead[kv.Key] = ClampLead(Median(kv.Value.Skip(Math.Max(0, kv.Value.Count - 8))));
            }
            if (!o.Train) return;
            if (announce || Math.Abs(beepLead - old) >= 0.02f)
                Console.WriteLine(string.Format(Inv, "Ostatnie piknięcie {0:0.00} s przed punktem hamowania ({1})", beepLead,
                    !float.IsNaN(o.BeepLead) ? "ustawione ręcznie" : "dopasowane do Twojej reakcji"));
            foreach (KeyValuePair<int, float> kv in cornerLead.OrderBy(x => x.Key))
            {
                float prev;
                bool changed = !oldCorner.TryGetValue(kv.Key, out prev) || Math.Abs(prev - kv.Value) >= 0.02f;
                if ((announce || changed) && Math.Abs(kv.Value - beepLead) >= 0.03f)
                    Console.WriteLine(string.Format(Inv, "   Z{0}: {1:0.00} s (tu reagujesz {2})", kv.Key, kv.Value, kv.Value > beepLead ? "wolniej" : "szybciej"));
            }
        }

        float LeadFor(int num)
        {
            float l;
            return cornerLead.TryGetValue(num, out l) ? l : beepLead;
        }

        static float ClampLead(float v) { return Math.Max(MinLead, Math.Min(MaxLead, v)); }

        static float Median(IEnumerable<float> xs)
        {
            List<float> l = xs.OrderBy(x => x).ToList();
            return l.Count == 0 ? float.NaN : (l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2);
        }

        void SaveBeepLog(string lapName)
        {
            if (beepLog.Count == 0) return;
            string f = Path.Combine(comboDir, "pikniecia.csv");
            List<string> lines = new List<string>();
            if (!File.Exists(f)) lines.Add("lap,corner,ref_brake_m,beep_m,brake_m,reaction_s,force_target_pct,force_pct,min_speed,ref_min_speed,tyres_out,seg_delta_s,target_m,miss_m,lead_s");
            foreach (string l in beepLog) lines.Add(lapName + "," + l);
            File.AppendAllLines(f, lines);
        }

        // Brake countdown beeps (aimed at the best version of each corner) and the "go wider" cue before each corner.
        void Training(float d)
        {
            float v = Math.Max(lastSpeed / 3.6f, 5f);
            for (int k = 0; k < refCorners.Count; k++)
            {
                Corner c = refCorners[k];

                if (!entryCued[k] && c.TurnSign != 0 && d >= c.OnsetD - 150)
                {
                    entryCued[k] = true;
                    float off = Corners.Offset(refLap.Trace, live, d, c.TurnSign);
                    if (d < c.OnsetD - 90 && off > 1.5f)
                    {
                        Console.WriteLine(string.Format(Inv, "  Z{0}: {1:0.0} m za blisko wewnętrznej przed zakrętem", c.Num, off));
                        if (SpeakAbout(c.Num)) Enqueue("Szerzej", true);
                    }
                }

                Corner t = targetCorners[k];
                Trace tt = targetTrace[k];
                if (!t.Braked) continue;
                float lead = LeadFor(t.Num);
                if (d >= t.BrakeD - 50 && d <= t.EndD) cornerTyresOut[k] = Math.Max(cornerTyresOut[k], lastTyresOut);

                // first press of the brake in this braking zone
                float prevApex = k > 0 ? refCorners[k - 1].ApexD : 0;
                if (!brakeArmed[k] && lastBrake < 0.05f && d > prevApex) brakeArmed[k] = true;
                if (float.IsNaN(brakeT[k]) && brakeArmed[k] && lastBrake > 0.1f && d >= Math.Max(t.BrakeD - 200, prevApex + 10) && d <= t.ApexD)
                {
                    brakeT[k] = lastT / 1000f; brakeD[k] = d;
                    if (!float.IsNaN(finalBeepT[k]))
                    {
                        reaction[k] = brakeT[k] - finalBeepT[k];
                        if (reaction[k] > LateS) { Console.WriteLine(string.Format(Inv, "  [PÓŹNO] Z{0}: hamulec {1:0.00} s po piknięciu", t.Num, reaction[k])); if (SpeakAbout(t.Num)) Enqueue("Późno", true); }
                    }
                    else if (beepStage[k] < BeepAt.Length && beepPoint[k] > 0)
                    {
                        // braked before the final beep: stop the countdown so it does not beep mid-braking
                        float early = (beepPoint[k] - d) / v - lead;
                        reaction[k] = -early;
                        beepStage[k] = BeepAt.Length;
                        if (early > EarlyS) { Console.WriteLine(string.Format(Inv, "  [WCZEŚNIE] Z{0}: hamulec {1:0.00} s przed piknięciem", t.Num, early)); if (SpeakAbout(t.Num)) Enqueue("Wcześnie", true); }
                    }
                }

                if (beepStage[k] >= BeepAt.Length) continue;
                if (d > t.ApexD) { beepStage[k] = BeepAt.Length; continue; }
                if (d < t.BrakeD - 500) continue;
                // faster than on the target lap -> brake earlier, slower -> later (braking distance ~ v²).
                // Speed at the braking point is predicted from the gap to the target at the same spot,
                // updated every tick until the final beep.
                float vb = tt.SpeedAt(t.BrakeD) / 3.6f, vm = t.MinSpeed / 3.6f;
                float ve = vb + v - tt.SpeedAt(d) / 3.6f;
                float f = 1f;
                if (vb > vm + 3 && ve > vm + 3) f = Math.Max(0.75f, Math.Min(1.3f, (ve * ve - vm * vm) / (vb * vb - vm * vm)));
                beepPoint[k] = t.ApexD - (t.ApexD - t.BrakeD) * f;
                beepPoint[k] -= MarginFor(t.Num);   // earlier after off-tracks here
                float ttb = (beepPoint[k] - d) / v;
                int stage = beepStage[k];
                while (stage < BeepAt.Length && ttb <= BeepAt[stage] + lead) stage++;
                if (stage == beepStage[k]) continue;
                bool final = stage == BeepAt.Length;
                beepStage[k] = stage;
                if (final) { finalBeepT[k] = lastT / 1000f; finalBeepD[k] = d; }
                beeper.Beep(final, t.ForceLevel);
                if (!final) Console.WriteLine(string.Format(Inv, "  [PIK {0}/3] Z{1}", stage, t.Num));
                if (final) Console.WriteLine(string.Format(Inv, "  [HAMUJ] Z{0} @{1:0} m {2} ({3:0}%)", t.Num, beepPoint[k], t.ForceName, t.BrakeForce * 100));
            }
        }

        static string LineText(float off)
        {
            if (float.IsNaN(off)) return "–";
            if (Math.Abs(off) < 0.5f) return "ok";
            return string.Format(Inv, "{0:0.0} m {1}", Math.Abs(off), off < 0 ? "szerzej" : "ciaśniej");
        }

        static string Metres(float v)
        {
            if (float.IsNaN(v)) return "–";
            return v.ToString("+0;-0;0", Inv) + " m";
        }

        static string Pct(float v)
        {
            if (float.IsNaN(v)) return "–";
            return (v * 100).ToString("+0;-0;0", Inv) + "%";
        }
    }
}
