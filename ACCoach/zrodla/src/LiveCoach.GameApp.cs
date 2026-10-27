using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ACCoach
{
    // Data for the in-game app. <combo>.txt: the braking points of the best run of every turn and the best-lap time
    // along the track (rewritten when they change). <combo>.live.txt: one thing to fix before each turn and the
    // result right after it (rewritten after every turn). The app shows nothing inside the turn but the countdown.
    public partial class LiveCoach
    {
        readonly Dictionary<int, string> appTip = new Dictionary<int, string>();   // turn number -> what to do there next time
        readonly Dictionary<int, int> appGood = new Dictionary<int, int>();        // turn number -> good runs in a row
        int appSeq;
        string appResult;                                // "seq num kind headline|detail" of the last turn, null = none yet
        readonly List<string> appLog = new List<string>();   // this lap, for the track map: d, kind, turn, text
        const int AppQuietAfter = 3;                     // good runs in a row: the turn's result is not shown any more

        // Rewritten (in the game and in <combo>\aplikacja_w_grze.txt) whenever the best lap, a corner's best run, a margin or the reaction time changes.
        void WriteGameData()
        {
            if (refLap == null || refCorners == null || targetTrace == null || length <= 0 || si == null) return;
            Trace rt = refLap.Trace;
            int n = rt.N;
            List<string> l = new List<string>();
            l.Add("# AC Coach – data of the in-game app: turns, braking points and the best lap along the track");
            l.Add("accoach 2");
            l.Add("car " + si.Car);
            l.Add("track " + si.Track);
            l.Add("config " + si.Config);
            l.Add(string.Format(Inv, "length {0:0.0}", length));
            l.Add("best_ms " + refLap.LapMs);
            l.Add(string.Format(Inv, "lead {0:0.00}", beepLead));
            // turn <num> <onset m> <apex m>: every turn, for the tip before it
            foreach (Corner c in refCorners)
                l.Add(string.Format(Inv, "turn {0} {1:0.0} {2:0.0}", c.Num, c.OnsetD, c.ApexD));
            // corner <num> <brake m> <apex m> <speed at the brake point> <apex speed> <margin m> <lead s>: braked turns, for the countdown
            for (int k = 0; k < targetCorners.Count; k++)
            {
                Corner t = targetCorners[k];
                if (!t.Braked) continue;
                l.Add(string.Format(Inv, "corner {0} {1:0.0} {2:0.0} {3:0.0} {4:0.0} {5:0} {6:0.00}",
                    t.Num, t.BrakeD, t.ApexD, targetTrace[k].SpeedAt(t.BrakeD), t.MinSpeed, MarginFor(t.Num), LeadFor(t.Num)));
            }
            // grid: best-lap time (for the delta) and the speed of the best run of each stretch (for the countdown)
            l.Add(string.Format(Inv, "grid {0:0.0} {1}", Trace.Step, n));
            for (int i = 0; i < n; i++)
                l.Add(string.Format(Inv, "{0:0.000} {1:0}", rt.T[i], targetTrace[OwnerCorner(i * Trace.Step)].Spd[i]));
            // a copy next to the laps: the track map of the analysis replays what the app showed
            try { GameApp.Write(Path.Combine(comboDir, GameApp.ComboCopy), l); } catch (Exception) { }
            string f = o.GameApp ? GameApp.DataFile(Path.GetFileName(comboDir)) : null;
            if (f == null) return;
            try { GameApp.Write(f, l); }
            catch (Exception e) { Console.WriteLine(L.T("Aplikacja w grze: nie udało się zapisać danych (", "In-game app: could not write its data (") + e.Message + ")"); }
        }

        // ---------- tips and results ----------

        string AppTipsFile { get { return Path.Combine(comboDir, "aplikacja_wskazowki.txt"); } }

        // "Z<num> <good runs in a row> <tip>" per turn, kept between sessions
        void LoadAppTips()
        {
            appTip.Clear(); appGood.Clear(); appResult = null;
            if (!File.Exists(AppTipsFile)) return;
            foreach (string line in File.ReadAllLines(AppTipsFile, Encoding.UTF8))
            {
                string[] p = line.Split(new[] { ' ' }, 3);
                int num, good;
                if (p.Length < 2 || !int.TryParse(p[0].TrimStart('Z'), out num) || !int.TryParse(p[1], out good)) continue;
                appGood[num] = good;
                if (p.Length == 3 && p[2].Trim().Length > 0) appTip[num] = p[2].Trim();
            }
        }

        void SaveAppTips()
        {
            List<string> l = new List<string>();
            foreach (int num in appGood.Keys.Union(appTip.Keys).OrderBy(x => x))
            {
                int g; string t;
                appGood.TryGetValue(num, out g);
                appTip.TryGetValue(num, out t);
                l.Add("Z" + num + " " + g + (t != null ? " " + t : ""));
            }
            try { File.WriteAllLines(AppTipsFile, l, new UTF8Encoding(false)); } catch (IOException) { }
        }

        // the tips the app shows now (focus mode: the focus turn only)
        IEnumerable<KeyValuePair<int, string>> ShownTips()
        {
            return appTip.Where(x => SpeakAbout(x.Key)).OrderBy(x => x.Key);
        }

        // Right after a turn: the result vs the best lap and what was different from the best run of the turn;
        // the tip for next time from the same comparison. Quiet once the turn goes well a few times in a row.
        void AppCorner(int k, CornerCmp cmp, CornerCmp tcmp, Tip tip, bool ownTarget, float evalD)
        {
            int num = refCorners[k].Num;
            float vsBest = cmp.SegDelta, vsTarget = ownTarget ? tcmp.SegDelta : cmp.SegDelta;
            bool record = vsTarget < -0.02f && vsBest < 0.03f, good = vsTarget <= 0.05f;
            int streak;
            appGood.TryGetValue(num, out streak);
            appGood[num] = streak = good ? streak + 1 : 0;
            string oldTip;
            appTip.TryGetValue(num, out oldTip);
            if (good) appTip.Remove(num);
            else if (tip != null && tip.Cue != null) appTip[num] = tip.Cue;
            string newTip;
            appTip.TryGetValue(num, out newTip);
            if (newTip != oldTip) appLog.Add(string.Format(Inv, "{0:0}\ttip\t{1}\t{2}", evalD, num, GameApp.Ascii(newTip ?? "")));
            SaveAppTips();
            if (!SpeakAbout(num)) { WriteLive(); return; }
            if (streak >= AppQuietAfter && !record && vsBest < 0.05f) { WriteLive(); return; }   // goes well: quiet

            List<string> diff = new List<string>();
            if (!float.IsNaN(tcmp.BrakeDiff) && Math.Abs(tcmp.BrakeDiff) >= 5)
                diff.Add(string.Format(Inv, L.T("hamowanie {0:0} m {1}", "braking {0:0} m {1}"), Math.Abs(tcmp.BrakeDiff), tcmp.BrakeDiff > 0 ? L.T("później", "later") : L.T("wcześniej", "earlier")));
            if (Math.Abs(tcmp.MinSpdDiff) >= 2)
                diff.Add(string.Format(Inv, L.T("szczyt {0:+0;-0} km/h", "apex {0:+0;-0} km/h"), tcmp.MinSpdDiff));
            if (!float.IsNaN(tcmp.ThrDiff) && Math.Abs(tcmp.ThrDiff) >= 10)
                diff.Add(string.Format(Inv, L.T("gaz {0:0} m {1}", "throttle {0:0} m {1}"), Math.Abs(tcmp.ThrDiff), tcmp.ThrDiff > 0 ? L.T("później", "later") : L.T("wcześniej", "earlier")));
            // what was different from the best run of the turn (a few metres either way is noise)
            string detail = diff.Count > 0 ? string.Join(", ", diff.ToArray()) : L.T("jak najlepszy przejazd", "like your best run");
            string kind = record ? "record" : vsBest <= -0.03f ? "good" : vsBest >= 0.05f ? "bad" : "even";
            string head = L.T("Z", "T") + num + "  " + Corners.Sec2(vsBest) + " s" + (record ? L.T("  REKORD", "  RECORD") : "");
            AppResult(num, kind, head, detail, evalD);
        }

        // Off track in a turn: no numbers, just that, and what to do next time (set in OnOffTrack).
        void AppOff(int k, float evalD)
        {
            int num = refCorners[k].Num;
            appGood[num] = 0;
            SaveAppTips();
            if (!SpeakAbout(num)) { WriteLive(); return; }
            float m;
            string detail = marginNote.TryGetValue(k, out m) ? string.Format(Inv, L.T("licznik {0:0} m wcześniej", "countdown {0:0} m earlier"), m) : "";
            AppResult(num, "off", L.T("Z", "T") + num + L.T("  wyjazd", "  off track"), detail, evalD);
        }

        void AppTipForOff(int num, bool entry)
        {
            string tip = entry ? L.T("hamuj wcześniej", "brake earlier") : L.T("spokojniej z gazem na wyjściu", "calmer throttle on the exit");
            string old;
            if (appTip.TryGetValue(num, out old) && old == tip) return;
            appTip[num] = tip;
            float d = live != null ? live.FilledDistance : 0;
            appLog.Add(string.Format(Inv, "{0:0}\ttip\t{1}\t{2}", d, num, GameApp.Ascii(tip)));
            SaveAppTips();
        }

        void AppResult(int num, string kind, string head, string detail, float evalD)
        {
            appSeq++;
            appResult = string.Format(Inv, "{0} {1} {2} {3}|{4}", appSeq, num, kind, GameApp.Ascii(head), GameApp.Ascii(detail));
            appLog.Add(string.Format(Inv, "{0:0}\tresult\t{1}\t{2}\t{3}|{4}", evalD, num, kind, GameApp.Ascii(head), GameApp.Ascii(detail)));
            WriteLive();
        }

        // At the start of a lap: the tips the app shows from the line on (the track map starts from them).
        void AppLapStart()
        {
            appLog.Clear();
            foreach (KeyValuePair<int, string> t in ShownTips())
                appLog.Add(string.Format(Inv, "0\ttip\t{0}\t{1}", t.Key, GameApp.Ascii(t.Value)));
            WriteLive();
        }

        // <combo>.live.txt in the game: "seq N", "result <seq> <num> <kind> <headline>|<detail>", "tip <num> <text>"
        void WriteLive()
        {
            if (!o.GameApp || comboDir == null) return;
            string f = GameApp.DataFile(Path.GetFileName(comboDir) + ".live");
            if (f == null) return;
            List<string> l = new List<string> { "accoach_live 1", "seq " + appSeq };
            if (appResult != null) l.Add("result " + appResult);
            foreach (KeyValuePair<int, string> t in ShownTips()) l.Add("tip " + t.Key + " " + GameApp.Ascii(t.Value));
            try { GameApp.Write(f, l); } catch (Exception) { }
        }

        // The app's tips and results of a finished lap, for the track map: <combo>\aplikacja_log.txt, tab-separated
        // lap file, metres, "tip"/"result", turn, text (results: kind, then headline|detail)
        void SaveAppLog(string lapName)
        {
            if (appLog.Count == 0) return;
            try { File.AppendAllLines(Path.Combine(comboDir, GameApp.LogFile), appLog.Select(x => lapName + "\t" + x), new UTF8Encoding(false)); }
            catch (IOException) { }
        }
    }
}
