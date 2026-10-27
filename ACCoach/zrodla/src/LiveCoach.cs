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
        public bool GameApp = true;          // write the data of the in-game app (apps\python\ACCoach)
    }

    // Records every lap to CSV and, once a reference lap exists, gives short spoken tips after each corner.
    public partial class LiveCoach
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
        float lastSpeed, lastBrake, lastPos;
        int lastTyresOut;
        readonly List<float[]> posJumps = new List<float[]>();   // this lap: stretches the car's position jumped over (from, to)
        float lastLiveD = -1, lastLiveT;
        readonly Dictionary<int, float> marginNote = new Dictionary<int, float>();   // corner index -> new margin after an off this lap
        int expectCount = -1;                  // lap counter value still to come for a crossing taken from the timer
        int prevLastMs;
        DateTime expectUntil, staleUntil;
        DateTime skipShownAt = DateTime.MinValue;   // "out lap" message: at most every 30 s
        int skipHidden, lapNotes, lapNotesIgnored;

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

        class Msg
        {
            public string Text;
            public string[] Shorter;               // shorter versions, for when there is little time before the next corner
            public DateTime At;
            public int Kind;                       // MsgInfo / MsgCorner / MsgInstant
            public int Corner = -1;                // corner index of a corner call
            public float DropD = float.MaxValue;   // not said any more once the car is past this distance
        }
        readonly List<Msg> queue = new List<Msg>();
        DateTime lastSpoke = DateTime.MinValue;

        public LiveCoach(Options opts) { o = opts; cliTarget = o.TargetMs > 0; }

        public void Run()
        {
            try { Console.CancelKeyPress += delegate (object s, ConsoleCancelEventArgs e) { e.Cancel = true; stop = true; }; } catch (Exception) { }   // no console in the window app
            try { timeBeginPeriod(1); } catch (Exception) { }   // finer Sleep on Windows
            voice = new Voice(false, o.Rate, o.VoiceName, o.Volume) { Muted = o.Mute };
            beeper = new Beeper(o.Mute || !o.Train, o.BeepVolume);
            Console.WriteLine(L.T("AC Coach – nagrywanie okrążeń i trener głosowy", "AC Coach – lap recording and voice coach"));
            Console.WriteLine(L.T("Dane: ", "Data: ") + o.DataDir);
            Console.WriteLine(L.T("Głos: ", "Voice: ") + voice.VoiceName + (o.Quiet ? L.T(" (tryb cichy: tylko czasy i notatki)", " (quiet: lap times and notes only)") : ""));
            if (o.Train) Console.WriteLine(L.T("TRYB TRENINGOWY: piknięcia do hamowania + uwagi o linii", "TRAINING MODE: braking beeps + racing line cues"));
            else Console.WriteLine(L.T("Piknięcia do hamowania WYŁĄCZONE (--no-train)", "Braking beeps OFF"));
            if (o.Focus) Console.WriteLine(L.T("TRYB FOKUS: uwagi głosowe tylko o jednym zakręcie", "FOCUS MODE: voice tips about one turn only") + (o.FocusCorner > 0 ? L.T(" (Z", " (T") + o.FocusCorner + ")" : L.T(", wybieranym automatycznie", ", picked automatically")));
            Console.WriteLine(L.T("Zatrzymanie: przycisk „Zatrzymaj trenera” albo zamknięcie okna\n", "Stop: the “Stop trainer” button or closing the window\n"));

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
                            if (!waitingShown) { Console.WriteLine(L.T("Czekam na Assetto Corsa...", "Waiting for Assetto Corsa...")); waitingShown = true; StState = L.T("Czekam na Assetto Corsa…", "Waiting for Assetto Corsa…"); }
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
                            Console.WriteLine(L.T("Sesja zakończona. Zapisanych okrążeń: ", "Session over. Laps saved: ") + lapsThisSession);
                            StState = L.T("Sesja zakończona – czekam na następną", "Session over – waiting for the next one");
                            if (o.Once) break;
                        }
                        Thread.Sleep(100);
                        continue;
                    }
                    if (!wasLive)
                    {
                        wasLive = true;
                        StState = L.T("Jazda – nagrywam okrążenia", "Driving – recording laps");
                        SetupCombo(shm.ReadStatic());
                        lastCompleted = t.CompletedLaps;
                        StartLap(t, false);
                    }
                    if (t.GraphicsPacket == lastPacket) { Thread.Sleep(5); continue; }
                    lastPacket = t.GraphicsPacket;

                    // At the line the lap counter goes up and the lap timer resets. Online both change in the same
                    // frame; offline (practice, career) one can come a few frames after the other, so each half of
                    // the crossing is accepted on its own and the other half is then expected, not taken as a restart.
                    bool timerReset = lastT >= 0 && t.CurrentTimeMs + 1000 < lastT;
                    if (t.CompletedLaps == lastCompleted + 1)
                    {
                        lastCompleted = t.CompletedLaps;
                        if (expectCount == t.CompletedLaps) { expectCount = -1; LapNote(t, "counter after timer"); }   // the timer already reset: lap done
                        else
                        {
                            LapNote(t, "counter");
                            FinishLap(t, t.CompletedLaps);
                            StartLap(t, true);
                            staleUntil = t.CurrentTimeMs > 3000 ? DateTime.Now.AddSeconds(3) : DateTime.MinValue;
                        }
                    }
                    else if (t.CompletedLaps == lastCompleted && timerReset && lastPos > 0.9f && (t.Pos < 0.1f || t.Pos > 0.97f) && RealCrossing(t))
                    {
                        // timer reset at the line before the counter (the game's last lap time may not be updated yet either)
                        if (t.LastTimeMs == prevLastMs) t.LastTimeMs = 0;
                        LapNote(t, "timer");
                        FinishLap(t, lastCompleted + 1);
                        StartLap(t, true);
                        expectCount = lastCompleted + 1;
                        expectUntil = DateTime.Now.AddSeconds(5);
                    }
                    else if (t.CompletedLaps != lastCompleted || timerReset)
                    {
                        // session restart, return to pits or a new session online
                        LapNote(t, "restart");
                        lastCompleted = t.CompletedLaps;
                        expectCount = -1;
                        StartLap(t, false);
                    }
                    if (expectCount >= 0 && DateTime.Now > expectUntil) expectCount = -1;
                    prevLastMs = t.LastTimeMs;
                    // counter already up, timer not reset yet: these frames still carry the old lap's time
                    if (staleUntil > DateTime.Now && t.CurrentTimeMs > 3000) { Thread.Sleep(5); continue; }
                    staleUntil = DateTime.MinValue;
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

        // A lap timer reset taken as the line only when it looks like one: the car is moving, it moved a few metres
        // forward since the last sample (not put back in the pits) and the timer had run for a lap. Standing in the
        // pit box just before the line with a timer that starts over is not a lap (1.8.1: "Out lap" over and over).
        bool RealCrossing(Telemetry t)
        {
            float len = si != null && si.SplineLength > 200 ? si.SplineLength : 3000;
            float moved = t.Pos - lastPos;
            if (moved < -0.5f) moved += 1;
            moved *= len;
            bool ok = t.SpeedKmh > 15 && lastSpeed > 15 && moved > -2 && moved < 120 && lastT > 10000;
            if (!ok) LapNote(t, string.Format(Inv, "timer reset ignored (moved {0:0} m)", moved));
            return ok;
        }

        // What the game reported at a lap change, to log.txt only: lets a problem with the line be traced later.
        void LapNote(Telemetry t, string what)
        {
            bool routine = what.StartsWith("restart") || what.StartsWith("timer reset ignored");   // repeats while standing
            if (routine ? ++lapNotesIgnored > 40 : ++lapNotes > 300) return;
            LogWriter.Note(string.Format(Inv, "[line] {0}: laps {1}->{2} timer {3}->{4} last {5} pos {6:0.0000}->{7:0.0000} speed {8:0} pit {9}",
                what, lastCompleted, t.CompletedLaps, lastT, t.CurrentTimeMs, t.LastTimeMs, lastPos, t.Pos, t.SpeedKmh, t.IsInPitLane));
        }

        void SetupCombo(StaticInfo info)
        {
            si = info;
            comboDir = Lap.ComboDir(o.DataDir, si.Car, si.Track, si.Config);
            Directory.CreateDirectory(Path.Combine(comboDir, "laps"));
            Console.WriteLine(string.Format(L.T("Auto: {0}   Tor: {1} {2}", "Car: {0}   Track: {1} {2}"), si.Car, si.Track, si.Config));
            StCombo = si.Car + "  |  " + si.Track + (string.IsNullOrEmpty(si.Config) ? "" : " " + si.Config);
            length = (si.SplineLength > 200 && si.SplineLength < 50000) ? si.SplineLength : 0;
            allLaps = Lap.LoadAll(comboDir, length);
            refLap = Lap.BestValid(allLaps);
            UpdateBeepLead(true);
            LoadMargins();
            LoadAppTips();
            if (refLap != null) SetReference(refLap, false);
            else Console.WriteLine(L.T("Brak wzorca – przejedź jedno czyste okrążenie, wtedy włączą się podpowiedzi.", "No best lap yet – drive one clean lap and the tips will start."));
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
            Console.WriteLine(string.Format(L.T("Wzorzec: {0}  ({1} m, zakrętów: {2})", "Best lap: {0}  ({1} m, turns: {2})"), Lap.FormatTime(lap.LapMs), (int)length, refCorners.Count));
            StRef = Lap.FormatTime(lap.LapMs) + L.T("  (zakrętów: ", "  (turns: ") + refCorners.Count + ")";
            if (announce)
                foreach (Corner c in refCorners)
                    Console.WriteLine(string.Format(Inv, L.T("   Z{0,-2} szczyt {1,5:0} m  {2,4:0} km/h", "   T{0,-2} apex {1,5:0} m  {2,4:0} km/h"), c.Num, c.ApexD, c.MinSpeed));
            if (o.Train && !Corners.HasPositions(lap.Trace))
                Console.WriteLine(L.T("Uwaga: brak pozycji auta we wzorcu – uwagi o linii będą wyłączone.", "Note: no car positions in the best lap – racing line cues are off."));
            BuildTargets();
            ResetTraining();
            WriteGameData();
        }

        static float Seg(Trace tr, Corner c) { return tr.TimeAt(c.EndD) - tr.TimeAt(c.StartD); }

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
                    Console.WriteLine(string.Format(Inv, L.T("   Z{0}: cel = Twój najlepszy przejazd tego zakrętu ({1}), szybszy o {2:0.00} s", "   T{0}: target = your best run through this turn ({1}), {2:0.00} s faster"),
                        refCorners[k].Num, targetFrom[k], refSeg[k] - targetSeg[k]));
                }
            idealMs = gain > 0.005f ? refLap.LapMs - (int)Math.Round(gain * 1000) : 0;
            if (gain > 0.005f)
                Console.WriteLine(string.Format(Inv, L.T("Idealne okrążenie z Twoich najlepszych zakrętów: {0} (wzorzec {1})", "Ideal lap from your best turns: {0} (best lap {1})"),
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
                string key = line.Substring(0, colon).Trim().TrimStart('Z', 'z', 'T', 't');
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
            posJumps.Clear();
            marginNote.Clear();
            lastLiveD = -1;
            ResetTraining();
            LoadNotes();
            AppLapStart();
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
            lastPos = t.Pos;
            lastTyresOut = t.TyresOut;
            if (t.TyresOut > maxTyresOut) maxTyresOut = t.TyresOut;
            if (t.IsInPitLane != 0) curPit = true;
            if (live != null)
            {
                float p = t.Pos;
                if (t.CurrentTimeMs < 5000 && p > 0.5f) return;   // still reading the previous lap's end
                // off track the game can place the car on another part of the track for a moment: a forward jump
                // the car cannot have driven is remembered, the corners it covers are not judged
                float dd = p * length, ts = t.CurrentTimeMs / 1000f;
                if (lastLiveD >= 0 && dd - lastLiveD > 40 + 2 * Math.Max(t.SpeedKmh, lastSpeed) / 3.6f * Math.Max(0.05f, ts - lastLiveT))
                {
                    posJumps.Add(new[] { lastLiveD, dd });
                    Console.WriteLine(string.Format(Inv, L.T("  [SKOK POZYCJI] {0:0} m → {1:0} m", "  [POSITION JUMP] {0:0} m → {1:0} m"), lastLiveD, dd));
                }
                if (dd > lastLiveD) { lastLiveD = dd; lastLiveT = ts; }
                live.Add(dd, ts, t.SpeedKmh, t.Gas, t.Brake, t.Steer, t.X, t.Z);
                if (curFromLine && refCorners != null && t.TyresOut >= 3) OnOffTrack(p * length);
            }
        }

        void FinishLap(Telemetry t, int lapNo)
        {
            if (!curFromLine || cur.Samples.Count < 100)
            {
                if ((DateTime.Now - skipShownAt).TotalSeconds < 30) { skipHidden++; return; }
                Console.WriteLine(L.T("Okrążenie wyjazdowe / niepełne – pomijam.", "Out lap / incomplete lap – skipped.")
                    + (skipHidden > 0 ? string.Format(L.T(" (i {0} razy wcześniej)", " (and {0} times before)"), skipHidden) : ""));
                skipShownAt = DateTime.Now;
                skipHidden = 0;
                return;
            }
            int lastSampleT = cur.Samples[cur.Samples.Count - 1].T;
            int lapMs = t.LastTimeMs;
            // the game's lap time is the crossing, just after the last sample; anything else is not updated yet:
            // then the last sample's time plus the few metres left to the line
            if (lapMs < lastSampleT - 100 || lapMs > lastSampleT + 500)
            {
                Sample ls = cur.Samples[cur.Samples.Count - 1];
                float left = ls.Pos > 0.9f && si.SplineLength > 0 ? (1 - ls.Pos) * si.SplineLength : 0;
                lapMs = lastSampleT + (int)Math.Min(200, left / Math.Max(ls.Speed / 3.6f, 1f) * 1000);
            }
            cur.LapMs = lapMs;
            cur.Valid = !curPit && maxTyresOut <= 2;
            cur.Date = DateTime.Now;
            string name = cur.Date.ToString("yyyyMMdd_HHmmss", Inv) + "_" + Lap.FormatTime(lapMs).Replace(':', '-') + (cur.Valid ? "" : "_niewazne") + ".csv";
            cur.Save(Path.Combine(comboDir, "laps", name));
            cur.File = Path.Combine(comboDir, "laps", name);
            SaveBeepLog(name);
            SaveAppLog(name);
            UpdateBeepLead(false);
            lapsThisSession++;

            string delta = "";
            string spoken = L.T("Czas ", "Time ") + SpokenTime(lapMs);
            bool newBest = cur.Valid && (refLap == null || lapMs < refLap.LapMs);
            if (refLap != null)
            {
                float d = (lapMs - refLap.LapMs) / 1000f;
                delta = "  (" + Corners.Sec2(d) + ")";
                if (!newBest) spoken += (d >= 0 ? L.T(", plus ", ", plus ") : L.T(", minus ", ", minus ")) + Corners.Sec(d);
            }
            if (newBest) spoken += refLap == null ? L.T(". Mam wzorzec", ". Best lap set") : L.T(". Nowy rekord!", ". New record!");
            if (!cur.Valid) spoken += L.T(", nieważne", ", invalid");
            Grip.TyreStats tyres = Grip.Tyres(cur.Samples);
            if (tyres != null)
            {
                bool cold = Grip.Cold(tyres, refTyres);
                if (cold) spoken += L.T(", opony zimne", ", tyres cold");
                else if (tyresWereCold) spoken += L.T(", opony rozgrzane", ", tyres warm");
                tyresWereCold = cold;
            }
            string targetTxt = "";
            if (plan != null)
            {
                float gt = (lapMs - plan.TargetMs) / 1000f;
                bool final = cur.Valid && o.TargetMs > 0 && lapMs <= o.TargetMs;
                spoken += final ? L.T(", cel końcowy osiągnięty!", ", final target reached!") : gt <= 0 ? L.T(", cel osiągnięty", ", target reached") : L.T(", do celu ", ", to target ") + Corners.Sec(gt);
                targetTxt = string.Format(Inv, L.T("  cel {0}: {1}", "  target {0}: {1}"), Lap.FormatTime(plan.TargetMs), Corners.Sec2(gt));
            }
            if (rival != null && sectorBounds != null && nextSector == sectorBounds.Length)
            {
                SectorDone(nextSector, lapMs / 1000f - sectorStartS, false);
                spoken += L.T(", do rywala ", ", to rival ") + Rival.Spoken((lapMs - rival.BestLapMs) / 1000f);
                Console.WriteLine(string.Format(Inv, L.T("  rywal {0}: {1:+0.000;-0.000} s (najlepsze rywala {2})", "  rival {0}: {1:+0.000;-0.000} s (rival's best {2})"), rival.Name, (lapMs - rival.BestLapMs) / 1000f, Lap.FormatTime(rival.BestLapMs)));
                if (cur.Valid) UpdateMySectors(cur);
            }
            Console.ForegroundColor = newBest ? ConsoleColor.Green : (cur.Valid ? ConsoleColor.White : ConsoleColor.DarkGray);
            Console.WriteLine(string.Format(L.T("Okrążenie {0}: {1}{2}{3}{4}{5}", "Lap {0}: {1}{2}{3}{4}{5}"), lapNo, Lap.FormatTime(lapMs), delta,
                cur.Valid ? "" : L.T("  NIEWAŻNE (", "  INVALID (") + (curPit ? "pit" : L.T("poza torem", "off track")) + ")", newBest ? L.T("  ★ NOWY WZORZEC", "  ★ NEW BEST LAP") : "", targetTxt));
            Console.ResetColor();
            if (tyres != null) Console.WriteLine("  " + Grip.Text(tyres) + (tyresWereCold ? L.T("  – ZIMNE (chłodniejsze niż na najlepszym okrążeniu)", "  – COLD (colder than on the best lap)") : ""));
            Enqueue(spoken, true);
            StLast = Lap.FormatTime(lapMs) + delta + (cur.Valid ? "" : L.T("  nieważne", "  invalid")) + (newBest ? L.T("  ★ nowy wzorzec", "  ★ new best lap") : "");
            allLaps.Add(cur);
            if (newBest) { SetReference(cur, true); RebuildModel(); }
            else if (refCorners != null)
                foreach (int k in ConsiderForTargets(cur))
                    Console.WriteLine(string.Format(Inv, L.T("   Z{0}: nowy najlepszy przejazd tego zakrętu, cel szybszy o {1:0.00} s od wzorca", "   T{0}: new best run through this turn, target {1:0.00} s faster than the best lap"), refCorners[k].Num, refSeg[k] - targetSeg[k]));
            if (o.Focus && focusNum == 0) ChooseFocus(true);   // also when focus was switched on from the panel
            WriteGameData();
        }

        static string SpokenTime(int ms)
        {
            int m = ms / 60000;
            double s = (ms % 60000) / 1000.0;
            string ss = s.ToString("0.0", L.Culture);
            return m > 0 ? m + " " + ss : ss;
        }

    }
}
