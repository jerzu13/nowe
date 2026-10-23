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
    // Braking beeps: the countdown, the reaction-time lead and the beep log.
    public partial class LiveCoach
    {
        // One line per braked corner: when the final beep played vs when the brake went on, where it went on
        // vs the point the beep aimed at, and what came of it.
        void LogBeep(int k, CornerCmp cmp)
        {
            Corner c = targetCorners[k];
            float target = beepPoint[k] > 0 ? beepPoint[k] : float.NaN;
            float miss = brakeD[k] - target;
            if (!float.IsNaN(reaction[k]) || !float.IsNaN(miss))
                Console.WriteLine(string.Format(Inv, L.T("      piknięcie -> hamulec: {0} | punkt hamowania: {1} | siła {2:0}% (cel {3:0}%){4}", "      beep -> brake: {0} | braking point: {1} | force {2:0}% (target {3:0}%){4}"),
                    float.IsNaN(reaction[k]) ? "–" : reaction[k].ToString("+0.00;-0.00", Inv) + " s",
                    float.IsNaN(miss) ? "–" : miss.ToString("+0;-0;0", Inv) + " m", cmp.Lap.BrakeForce * 100, c.BrakeForce * 100,
                    cornerTyresOut[k] >= 3 ? L.T(", WYJAZD POZA TOR", ", OFF TRACK") : ""));
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
                Console.WriteLine(string.Format(Inv, L.T("Ostatnie piknięcie {0:0.00} s przed punktem hamowania ({1})", "Last beep {0:0.00} s before the braking point ({1})"), beepLead,
                    !float.IsNaN(o.BeepLead) ? L.T("ustawione ręcznie", "set by you") : L.T("dopasowane do Twojej reakcji", "matched to your reaction")));
            foreach (KeyValuePair<int, float> kv in cornerLead.OrderBy(x => x.Key))
            {
                float prev;
                bool changed = !oldCorner.TryGetValue(kv.Key, out prev) || Math.Abs(prev - kv.Value) >= 0.02f;
                if ((announce || changed) && Math.Abs(kv.Value - beepLead) >= 0.03f)
                    Console.WriteLine(string.Format(Inv, L.T("   Z{0}: {1:0.00} s (tu reagujesz {2})", "   T{0}: {1:0.00} s (you react {2} here)"), kv.Key, kv.Value, kv.Value > beepLead ? L.T("wolniej", "slower") : L.T("szybciej", "faster")));
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
                        Console.WriteLine(string.Format(Inv, L.T("  Z{0}: {1:0.0} m za blisko wewnętrznej przed zakrętem", "  T{0}: {1:0.0} m too close to the inside before the turn"), c.Num, off));
                        if (SpeakAbout(c.Num)) Enqueue(L.T("Szerzej", "Wider"), true);
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
                        if (reaction[k] > LateS) { Console.WriteLine(string.Format(Inv, L.T("  [PÓŹNO] Z{0}: hamulec {1:0.00} s po piknięciu", "  [LATE] T{0}: brake {1:0.00} s after the beep"), t.Num, reaction[k])); if (SpeakAbout(t.Num)) Enqueue(L.T("Późno", "Late"), true); }
                    }
                    else if (beepStage[k] < BeepAt.Length && beepPoint[k] > 0)
                    {
                        // braked before the final beep: stop the countdown so it does not beep mid-braking
                        float early = (beepPoint[k] - d) / v - lead;
                        reaction[k] = -early;
                        beepStage[k] = BeepAt.Length;
                        if (early > EarlyS) { Console.WriteLine(string.Format(Inv, L.T("  [WCZEŚNIE] Z{0}: hamulec {1:0.00} s przed piknięciem", "  [EARLY] T{0}: brake {1:0.00} s before the beep"), t.Num, early)); if (SpeakAbout(t.Num)) Enqueue(L.T("Wcześnie", "Early"), true); }
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
                if (!final) Console.WriteLine(string.Format(Inv, L.T("  [PIK {0}/3] Z{1}", "  [BEEP {0}/3] T{1}"), stage, t.Num));
                if (final) Console.WriteLine(string.Format(Inv, L.T("  [HAMUJ] Z{0} @{1:0} m {2} ({3:0}%)", "  [BRAKE] T{0} @{1:0} m {2} ({3:0}%)"), t.Num, beepPoint[k], t.ForceName, t.BrakeForce * 100));
            }
        }

        static string LineText(float off)
        {
            if (float.IsNaN(off)) return "–";
            if (Math.Abs(off) < 0.5f) return "ok";
            return string.Format(Inv, "{0:0.0} m {1}", Math.Abs(off), off < 0 ? L.T("szerzej", "wider") : L.T("ciaśniej", "tighter"));
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
