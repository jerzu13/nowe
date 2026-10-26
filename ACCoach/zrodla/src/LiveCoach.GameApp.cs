using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ACCoach
{
    // Data for the in-game app: the braking points and the pedals of the ideal lap, metre by metre.
    public partial class LiveCoach
    {
        const float ReleaseShowM = 25;   // after a braking zone the brake bar stays green this long: "let go"

        // Rewritten (in the game and in <combo>\aplikacja_w_grze.txt) whenever the best lap, a corner's best run, a margin or the reaction time changes.
        void WriteGameData()
        {
            if (refLap == null || refCorners == null || targetTrace == null || length <= 0 || si == null) return;
            Trace rt = refLap.Trace;
            int n = rt.N;
            List<string> l = new List<string>();
            l.Add("# AC Coach – ideal lap for the in-game app: braking points, pedals and best-lap time along the track");
            l.Add("accoach 1");
            l.Add("car " + si.Car);
            l.Add("track " + si.Track);
            l.Add("config " + si.Config);
            l.Add(string.Format(Inv, "length {0:0.0}", length));
            l.Add("best_ms " + refLap.LapMs);
            l.Add(string.Format(Inv, "lead {0:0.00}", beepLead));
            // corner <num> <brake m> <apex m> <speed at the brake point> <apex speed> <margin m> <lead s>
            for (int k = 0; k < targetCorners.Count; k++)
            {
                Corner t = targetCorners[k];
                if (!t.Braked) continue;
                l.Add(string.Format(Inv, "corner {0} {1:0.0} {2:0.0} {3:0.0} {4:0.0} {5:0} {6:0.00}",
                    t.Num, t.BrakeD, t.ApexD, targetTrace[k].SpeedAt(t.BrakeD), t.MinSpeed, MarginFor(t.Num), LeadFor(t.Num)));
            }

            // each stretch of track from the best run of its corner, time from the best lap
            float[] spd = new float[n], gas = new float[n], brk = new float[n];
            for (int i = 0; i < n; i++)
            {
                Trace tt = targetTrace[OwnerCorner(i * Trace.Step)];
                spd[i] = tt.Spd[i]; gas[i] = tt.Gas[i]; brk[i] = tt.Brk[i];
            }
            float[] ph = BrakePhase(brk);
            l.Add(string.Format(Inv, "grid {0:0.0} {1}", Trace.Step, n));
            for (int i = 0; i < n; i++)
                l.Add(string.Format(Inv, "{0:0.000} {1:0} {2:0.00} {3:0.00} {4:0.00}", rt.T[i], spd[i], gas[i], brk[i], ph[i]));
            // a copy next to the laps: the track map of the analysis replays what the app showed
            try { GameApp.Write(Path.Combine(comboDir, GameApp.ComboCopy), l); } catch (Exception) { }
            string f = o.GameApp ? GameApp.DataFile(Path.GetFileName(comboDir)) : null;
            if (f == null) return;
            try { GameApp.Write(f, l); }
            catch (Exception e) { Console.WriteLine(L.T("Aplikacja w grze: nie udało się zapisać danych (", "In-game app: could not write its data (") + e.Message + ")"); }
        }

        // Colour of the brake bar along the track: 1 = brake hard (up to the peak of the zone), then the share of the
        // peak still pressed while you ease off (trail braking), 0 = let go, -1 = no braking here.
        static float[] BrakePhase(float[] brk)
        {
            int n = brk.Length, release = (int)(ReleaseShowM / Trace.Step);
            float[] ph = new float[n];
            for (int i = 0; i < n; i++) ph[i] = -1;
            int s = 0;
            while (s < n)
            {
                if (brk[s] <= 0.05f) { s++; continue; }
                int e = s, peak = s;
                while (e < n && brk[e] > 0.05f) { if (brk[e] > brk[peak]) peak = e; e++; }
                for (int i = s; i < e; i++) ph[i] = i <= peak ? 1 : brk[i] / brk[peak];
                for (int i = e; i < Math.Min(n, e + release); i++) ph[i] = 0;
                s = e;
            }
            return ph;
        }
    }
}
