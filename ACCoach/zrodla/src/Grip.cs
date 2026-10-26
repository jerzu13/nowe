using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ACCoach
{
    // Something the tyres did that costs time: a wheel locking under braking, wheelspin on the exit,
    // the rear (oversteer) or the front (understeer) sliding more than the other axle, traction control cutting in.
    public class WheelEvent
    {
        public string Kind;      // lockF, lockR, spin, over, under, tc
        public float D, Dur;     // where it started (metres), how long it lasted (s)
        public float X, Z;
    }

    public static class Grip
    {
        static CultureInfo Pl { get { return L.Culture; } }

        // most important first: a locked wheel flat-spots the tyre and runs wide, wheelspin kills the exit
        public static readonly string[] Kinds = { "lockF", "lockR", "spin", "over", "under", "tc" };

        public static string Spoken(string kind)
        {
            switch (kind)
            {
                case "lockF": return L.T("blokujesz przód", "front wheels locking");
                case "lockR": return L.T("blokujesz tył", "rear wheels locking");
                case "spin": return L.T("buksujesz na wyjściu", "wheelspin on exit");
                case "over": return L.T("tył ucieka", "rear stepping out");
                case "under": return L.T("przód ucieka", "front washing out");
                default: return L.T("kontrola trakcji, łagodniej gaz", "traction control, gentler throttle");
            }
        }

        public static string Name(string kind)
        {
            switch (kind)
            {
                case "lockF": return L.T("blokada przodu", "front lock-up");
                case "lockR": return L.T("blokada tyłu", "rear lock-up");
                case "spin": return L.T("buksowanie", "wheelspin");
                case "over": return L.T("nadsterowność", "oversteer");
                case "under": return L.T("podsterowność", "understeer");
                default: return L.T("praca TC", "TC cutting in");
            }
        }

        public static string Advice(string kind)
        {
            switch (kind)
            {
                case "lockF": return L.T("hamuj mocno od razu, ale potem stopniowo zdejmuj hamulec, im bardziej skręcasz", "brake hard straight away, then ease off the brake the more you turn");
                case "lockR": return L.T("mniej hamulca przy skręcie (i ewentualnie balans hamulców do przodu)", "less brake while turning (and possibly brake bias forward)");
                case "spin": return L.T("gaz dodawaj płynniej, pełny dopiero gdy koła się prostują", "add throttle more smoothly, full only once the wheel unwinds");
                case "over": return L.T("spokojniej z gazem na wyjściu i mniej skrętu w tej fazie", "calmer throttle on the exit and less steering in that phase");
                case "under": return L.T("wolniej na wejściu albo później skręcaj – przód nie trzyma przy tej prędkości", "slower entry or turn in later – the front does not grip at that speed");
                default: return L.T("łagodniej z gazem, TC zabiera moc", "gentler throttle, TC is cutting power");
            }
        }

        public static bool Has(List<Sample> s)
        {
            return s.Count > 0 && s.Exists(x => x.Ext != null && x.Ext[Wheels.Load] > 1);
        }

        // slip ratio and slip angle are filled by AC 1.14+ only; older versions (and some setups) leave them at 0
        public static bool HasSlip(List<Sample> s)
        {
            return s.Exists(x => x.Ext != null && (Math.Abs(x.Ext[Wheels.SlipRatio]) > 1e-4f || Math.Abs(x.Ext[Wheels.SlipAngle]) > 1e-4f));
        }

        static float Avg2(float[] e, int i, int a, int b) { return (e[i + a] + e[i + b]) / 2; }
        static float AbsAvg2(float[] e, int i, int a, int b) { return (Math.Abs(e[i + a]) + Math.Abs(e[i + b])) / 2; }

        // Events in samples [from, to); d(i) gives the distance of sample i.
        public static List<WheelEvent> Detect(List<Sample> s, int from, int to, Func<int, float> d)
        {
            List<WheelEvent> ev = new List<WheelEvent>();
            from = Math.Max(0, from); to = Math.Min(s.Count, to);
            if (to - from < 10) return ev;
            // slip angles: the sim's units vary between versions, so "a lot" is relative to this stretch of track
            List<float> corner = new List<float>();
            for (int i = from; i < to; i++)
                if (s[i].Ext != null && Math.Abs(s[i].LatG) > 1.0f) corner.Add(Math.Max(AbsAvg2(s[i].Ext, Wheels.SlipAngle, 0, 1), AbsAvg2(s[i].Ext, Wheels.SlipAngle, 2, 3)));
            corner.Sort();
            float aMin = corner.Count >= 20 ? 0.6f * corner[corner.Count / 2] : float.MaxValue;

            foreach (string kind in Kinds)
            {
                float minDur = kind == "lockF" || kind == "lockR" ? 0.08f : kind == "under" || kind == "tc" ? 0.3f : 0.15f;
                int start = -1;
                for (int i = from; i <= to; i++)
                {
                    bool on = i < to && Is(kind, s[i], aMin);
                    if (on && start < 0) start = i;
                    if (!on && start >= 0)
                    {
                        float dur = (s[i - 1].T - s[start].T) / 1000f;
                        if (dur >= minDur) ev.Add(new WheelEvent { Kind = kind, D = d(start), Dur = dur, X = s[start].X, Z = s[start].Z });
                        start = -1;
                    }
                }
            }
            return ev;
        }

        static bool Is(string kind, Sample x, float aMin)
        {
            float[] e = x.Ext;
            if (e == null || x.Speed < 30) return false;
            switch (kind)
            {
                case "lockF": return x.Brake > 0.3f && Math.Min(e[Wheels.SlipRatio], e[Wheels.SlipRatio + 1]) < -0.25f;
                case "lockR": return x.Brake > 0.3f && Math.Min(e[Wheels.SlipRatio + 2], e[Wheels.SlipRatio + 3]) < -0.25f;
                case "spin": return x.Gas > 0.5f && Math.Max(e[Wheels.SlipRatio + 2], e[Wheels.SlipRatio + 3]) > 0.2f;
                case "over":
                    {
                        float f = AbsAvg2(e, Wheels.SlipAngle, 0, 1), r = AbsAvg2(e, Wheels.SlipAngle, 2, 3);
                        return x.Speed > 50 && Math.Abs(x.LatG) > 0.6f && r > aMin && r > 1.4f * f;
                    }
                case "under":
                    {
                        float f = AbsAvg2(e, Wheels.SlipAngle, 0, 1), r = AbsAvg2(e, Wheels.SlipAngle, 2, 3);
                        return x.Speed > 50 && Math.Abs(x.LatG) > 0.6f && f > 1.5f * aMin && f > 1.7f * r;
                    }
                default: return x.Gas > 0.3f && e[Wheels.Tc] > 0.5f;
            }
        }

        // a whole saved lap
        public static List<WheelEvent> LapEvents(Lap l, float length)
        {
            int n = l.Samples.Count;
            return Detect(l.Samples, 0, n, i => Lap.UnwrapPos(l.Samples[i].Pos, i, n) * length);
        }

        // the corner a spot belongs to: from 50 m before its braking to 50 m before the next one's
        public static int CornerOf(List<Corner> corners, float d)
        {
            int k = corners.Count - 1;
            for (int j = 0; j < corners.Count; j++) if (d >= corners[j].OnsetD - 50) k = j;
            return k;
        }

        // ---------- tyres ----------

        public class TyreStats
        {
            public float[] Core = new float[4], Press = new float[4], Brake = new float[4];
            public float CoreAvg { get { return Core.Average(); } }
        }

        // average over the samples at speed (pit stops and standstill would drag the numbers down)
        public static TyreStats Tyres(List<Sample> s, int from = 0, int to = int.MaxValue)
        {
            to = Math.Min(to, s.Count);
            TyreStats t = new TyreStats();
            int n = 0;
            for (int i = Math.Max(0, from); i < to; i++)
            {
                float[] e = s[i].Ext;
                if (e == null || s[i].Speed < 40 || e[Wheels.Core] <= 0) continue;
                for (int w = 0; w < 4; w++) { t.Core[w] += e[Wheels.Core + w]; t.Press[w] += e[Wheels.Pressure + w]; t.Brake[w] += e[Wheels.BrakeTemp + w]; }
                n++;
            }
            if (n == 0) return null;
            for (int w = 0; w < 4; w++) { t.Core[w] /= n; t.Press[w] /= n; t.Brake[w] /= n; }
            return t;
        }

        public static string Text(TyreStats t)
        {
            Func<float[], string, string> f = (a, fmt) => string.Join("/", a.Select(v => v.ToString(fmt, Pl)));
            return string.Format(L.T("opony (LP/PP/LT/PT) rdzeń {0} °C, ciśnienie {1} psi, hamulce {2} °C", "tyres (FL/FR/RL/RR) core {0} °C, pressure {1} psi, brakes {2} °C"), f(t.Core, "0"), f(t.Press, "0.0"), f(t.Brake, "0"));
        }

        // colder than on the best lap: the grip is not there yet (or no longer, after a spin)
        public static bool Cold(TyreStats lap, TyreStats best)
        {
            if (lap == null) return false;
            float refC = best != null ? best.CoreAvg : 70f;
            return lap.CoreAvg < refC - Math.Max(8f, 0.12f * refC);
        }
    }
}
