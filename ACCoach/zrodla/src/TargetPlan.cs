using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ACCoach
{
    // "Target lap time" mode: where the missing time is on the track, whether the car (as the driver has
    // driven it so far) can do it, and intermediate goals on the way. The plan is a time per metre along the
    // reference lap: the reference minus the share of what the limit model says is possible at each spot.
    public class TargetPlan
    {
        public int TargetMs, RefMs, IdealMs;
        public int LimitMs;            // the model at the driver's own best grip (f = 1)
        public float GripNeeded;       // grip/braking scale the target needs; 0 = already done, NaN = beyond the model
        public float[] PlanT;          // plan time [s] at each 2 m grid point of the reference trace
        public float[] PlanSpd;        // plan speed [km/h]
        public float[] RefSpd;
        public bool Reached { get { return RefMs <= TargetMs; } }

        const float MaxGrip = 1.3f;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static CultureInfo Pl { get { return L.Culture; } }

        public static TargetPlan Make(LimitModel m, LimitModel.Line line, Trace refTrace, int refMs, int idealMs, int targetMs)
        {
            TargetPlan p = new TargetPlan { TargetMs = targetMs, RefMs = refMs, IdealMs = idealMs, refT = refTrace.T };
            int n = refTrace.N;
            p.RefSpd = refTrace.Spd;
            // model and reference integrated the same way, scaled to the real lap time
            double grid = 0;
            for (int i = 0; i < n; i++) grid += line.DS[i] / Math.Max(refTrace.Spd[i] / 3.6, 1);
            float c = (float)(refMs / 1000.0 / grid);
            float[] dtRef = new float[n];
            for (int i = 0; i < n; i++) dtRef[i] = c * line.DS[i] / Math.Max(refTrace.Spd[i] / 3.6f, 1f);

            float[] v1;
            float[] gain1 = Gains(m, line, dtRef, c, 1f, out v1);
            float g1 = gain1.Sum();
            p.LimitMs = refMs - (int)Math.Round(g1 * 1000);
            float need = (refMs - targetMs) / 1000f;

            float f = 1f, alpha;
            float[] gain = gain1, v = v1;
            if (need <= 0) { p.GripNeeded = 0; alpha = 0; }
            else if (need <= g1) { p.GripNeeded = 1f; alpha = need / g1; }
            else
            {
                float lo = 1f, hi = MaxGrip;
                float[] vh;
                float[] gh = Gains(m, line, dtRef, c, hi, out vh);
                if (gh.Sum() < need) { p.GripNeeded = float.NaN; f = hi; gain = gh; v = vh; alpha = 1; }
                else
                {
                    for (int it = 0; it < 18; it++)
                    {
                        float mid = (lo + hi) / 2;
                        float[] vm;
                        if (Gains(m, line, dtRef, c, mid, out vm).Sum() >= need) hi = mid; else lo = mid;
                    }
                    f = hi;
                    gain = Gains(m, line, dtRef, c, f, out v);
                    p.GripNeeded = f;
                    alpha = Math.Min(1f, need / gain.Sum());
                }
            }

            p.PlanT = new float[n];
            p.PlanSpd = new float[n];
            double t = refTrace.T[0];
            for (int i = 0; i < n; i++)
            {
                p.PlanT[i] = (float)t;
                float realDt = i + 1 < n ? refTrace.T[i + 1] - refTrace.T[i] : dtRef[i];
                t += realDt - alpha * gain[i];
                float vs = v[i] * 3.6f;
                p.PlanSpd[i] = gain[i] > 0 ? refTrace.Spd[i] + alpha * Math.Max(0, vs - refTrace.Spd[i]) : refTrace.Spd[i];
            }
            return p;
        }

        // time the model gains at each point over the reference (only where it is faster: elsewhere the
        // reference stands, the model is not trusted to ask for slower driving)
        static float[] Gains(LimitModel m, LimitModel.Line line, float[] dtRef, float c, float f, out float[] v)
        {
            v = m.Simulate(line, f);
            float[] g = new float[line.N];
            for (int i = 0; i < line.N; i++) g[i] = Math.Max(0, dtRef[i] - c * line.DS[i] / Math.Max(v[i], 1f));
            return g;
        }

        public float TimeAt(float d)
        {
            int i = Math.Max(0, Math.Min(PlanT.Length - 1, (int)Math.Round(d / Trace.Step)));
            return PlanT[i];
        }

        public float MinSpeed(float from, float to)
        {
            int a = Math.Max(0, (int)(from / Trace.Step)), b = Math.Min(PlanSpd.Length - 1, (int)(to / Trace.Step));
            float s = float.MaxValue;
            for (int i = a; i <= b; i++) s = Math.Min(s, PlanSpd[i]);
            return s;
        }

        public float RefMinSpeed(float from, float to)
        {
            int a = Math.Max(0, (int)(from / Trace.Step)), b = Math.Min(RefSpd.Length - 1, (int)(to / Trace.Step));
            float s = float.MaxValue;
            for (int i = a; i <= b; i++) s = Math.Min(s, RefSpd[i]);
            return s;
        }

        // ---------- what to tell the driver ----------

        public string Verdict()
        {
            if (Reached) return L.T("osiągnięty – Twój wzorzec jest już szybszy.", "reached – your best lap is already faster.");
            if (IdealMs > 0 && TargetMs >= IdealMs) return L.T("realny – masz go już w swoich najlepszych przejazdach zakrętów, wystarczy je połączyć.", "realistic – you already have it in your best runs through the corners, you just need to put them together.");
            if (GripNeeded <= 1f) return L.T("realny – w granicach przyczepności i hamowania, które już osiągasz.", "realistic – within the grip and braking you already reach.");
            if (GripNeeded <= 1.02f) return L.T("realny, ale na samej granicy przyczepności i hamowania, które już osiągasz.", "realistic, but right at the limit of the grip and braking you already reach.");
            if (float.IsNaN(GripNeeded))
                return L.T("poza zasięgiem modelu – potrzeba ponad 30% więcej przyczepności niż Twoje maksimum. Ustaw cel pośredni.", "beyond the model – it needs over 30% more grip than your maximum. Set an intermediate goal.");
            int pct = (int)Math.Round((GripNeeded - 1) * 100);
            if (GripNeeded <= 1.08f) return string.Format(L.T("ambitny – wymaga ok. {0}% więcej przyczepności i siły hamowania niż Twoje dotychczasowe maksimum.", "ambitious – it needs about {0}% more grip and braking than your maximum so far."), pct);
            if (GripNeeded <= 1.15f) return string.Format(L.T("bardzo ambitny – ok. {0}% więcej przyczepności i hamowania niż Twoje maksimum; to już kwestia techniki, a może i ustawień auta.", "very ambitious – about {0}% more grip and braking than your maximum; that is down to technique, and maybe the car setup."), pct);
            return string.Format(L.T("bardzo daleko – ok. {0}% ponad Twoje maksimum przyczepności. Najpierw cele pośrednie.", "very far – about {0}% above your maximum grip. Intermediate goals first."), pct);
        }

        // Goals on the way, slowest first: the ideal lap, the model at today's grip, then whole seconds.
        public List<int> Steps()
        {
            List<int> s = new List<int>();
            if (Reached) return s;
            Func<int, int> r100 = x => (int)Math.Round(x / 100.0) * 100;
            List<int> named = new List<int>();   // the ideal lap and the model at today's grip come first
            if (IdealMs > 0) named.Add(r100(IdealMs));
            named.Add(r100(LimitMs));
            List<int> all = new List<int>(named);
            for (int k = 1; k <= 30; k++)
            {
                int x = r100(RefMs - k * 1000);
                if (named.All(y => Math.Abs(y - x) >= 400)) all.Add(x);
            }
            foreach (int x in all.Distinct().OrderByDescending(x => x))
                if (x > TargetMs + 300 && x < RefMs - 150) s.Add(x);
            s.Add(TargetMs);
            return s;
        }

        public class CornerPlan
        {
            public Corner C;
            public float Gain;          // seconds the plan takes off this corner
            public float RefMin, PlanMin;
        }

        public List<CornerPlan> PerCorner(List<Corner> corners)
        {
            List<CornerPlan> l = new List<CornerPlan>();
            for (int k = 0; k < corners.Count; k++)
            {
                Corner c = corners[k];
                float from = k == 0 ? 0 : corners[k - 1].EndD, to = c.EndD;
                CornerPlan cp = new CornerPlan { C = c };
                int a = Math.Max(0, (int)(from / Trace.Step)), b = Math.Min(PlanT.Length - 1, (int)(to / Trace.Step));
                cp.Gain = (RefT(a, b)) - (PlanT[b] - PlanT[a]);
                cp.RefMin = RefMinSpeed(c.StartD, c.EndD);
                cp.PlanMin = MinSpeed(c.StartD, c.EndD);
                l.Add(cp);
            }
            return l;
        }

        float[] refT;
        float RefT(int a, int b) { return refT[b] - refT[a]; }

        // The report / console block for this plan (the final target).
        public List<string> Describe(LimitModel m, List<Corner> corners)
        {
            List<string> r = new List<string>();
            r.Add(string.Format(L.T("CEL CZASU {0}: {1}", "LAP TIME TARGET {0}: {1}"), Lap.FormatTime(TargetMs), Verdict()));
            r.Add(string.Format(L.T("  wzorzec {0}{1} | model przy Twojej dotychczasowej przyczepności {2}", "  best lap {0}{1} | model at the grip you have used so far {2}"),
                Lap.FormatTime(RefMs), IdealMs > 0 && IdealMs < RefMs ? L.T(" | idealne (Twoje najlepsze zakręty) ", " | ideal (your best turns) ") + Lap.FormatTime(IdealMs) : "",
                Lap.FormatTime(LimitMs)));
            if (Reached) return r;
            List<int> steps = Steps();
            if (steps.Count > 1) r.Add(L.T("  cele pośrednie: ", "  intermediate goals: ") + string.Join(" → ", steps.Select(x => Lap.FormatTime(x))));
            List<CornerPlan> cps = PerCorner(corners).Where(c => c.Gain >= 0.05f).OrderByDescending(c => c.Gain).ToList();
            if (cps.Count > 0)
            {
                r.Add(L.T("  gdzie szukać czasu (plan na ten cel):", "  where to find the time (plan for this target):"));
                foreach (CornerPlan c in cps)
                    r.Add(string.Format(L.T("    Z{0,-2} {1,6} s   szczyt {2:0} → {3:0} km/h", "    T{0,-2} {1,6} s   apex {2:0} → {3:0} km/h"), c.C.Num, "-" + Sec(c.Gain), c.RefMin, c.PlanMin));
            }
            r.Add(L.T("  (model z Twoich okrążeń: ", "  (model from your laps: ") + m.Describe() + L.T("; im szybciej jeździsz, tym dokładniej)", "; the faster you drive, the more accurate it gets)"));
            return r;
        }

        public static string Sec(float s) { return s.ToString("0.00", Pl); }

        // "1:35", "1:35.5", "1:35,500", "1:35:500" (as often typed), "95.2" -> ms; 0 when empty or not a time
        public static int ParseTime(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Trim().Replace(',', '.');
            string[] p = s.Split(':');
            try
            {
                double sec;
                if (p.Length == 1) sec = double.Parse(p[0], Inv);
                else if (p.Length == 2) sec = int.Parse(p[0], Inv) * 60 + double.Parse(p[1], Inv);
                else if (p.Length == 3) sec = int.Parse(p[0], Inv) * 60 + int.Parse(p[1], Inv) + double.Parse("0." + p[2], Inv);
                else return 0;
                return sec > 10 && sec < 3600 ? (int)Math.Round(sec * 1000) : 0;
            }
            catch (FormatException) { return 0; }
        }

        // the target of a car + track: <combo>\cel.txt
        public static int LoadTarget(string comboDir)
        {
            try
            {
                string f = Path.Combine(comboDir, "cel.txt");
                if (File.Exists(f))
                    foreach (string line in File.ReadAllLines(f))
                        if (line.Trim().Length > 0 && !line.TrimStart().StartsWith("#")) return ParseTime(line);
            }
            catch (Exception) { }
            return 0;
        }

        public static void SaveTarget(string comboDir, int ms)
        {
            try
            {
                string f = Path.Combine(comboDir, "cel.txt");
                if (ms <= 0) { if (File.Exists(f)) File.Delete(f); return; }
                File.WriteAllLines(f, new[] { L.T("# cel czasu okrążenia dla tego auta i toru (ustawiany w oknie na żywo)", "# lap time target for this car and track (set in the AC Coach window)"), Lap.FormatTime(ms) });
            }
            catch (Exception) { }
        }
    }
}
