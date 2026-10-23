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
    // Focus mode: voice tips about one turn at a time until it is mastered.
    public partial class LiveCoach
    {
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
                SetFocus(o.FocusCorner, L.T("wybrany ręcznie", "picked by you"), announce);
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
                    why = string.Format(Inv, L.T("średnio {0:+0.00;-0.00} s, wyjazdy {1}/{2}, Twój najlepszy przejazd szybszy o {3:0.00} s", "average {0:+0.00;-0.00} s, off-tracks {1}/{2}, your best run {3:0.00} s faster"), loss, offs, attempts, potential);
                }
            }
            if (best >= 0) SetFocus(refCorners[best].Num, why, announce);
            else if (focusDone.Count >= n)
                StFocus = L.T("wszystkie zakręty opanowane", "all turns mastered");
            else if (StFocus != L.T("wybiorę po następnym okrążeniu", "picking after the next lap"))
            {
                StFocus = L.T("wybiorę po następnym okrążeniu", "picking after the next lap");
                Console.WriteLine(L.T("FOKUS: za mało okrążeń do porównania – wybiorę zakręt po następnym okrążeniu.", "FOCUS: too few laps to compare – I will pick a turn after the next lap."));
            }
        }

        void SetFocus(int num, string why, bool announce)
        {
            focusNum = num; focusStreak = 0; focusTip = null; focusReminded = false;
            StFocus = L.T("zakręt ", "turn ") + num;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(string.Format(L.T("FOKUS: zakręt {0} ({1}). Uwagi głosowe tylko o nim, piknięcia wszędzie.", "FOCUS: turn {0} ({1}). Voice tips only about it, beeps everywhere."), num, why));
            Console.ResetColor();
            if (announce) Enqueue(string.Format(L.T("Fokus: zakręt {0}", "Focus: turn {0}"), num), false);
        }

        // After the focus corner: result, the streak of good runs, and moving on once it is mastered.
        string FocusCall(int k, CornerCmp cmp, string call, Tip tip)
        {
            bool good = cmp.SegDelta <= 0.03f && !offThisLap[k];
            focusStreak = good ? focusStreak + 1 : 0;
            if (tip != null) focusTip = tip.Action;
            else if (good) focusTip = null;
            if (good) call += string.Format(L.T(", dobrze {0} z {1}", ", good {0} of {1}"), focusStreak, FocusStreakToMaster);
            Console.WriteLine(string.Format(L.T("      fokus Z{0}: {1}, seria {2}/{3}", "      focus T{0}: {1}, streak {2}/{3}"), focusNum, good ? L.T("dobrze", "good") : L.T("do poprawy", "to improve"), focusStreak, FocusStreakToMaster));
            if (focusStreak >= FocusStreakToMaster)
            {
                int done = focusNum;
                focusDone.Add(done);
                focusNum = 0;
                ChooseFocus(false);
                call += focusNum > 0 ? string.Format(L.T(". Zakręt {0} opanowany, nowy fokus: zakręt {1}", ". Turn {0} mastered, new focus: turn {1}"), done, focusNum)
                                     : string.Format(L.T(". Zakręt {0} opanowany", ". Turn {0} mastered"), done);
            }
            return call;
        }
    }
}
