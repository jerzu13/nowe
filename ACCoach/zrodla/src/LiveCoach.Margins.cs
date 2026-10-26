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
    // Safety margin: the last beep plays earlier where you went off track.
    public partial class LiveCoach
    {
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
                Console.WriteLine(L.T("Margines bezpieczeństwa (piknięcie wcześniej): ", "Safety margin (earlier beep): ") +
                    string.Join(", ", margin.OrderBy(x => x.Key).Select(x => string.Format(Inv, L.T("Z{0} {1:0} m", "T{0} {1:0} m"), x.Key, x.Value)).ToArray()));
        }

        void SaveMargins()
        {
            List<string> lines = new List<string> { "# margines bezpieczeństwa: o ile metrów wcześniej gra ostatnie piknięcie po wyjazdach z toru" };
            foreach (KeyValuePair<int, float> x in margin.Where(x => x.Value > 0).OrderBy(x => x.Key))
                lines.Add(string.Format(Inv, "Z{0} {1:0}", x.Key, x.Value));
            try { File.WriteAllLines(Path.Combine(comboDir, "margines.txt"), lines); } catch (IOException) { }
            WriteGameData();
        }

        // First off-track of this lap in a corner: brake earlier there next time (more if it happened on the entry).
        void OnOffTrack(float d)
        {
            int k = OwnerCorner(d);
            if (offThisLap[k]) return;
            offThisLap[k] = true;
            Corner c = refCorners[k];
            bool entry = d < c.ApexD;
            Console.WriteLine(string.Format(Inv, L.T("  [WYJAZD] Z{0} {1} ({2:0} m {3} szczytem)", "  [OFF TRACK] T{0} {1} ({2:0} m {3} the apex)"), c.Num, entry ? L.T("na wejściu", "on entry") : L.T("na wyjściu", "on exit"), Math.Abs(d - c.ApexD), entry ? L.T("przed", "before") : L.T("za", "after")));
            if (o.Focus && c.Num == focusNum)
            {
                focusStreak = 0;
                focusTip = entry ? L.T("hamuj wcześniej", "brake earlier") : L.T("spokojniej z gazem na wyjściu", "calmer throttle on the exit");
            }
            if (o.Train && targetCorners[k].Braked)
            {
                float old = MarginFor(c.Num);
                float now = Math.Min(MarginMax, old + (entry ? MarginEntry : MarginExit));
                if (now > old)
                {
                    margin[c.Num] = now;
                    marginNote[k] = now;
                    SaveMargins();
                    Console.WriteLine(string.Format(Inv, L.T("      margines Z{0}: ostatnie piknięcie {1:0} m wcześniej", "      margin T{0}: last beep {1:0} m earlier"), c.Num, now));
                }
            }
            // the corner was already judged (off late on the exit): its call becomes "off track";
            // otherwise that call is made when the corner is judged
            if (nextEval > k && !o.Quiet && SpeakAbout(c.Num)) OffCall(k);
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
            Console.WriteLine(string.Format(Inv, L.T("      margines Z{0}: {1:0} m{2}", "      margin T{0}: {1:0} m{2}"), num, now, now <= 0 ? L.T(" – zdjęty, czysty przejazd", " – removed, clean run") : ""));
        }
    }
}
