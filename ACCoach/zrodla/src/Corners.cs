using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ACCoach
{
    public class Corner
    {
        public int Num;
        public float ApexD, MinSpeed;
        public float BrakeD = float.NaN;     // where braking starts
        public float LiftD = float.NaN;      // where the driver lifts, for corners taken without brakes
        public float ThrottleD = float.NaN;  // where full throttle is back
        public float EntrySpeed, ExitSpeed, PeakBrake, CoastS;
        public float BrakeForce;             // average brake over the first second of braking, 0..1
        // pedal and steering work (see MeasureInputs)
        public float OverlapS;               // seconds with brake and throttle pressed together on entry
        public float BrakeGasS;              // of that, seconds of throttle held during hard braking (brake > 50%)
        public float BrakeReleaseD = float.NaN;  // where the brake is finally let go before the apex
        public float SteerMax;               // largest steering lock in the corner (absolute)
        public float GasSteer = float.NaN;   // steering still applied when full throttle comes, share of SteerMax
        public float CounterSteer;           // largest steering against the corner on the exit (rear stepping out)
        public int SteerCorrections;         // steering swings back and forth on the exit
        public int GasLifts;                 // throttle backed off again on the exit
        public float ExitD, StartD, EndD;    // set on the reference lap only
        public int TurnSign;                 // +1 / -1 turn direction in world coordinates, 0 = unknown

        public bool Braked { get { return !float.IsNaN(BrakeD); } }
        // 2 = full brake, 1 = medium, 0 = light or a short stab
        public int ForceLevel { get { return BrakeForce >= 0.85f ? 2 : BrakeForce >= 0.5f ? 1 : 0; } }
        public string ForceName { get { return ForceLevel == 2 ? "mocno" : ForceLevel == 1 ? "średnio" : "lekko"; } }
        public float OnsetD { get { return Braked ? BrakeD : (float.IsNaN(LiftD) ? ApexD : LiftD); } }
    }

    public class CornerCmp
    {
        public Corner Ref, Lap;
        public float BrakeDiff = float.NaN;  // metres, + = brakes later than reference
        public float ThrDiff = float.NaN;    // metres, + = full throttle later than reference
        public float MinSpdDiff, ExitSpdDiff, CoastDiff, PeakBrakeDiff;
        public float BrakeForceDiff = float.NaN;  // + = brakes harder than reference over the first second
        public float SegDelta;               // seconds, + = slower than reference in this corner's segment
        // distance from the reference line in metres, + = towards the inside of the corner, - = wider
        public float EntryOff = float.NaN, ApexOff = float.NaN, ExitOff = float.NaN;
    }

    public class Tip
    {
        public float Score;
        public string Short, Long;
        // Short without the leading "Zakręt N, ", to append after the corner's better/worse call
        public string Action { get { int i = Short.IndexOf(", "); return i < 0 ? Short : Short.Substring(i + 2); } }
    }

    public static class Corners
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static float[] Smooth(float[] a, int n, int half)
        {
            float[] r = new float[n];
            for (int i = 0; i < n; i++)
            {
                int lo = Math.Max(0, i - half), hi = Math.Min(n - 1, i + half);
                float sum = 0;
                for (int j = lo; j <= hi; j++) sum += a[j];
                r[i] = sum / (hi - lo + 1);
            }
            return r;
        }

        // Finds corners on a complete reference lap as speed minima with at least 8 km/h of drop.
        public static List<Corner> Detect(Trace tr)
        {
            int n = tr.Filled;
            List<Corner> list = new List<Corner>();
            if (n < 50) return list;
            float[] s = Smooth(tr.Spd, n, 5);
            List<int> apexes = new List<int>();
            bool falling = false;
            float peak = s[0], dropPeak = 0, min = float.MaxValue;
            int minI = 0;
            for (int i = 1; i < n; i++)
            {
                if (!falling)
                {
                    if (s[i] > peak) peak = s[i];
                    else if (s[i] < peak - 6) { falling = true; dropPeak = peak; min = s[i]; minI = i; }
                }
                else
                {
                    if (s[i] < min) { min = s[i]; minI = i; }
                    else if (s[i] > min + 6)
                    {
                        if (dropPeak - min >= 8) apexes.Add(minI);
                        falling = false;
                        peak = s[i];
                    }
                }
            }
            // merge minima closer than 60 m (chicanes measured as one corner), keep the slower one
            List<int> merged = new List<int>();
            foreach (int a in apexes)
            {
                if (merged.Count > 0 && (a - merged[merged.Count - 1]) * Trace.Step < 60)
                {
                    if (s[a] < s[merged[merged.Count - 1]]) merged[merged.Count - 1] = a;
                }
                else merged.Add(a);
            }
            for (int k = 0; k < merged.Count; k++)
            {
                float prev = k > 0 ? merged[k - 1] * Trace.Step : 0;
                float next = k < merged.Count - 1 ? merged[k + 1] * Trace.Step : tr.Length;
                Corner c = Measure(tr, merged[k] * Trace.Step, prev, next);
                c.Num = k + 1;
                list.Add(c);
            }
            Segment(list, tr);
            return list;
        }

        // Exit and segment boundaries of every corner (list ordered along the track).
        static void Segment(List<Corner> list, Trace tr)
        {
            for (int k = 0; k < list.Count; k++)
            {
                Corner c = list[k];
                float exit = (float.IsNaN(c.ThrottleD) ? c.ApexD : c.ThrottleD) + 150;
                if (k < list.Count - 1) exit = Math.Min(exit, list[k + 1].OnsetD - 10);
                exit = Math.Min(exit, tr.Length - 2);
                exit = Math.Max(exit, c.ApexD + 20);
                if (k < list.Count - 1) exit = Math.Min(exit, Math.Max(c.ApexD + 2, list[k + 1].ApexD - 20));   // never past the next apex
                c.ExitD = exit;
                c.ExitSpeed = tr.SpeedAt(exit);
                c.StartD = k == 0 ? 0 : list[k - 1].ExitD;
                c.EndD = exit;
                c.TurnSign = HasPositions(tr) ? TurnSign(tr, c.ApexD) : 0;
            }
        }

        // Keeps corner numbers stable when the reference lap changes: the first corner layout is saved to
        // zakrety.txt ("Z<num> <apex m>") and later detections take the number of the nearest saved apex.
        public static void Renumber(List<Corner> list, string comboDir, Trace tr)
        {
            string f = System.IO.Path.Combine(comboDir, "zakrety.txt");
            List<KeyValuePair<int, float>> saved = new List<KeyValuePair<int, float>>();
            if (System.IO.File.Exists(f))
                foreach (string line in System.IO.File.ReadAllLines(f))
                {
                    string[] p = line.Trim().TrimStart('Z', 'z').Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    int num; float apex;
                    if (p.Length >= 2 && int.TryParse(p[0], out num) && float.TryParse(p[1], NumberStyles.Float, Inv, out apex))
                        saved.Add(new KeyValuePair<int, float>(num, apex));
                }
            if (saved.Count == 0)
            {
                List<string> lines = new List<string> { "# numeracja zakrętów: Z<numer> <szczyt w metrach od linii mety>" };
                foreach (Corner c in list) lines.Add(string.Format(Inv, "Z{0} {1:0}", c.Num, c.ApexD));
                System.IO.File.WriteAllLines(f, lines);
                return;
            }
            // closest pairs first, each saved number used once
            List<int> taken = new List<int>();
            var pairs = new List<Tuple<float, Corner, int>>();
            foreach (Corner c in list)
                foreach (var s in saved)
                    if (Math.Abs(s.Value - c.ApexD) <= 100) pairs.Add(Tuple.Create(Math.Abs(s.Value - c.ApexD), c, s.Key));
            List<Corner> done = new List<Corner>();
            foreach (var p in pairs.OrderBy(x => x.Item1))
            {
                if (done.Contains(p.Item2) || taken.Contains(p.Item3)) continue;
                p.Item2.Num = p.Item3; done.Add(p.Item2); taken.Add(p.Item3);
            }
            // a corner this lap shows but the saved layout does not (a kink detected only sometimes) is not coached,
            // so numbers stay in track order and mean the same corner every time
            list.RemoveAll(c => !done.Contains(c));
            // a saved corner this lap does not show (e.g. taken flat out) is still measured at its saved place,
            // so its entry, exit and any off-track there keep their own number
            foreach (var s in saved.Where(s => !taken.Contains(s.Key)))
            {
                float prev = list.Where(c => c.ApexD < s.Value).Select(c => c.ApexD).DefaultIfEmpty(0).Max();
                float next = list.Where(c => c.ApexD > s.Value).Select(c => c.ApexD).DefaultIfEmpty(tr.Length).Min();
                if (s.Value - prev < 40 || next - s.Value < 40) continue;
                Corner mc = Measure(tr, s.Value, prev, next);
                mc.Num = s.Key;
                list.Add(mc);
            }
            list.Sort((a, b) => a.ApexD.CompareTo(b.ApexD));
            Segment(list, tr);
        }

        public static bool HasPositions(Trace tr)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < tr.Filled; i += 10)
            {
                minX = Math.Min(minX, tr.X[i]); maxX = Math.Max(maxX, tr.X[i]);
                minZ = Math.Min(minZ, tr.Z[i]); maxZ = Math.Max(maxZ, tr.Z[i]);
            }
            return (maxX - minX) + (maxZ - minZ) > 50;
        }

        static void Tangent(Trace tr, int i, out float tx, out float tz)
        {
            int a = Math.Max(0, i - 3), b = Math.Min(tr.Filled - 1, i + 3);
            tx = tr.X[b] - tr.X[a]; tz = tr.Z[b] - tr.Z[a];
            float len = (float)Math.Sqrt(tx * tx + tz * tz);
            if (len > 1e-3f) { tx /= len; tz /= len; }
        }

        // Turn direction from how the reference path bends around the apex. Using the same cross-product
        // convention for the offset makes inside/outside independent of the game's axis orientation.
        static int TurnSign(Trace tr, float apexD)
        {
            float ax, az, bx, bz;
            Tangent(tr, tr.Idx(apexD - 40), out ax, out az);
            Tangent(tr, tr.Idx(apexD + 40), out bx, out bz);
            float cross = ax * bz - az * bx;
            return Math.Abs(cross) < 0.05f ? 0 : Math.Sign(cross);
        }

        // Signed distance of the lap from the reference line at distance d: + = inside of the corner.
        public static float Offset(Trace refTr, Trace lapTr, float d, int turnSign)
        {
            int i = refTr.Idx(d);
            if (turnSign == 0 || i >= lapTr.Filled || i >= refTr.Filled) return float.NaN;
            float tx, tz;
            Tangent(refTr, i, out tx, out tz);
            float vx = lapTr.X[i] - refTr.X[i], vz = lapTr.Z[i] - refTr.Z[i];
            float off = (tx * vz - tz * vx) * turnSign;
            return Math.Abs(off) > 25 ? float.NaN : off;   // way off = bad data / off track
        }

        // Measures one corner on a (possibly partial) trace around an expected apex position.
        public static Corner Measure(Trace tr, float apexGuess, float prevApexD, float nextApexD)
        {
            Corner c = new Corner();
            int last = tr.Filled - 1;
            int lo = tr.Idx(Math.Max(apexGuess - 80, prevApexD + 20));
            int hi = Math.Min(last, tr.Idx(Math.Min(apexGuess + 80, nextApexD - 20)));
            if (hi < lo) hi = lo;
            int apex = lo;
            for (int i = lo; i <= hi; i++) if (tr.Spd[i] < tr.Spd[apex]) apex = i;
            c.ApexD = apex * Trace.Step;
            c.MinSpeed = tr.Spd[apex];

            int lowLimit = tr.Idx(Math.Max(prevApexD + 20, c.ApexD - 600));
            int k = apex;
            while (k > lowLimit && tr.Brk[k] <= 0.1f) k--;
            int onset;
            if (tr.Brk[k] > 0.1f)
            {
                while (k > lowLimit && tr.Brk[k - 1] > 0.05f) k--;
                // brake released and pressed again (a stab, then the real braking or a correction near the apex):
                // keep walking back while there was real braking up to 60 m earlier, so BrakeD is where braking began
                int gap = (int)(60 / Trace.Step);
                for (bool more = true; more; )
                {
                    more = false;
                    for (int j = k - 1; j >= Math.Max(lowLimit, k - gap); j--)
                        if (tr.Brk[j] > 0.1f)
                        {
                            k = j;
                            while (k > lowLimit && tr.Brk[k - 1] > 0.05f) k--;
                            more = true;
                            break;
                        }
                }
                onset = k;
                c.BrakeD = onset * Trace.Step;
            }
            else
            {
                k = apex;
                while (k > lowLimit && tr.Gas[k] < 0.9f) k--;
                onset = k;
                if (onset < apex) c.LiftD = onset * Trace.Step;
            }

            int highLimit = Math.Min(last, tr.Idx(nextApexD - 20));
            bool wasOff = false;
            for (int i = onset; i <= highLimit; i++)
            {
                if (tr.Gas[i] < 0.9f) wasOff = true;
                else if (wasOff && tr.Brk[i] < 0.05f && i >= apex - 10) { c.ThrottleD = i * Trace.Step; break; }
            }

            for (int i = onset; i <= apex; i++) c.PeakBrake = Math.Max(c.PeakBrake, tr.Brk[i]);
            if (c.Braked)
            {
                float sum = 0; int cnt = 0;
                for (int i = onset; i <= apex && tr.T[i] - tr.T[onset] <= 1f; i++) { sum += tr.Brk[i]; cnt++; }
                c.BrakeForce = cnt > 0 ? sum / cnt : 0;
            }
            int coastEnd = float.IsNaN(c.ThrottleD) ? apex : tr.Idx(c.ThrottleD);
            for (int i = onset; i < coastEnd && i + 1 <= last; i++)
                if (tr.Gas[i] < 0.1f && tr.Brk[i] < 0.05f) c.CoastS += tr.T[i + 1] - tr.T[i];
            for (int i = Math.Max(0, onset - 50); i <= onset; i++) c.EntrySpeed = Math.Max(c.EntrySpeed, tr.Spd[i]);
            MeasureInputs(tr, c, onset, apex, highLimit);
            return c;
        }

        // Seconds with throttle > 15% while the brake is above minBrake, in runs longer than 0.3 s.
        static float TogetherS(Trace tr, int from, int to, float minBrake)
        {
            float total = 0, run = 0;
            for (int i = from; i < to && i + 1 < tr.Filled; i++)
            {
                if (tr.Brk[i] > minBrake && tr.Gas[i] > 0.15f) run += tr.T[i + 1] - tr.T[i];
                else { if (run > 0.3f) total += run; run = 0; }
            }
            return run > 0.3f ? total + run : total;
        }

        // Brake, throttle and steering through the corner: entry = brake onset .. apex, exit = apex .. 120 m after
        // full throttle (or 150 m after the apex), never past the next corner.
        static void MeasureInputs(Trace tr, Corner c, int onset, int apex, int highLimit)
        {
            int exitEnd = Math.Min(highLimit, float.IsNaN(c.ThrottleD) ? apex + (int)(150 / Trace.Step) : tr.Idx(c.ThrottleD + 120));
            if (exitEnd <= apex) exitEnd = Math.Min(highLimit, apex + 1);
            int from = Math.Max(0, onset - (int)(30 / Trace.Step));

            if (c.Braked)
            {
                int rel = -1;
                for (int i = onset; i <= Math.Min(exitEnd, apex + (int)(30 / Trace.Step)); i++) if (tr.Brk[i] > 0.05f) rel = i;
                if (rel >= 0) c.BrakeReleaseD = rel * Trace.Step;
            }
            // the exit ends where braking for the next corner starts
            int relI = float.IsNaN(c.BrakeReleaseD) ? apex : Math.Max(apex, tr.Idx(c.BrakeReleaseD));
            for (int i = relI + 1; i <= exitEnd; i++) if (tr.Brk[i] > 0.1f) { exitEnd = Math.Max(apex, i - 1); break; }

            // pedals together, counted only in stretches longer than 0.3 s (a downshift blip is shorter)
            c.OverlapS = TogetherS(tr, from, apex, 0.15f);
            c.BrakeGasS = TogetherS(tr, from, apex, 0.5f);

            // turn direction = sign of the steering around the apex
            float sum = 0;
            for (int i = Math.Max(onset, apex - 5); i <= Math.Min(exitEnd, apex + 5); i++) sum += tr.Str[i];
            int dir = Math.Sign(sum);
            for (int i = onset; i <= exitEnd; i++) c.SteerMax = Math.Max(c.SteerMax, Math.Abs(tr.Str[i]));
            if (dir == 0 || c.SteerMax < 0.02f) return;

            if (!float.IsNaN(c.ThrottleD))
                c.GasSteer = Math.Abs(tr.Str[Math.Min(exitEnd, tr.Idx(c.ThrottleD))]) / c.SteerMax;

            // exit, from the moment of most lock: the wheel should only unwind. Every reversal of the steering
            // (by at least 15% of the lock) is a correction; steering past straight is countersteer.
            int maxAt = apex;
            for (int i = apex; i <= exitEnd; i++) if (dir * tr.Str[i] > dir * tr.Str[maxAt]) maxAt = i;
            float swing = Math.Max(0.03f, c.SteerMax * 0.15f);
            float ext = dir * tr.Str[maxAt];    // steering towards the corner, positive
            int trend = -1;                     // -1 = unwinding, +1 = adding lock again
            bool gasOn = false;
            for (int i = maxAt; i <= exitEnd; i++)
            {
                float s = dir * tr.Str[i];
                c.CounterSteer = Math.Max(c.CounterSteer, -s);
                if (trend < 0) { if (s < ext) ext = s; else if (s > ext + swing) { c.SteerCorrections++; trend = 1; ext = s; } }
                else { if (s > ext) ext = s; else if (s < ext - swing) { c.SteerCorrections++; trend = -1; ext = s; } }
            }
            // throttle backed off for more than 0.25 s (the gearbox cuts it for ~0.1 s on every upshift)
            float liftStart = float.NaN;
            for (int i = apex; i <= exitEnd; i++)
            {
                if (tr.Gas[i] > 0.6f) { gasOn = true; liftStart = float.NaN; }
                else if (gasOn && tr.Gas[i] < 0.3f && tr.Brk[i] < 0.05f)
                {
                    if (float.IsNaN(liftStart)) liftStart = tr.T[i];
                    else if (tr.T[i] - liftStart > 0.25f) { c.GasLifts++; gasOn = false; liftStart = float.NaN; }
                }
            }
        }

        public static CornerCmp Compare(Trace refTr, List<Corner> refCorners, int k, Trace lapTr)
        {
            return Compare(refTr, refCorners, k, lapTr, float.MaxValue);
        }

        // upTo: judge the corner only up to this distance (live, right after the exit)
        public static CornerCmp Compare(Trace refTr, List<Corner> refCorners, int k, Trace lapTr, float upTo)
        {
            Corner rc = refCorners[k];
            float prev = k > 0 ? refCorners[k - 1].ApexD : 0;
            float next = k < refCorners.Count - 1 ? refCorners[k + 1].ApexD : refTr.Length;
            Corner lc = Measure(lapTr, rc.ApexD, prev, next);
            lc.Num = rc.Num;
            float end = Math.Min(rc.EndD, upTo);
            lc.ExitSpeed = lapTr.SpeedAt(Math.Min(rc.ExitD, end));
            CornerCmp c = new CornerCmp();
            c.Ref = rc; c.Lap = lc;
            if (rc.Braked && lc.Braked) { c.BrakeDiff = lc.BrakeD - rc.BrakeD; c.BrakeForceDiff = lc.BrakeForce - rc.BrakeForce; }
            if (!float.IsNaN(rc.ThrottleD) && !float.IsNaN(lc.ThrottleD)) c.ThrDiff = lc.ThrottleD - rc.ThrottleD;
            c.MinSpdDiff = lc.MinSpeed - rc.MinSpeed;
            c.ExitSpdDiff = lc.ExitSpeed - refTr.SpeedAt(Math.Min(rc.ExitD, end));
            c.CoastDiff = lc.CoastS - rc.CoastS;
            c.PeakBrakeDiff = lc.PeakBrake - rc.PeakBrake;
            c.SegDelta = (lapTr.TimeAt(end) - lapTr.TimeAt(rc.StartD)) - (refTr.TimeAt(end) - refTr.TimeAt(rc.StartD));
            if (rc.TurnSign != 0)
            {
                c.EntryOff = Offset(refTr, lapTr, rc.OnsetD, rc.TurnSign);
                c.ApexOff = Offset(refTr, lapTr, rc.ApexD, rc.TurnSign);
                float exitD = Math.Min((float.IsNaN(rc.ThrottleD) ? rc.ApexD : rc.ThrottleD) + 60, end);
                c.ExitOff = Offset(refTr, lapTr, exitD, rc.TurnSign);
            }
            return c;
        }

        static int Round5(float v) { return (int)(Math.Round(v / 5.0) * 5); }
        public static string Sec(float s) { return Math.Abs(s).ToString("0.0", CultureInfo.GetCultureInfo("pl-PL")); }
        public static string Sec2(float s) { return s.ToString("+0.00;-0.00", Inv); }

        // Picks the single most important thing to fix in a corner, or null if nothing stands out.
        public static Tip MakeTip(CornerCmp c, bool line)
        {
            int n = c.Ref.Num;
            Tip best = null;
            Func<float, string, string, Tip> consider = delegate (float score, string sh, string lg)
            {
                if (best == null || score > best.Score) { best = new Tip(); best.Score = score; best.Short = sh; best.Long = lg; }
                return best;
            };
            if (!float.IsNaN(c.BrakeDiff) && c.BrakeDiff < -12)
            {
                int m = Round5(-c.BrakeDiff);
                consider(-c.BrakeDiff / 10f,
                    string.Format("Zakręt {0}, hamujesz za wcześnie, o {1} metrów", n, m),
                    string.Format("Z{0}: hamujesz ok. {1} m wcześniej niż na najlepszym okrążeniu – przesuń punkt hamowania bliżej zakrętu.", n, m));
            }
            if (!float.IsNaN(c.BrakeDiff) && c.BrakeDiff > 12 && c.MinSpdDiff < -3)
            {
                consider(c.BrakeDiff / 10f + -c.MinSpdDiff / 3f,
                    string.Format("Zakręt {0}, za późno na hamulec", n),
                    string.Format("Z{0}: hamujesz {1} m później, ale szczyt jest wolniejszy o {2:0} km/h – przestrzeliwujesz zakręt. Hamuj odrobinę wcześniej i płynniej puszczaj hamulec.", n, Round5(c.BrakeDiff), -c.MinSpdDiff));
            }
            if (c.MinSpdDiff < -4)
            {
                consider(-c.MinSpdDiff / 3f,
                    string.Format("Zakręt {0}, szczyt za wolno, minus {1:0}", n, -c.MinSpdDiff),
                    string.Format("Z{0}: minimalna prędkość niższa o {1:0} km/h ({2:0} vs {3:0}) – wnieś więcej prędkości, wcześniej odpuszczaj hamulec.", n, -c.MinSpdDiff, c.Lap.MinSpeed, c.Ref.MinSpeed));
            }
            if (!float.IsNaN(c.ThrDiff) && c.ThrDiff > 15)
            {
                consider(c.ThrDiff / 12f,
                    string.Format("Zakręt {0}, gaz za późno", n),
                    string.Format("Z{0}: pełny gaz ok. {1} m później niż na wzorcu – wcześniej i zdecydowanie otwieraj gaz na wyjściu.", n, Round5(c.ThrDiff)));
            }
            if (c.PeakBrakeDiff < -0.15f && !float.IsNaN(c.BrakeDiff) && c.BrakeDiff < -5)
            {
                consider(-c.PeakBrakeDiff * 8f,
                    string.Format("Zakręt {0}, hamuj mocniej i krócej", n),
                    string.Format("Z{0}: maksymalne hamowanie {1:0}% vs {2:0}% na wzorcu, a hamujesz wcześniej – hamuj później, ale mocniej.", n, c.Lap.PeakBrake * 100, c.Ref.PeakBrake * 100));
            }
            if (c.BrakeForceDiff < -0.2f)
            {
                consider(-c.BrakeForceDiff * 6f,
                    string.Format("Zakręt {0}, hamuj mocniej", n),
                    string.Format("Z{0}: hamulec średnio {1:0}% w pierwszej sekundzie vs {2:0}% na wzorcu – wciśnij mocniej od razu.", n, c.Lap.BrakeForce * 100, c.Ref.BrakeForce * 100));
            }
            if (c.BrakeForceDiff > 0.2f && c.MinSpdDiff < -3)
            {
                consider(c.BrakeForceDiff * 6f + -c.MinSpdDiff / 3f,
                    string.Format("Zakręt {0}, za mocno hamujesz", n),
                    string.Format("Z{0}: hamulec średnio {1:0}% vs {2:0}% na wzorcu, a szczyt wolniejszy o {3:0} km/h – hamuj lżej albo krócej.", n, c.Lap.BrakeForce * 100, c.Ref.BrakeForce * 100, -c.MinSpdDiff));
            }
            // pedal and steering work, against the best version of this corner (c.Ref). Only what your laps show
            // costs time or ends off track: a rear slide (countersteer) and both pedals pressed before the apex.
            // Throttle held while braking, steering corrections, throttle lifts and full throttle with lock showed
            // no loss in the data (full throttle with lock was even faster in this car), so they are not coached.
            Corner L = c.Lap, R = c.Ref;
            if (L.CounterSteer >= 0.15f && L.CounterSteer > R.CounterSteer + 0.12f)
                consider(2f + L.CounterSteer * 5f,
                    string.Format("Zakręt {0}, uślizg tyłu, łagodniej gaz", n),
                    string.Format(Inv, "Z{0}: na wyjściu kontrujesz kierownicą ({1:0}% skrętu w drugą stronę) – tył ucieka. Dodawaj gaz płynniej, w miarę jak prostujesz koła.", n, L.CounterSteer * 100));
            if (L.OverlapS >= 0.8f && L.OverlapS > R.OverlapS + 0.5f)
                consider(L.OverlapS * 1.2f,
                    string.Format("Zakręt {0}, gaz i hamulec naraz", n),
                    string.Format(Inv, "Z{0}: przez {1:0.0} s przed szczytem wciskasz gaz i hamulec jednocześnie – najpierw puść hamulec, dopiero potem dodawaj gaz.", n, L.OverlapS));
            if (L.Braked && R.Braked && !float.IsNaN(L.BrakeReleaseD) && !float.IsNaN(R.BrakeReleaseD) && c.MinSpdDiff < -2)
            {
                float rel = L.BrakeReleaseD - R.BrakeReleaseD;
                if (rel < -15)
                    consider(-rel / 10f,
                        string.Format("Zakręt {0}, prowadź hamulec dalej w zakręt", n),
                        string.Format("Z{0}: puszczasz hamulec {1} m wcześniej niż na najlepszym przejeździe, a szczyt jest wolniejszy o {2:0} km/h – zdejmuj hamulec stopniowo aż do skrętu (trail braking), wtedy przód lepiej skręca.", n, Round5(-rel), -c.MinSpdDiff));
                else if (rel > 15)
                    consider(rel / 10f,
                        string.Format("Zakręt {0}, za długo na hamulcu", n),
                        string.Format("Z{0}: trzymasz hamulec {1} m dłużej niż na najlepszym przejeździe, a szczyt jest wolniejszy o {2:0} km/h – puść go wcześniej, żeby auto przetoczyło się przez szczyt.", n, Round5(rel), -c.MinSpdDiff));
            }

            if (line && c.ApexOff < -1.0f)
            {
                consider(-c.ApexOff * 0.9f,
                    string.Format("Zakręt {0}, szczyt za szeroko", n),
                    string.Format(Inv, "Z{0}: na szczycie jesteś {1:0.0} m dalej od wewnętrznej niż na wzorcu – celuj bliżej tarki.", n, -c.ApexOff));
            }
            if (line && c.ApexOff > 1.5f)
            {
                consider(c.ApexOff * 0.6f,
                    string.Format("Zakręt {0}, za ciasno, za wcześnie w szczyt", n),
                    string.Format(Inv, "Z{0}: wchodzisz w szczyt {1:0.0} m ciaśniej niż na wzorcu – za wczesny szczyt psuje wyjście, skręcaj odrobinę później.", n, c.ApexOff));
            }
            if (line && c.EntryOff > 1.5f)
            {
                consider(c.EntryOff * 0.7f,
                    string.Format("Zakręt {0}, wejdź szerzej", n),
                    string.Format(Inv, "Z{0}: przed zakrętem jesteś {1:0.0} m bliżej wewnętrznej – ustaw auto szerzej przed hamowaniem.", n, c.EntryOff));
            }
            if (line && c.ExitOff > 1.5f)
            {
                consider(c.ExitOff * 0.7f,
                    string.Format("Zakręt {0}, wykorzystaj całe wyjście", n),
                    string.Format(Inv, "Z{0}: na wyjściu zostawiasz {1:0.0} m toru – pozwól autu wyjechać szeroko i wcześniej daj pełny gaz.", n, c.ExitOff));
            }
            if (best == null && c.ExitSpdDiff < -5)
            {
                consider(-c.ExitSpdDiff / 4f,
                    string.Format("Zakręt {0}, wolne wyjście", n),
                    string.Format("Z{0}: prędkość na wyjściu niższa o {1:0} km/h – lepsze wyjście daje czas na całej prostej.", n, -c.ExitSpdDiff));
            }
            return best;
        }
    }
}
