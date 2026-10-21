using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ACCoach
{
    // Post-session analysis: compares the latest session's laps with the best lap, corner by corner.
    public static class Analyzer
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        class CornerStats
        {
            public Corner Ref;
            public List<CornerCmp> Cmps = new List<CornerCmp>();
            public float AvgLoss, StdLoss;
        }

        static float Avg(IEnumerable<float> xs)
        {
            List<float> l = xs.Where(x => !float.IsNaN(x)).ToList();
            return l.Count == 0 ? float.NaN : l.Average();
        }

        static float Median(IEnumerable<float> xs)
        {
            List<float> l = xs.Where(x => !float.IsNaN(x)).OrderBy(x => x).ToList();
            if (l.Count == 0) return float.NaN;
            return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
        }

        static float Std(IEnumerable<float> xs)
        {
            List<float> l = xs.Where(x => !float.IsNaN(x)).ToList();
            if (l.Count < 2) return 0;
            double m = l.Average();
            return (float)Math.Sqrt(l.Sum(x => (x - m) * (x - m)) / (l.Count - 1));
        }

        static object J(float v) { return float.IsNaN(v) ? null : (object)Math.Round(v, 3); }

        public static string NewestCombo(string dataDir)
        {
            if (!Directory.Exists(dataDir)) return null;
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string d in Directory.GetDirectories(dataDir))
            {
                string lapsDir = Path.Combine(d, "laps");
                if (!Directory.Exists(lapsDir)) continue;
                foreach (string f in Directory.GetFiles(lapsDir, "*.csv"))
                {
                    DateTime t = File.GetLastWriteTime(f);
                    if (t > bestTime) { bestTime = t; best = d; }
                }
            }
            return best;
        }

        public static int Run(string dataDir, string comboDir, bool open = true)
        {
            if (comboDir == null) comboDir = NewestCombo(dataDir);
            if (comboDir == null) { Console.WriteLine("Brak nagranych okrążeń w " + dataDir); return 1; }
            List<Lap> laps = Lap.LoadAll(comboDir);
            Lap refLap = Lap.BestValid(laps);
            if (refLap == null) { Console.WriteLine("Brak ważnych okrążeń w " + comboDir); return 1; }

            float length = refLap.EstimateLength();
            refLap.BuildTrace(length);
            List<Corner> corners = Corners.Detect(refLap.Trace);
            Corners.Renumber(corners, comboDir, refLap.Trace);

            // latest session = laps chained with gaps under 60 minutes, counting back from the newest
            List<Lap> session = new List<Lap>();
            for (int i = laps.Count - 1; i >= 0; i--)
            {
                if (session.Count > 0 && (session[session.Count - 1].Date - laps[i].Date).TotalMinutes > 60) break;
                session.Add(laps[i]);
            }
            session.Reverse();
            List<Lap> valid = session.Where(l => l.Valid).ToList();
            int sessionBest = valid.Count > 0 ? valid.Min(l => l.LapMs) : 0;
            List<Lap> usable = valid.Where(l => l.LapMs <= sessionBest * 1.07).ToList();

            // Every corner driven on track counts, also from laps made invalid by another corner:
            // an off-track in one place should not throw away the other corners of that lap.
            List<Measured> measured = MeasureSession(session, refLap, corners, length);
            // ... but not the corner right after an off-track either: the car is still coming back from it
            HashSet<string> offAt = new HashSet<string>(measured.Where(m => m.Off != 0).Select(m => m.Lap.File + "#" + m.K));
            Func<Measured, bool> clean = m => m.Off == 0 && m.Lap.File != refLap.File && Math.Abs(m.Cmp.SegDelta) < 3
                                              && !offAt.Contains(m.Lap.File + "#" + (m.K - 1));
            List<Lap> compared = measured.Where(clean).Select(m => m.Lap).Distinct().ToList();

            List<CornerStats> stats = new List<CornerStats>();
            for (int k = 0; k < corners.Count; k++)
            {
                CornerStats cs = new CornerStats();
                cs.Ref = corners[k];
                cs.Cmps.AddRange(measured.Where(m => m.K == k && clean(m)).Select(m => m.Cmp));
                cs.AvgLoss = cs.Cmps.Count > 0 ? cs.Cmps.Average(c => c.SegDelta) : 0;
                cs.StdLoss = Std(cs.Cmps.Select(c => c.SegDelta));
                stats.Add(cs);
            }

            // ideal lap: the reference with every corner replaced by its fastest clean version of the session
            float ideal = refLap.LapMs / 1000f + stats.Sum(cs => cs.Cmps.Count > 0 ? Math.Min(0, cs.Cmps.Min(c => c.SegDelta)) : 0);

            StringBuilder r = new StringBuilder();
            string combo = Path.GetFileName(comboDir);
            r.AppendLine("=== AC Coach – analiza sesji ===");
            r.AppendLine(combo.Replace("__", "  |  "));
            r.AppendLine(string.Format("Sesja: {0} okr. ({1} ważnych) | najlepsze {2} | wzorzec {3}{4}",
                session.Count, valid.Count, sessionBest > 0 ? Lap.FormatTime(sessionBest) : "–", Lap.FormatTime(refLap.LapMs),
                compared.Count > 0 ? string.Format(Inv, " | idealne {0} (z Twoich najszybszych zakrętów)", Lap.FormatTime((int)Math.Round(ideal * 1000))) : ""));
            r.AppendLine();

            List<Priority> prio = Priorities(stats, measured, corners);
            List<string> tips = prio.Select(p => p.Text()).ToList();
            if (prio.Count == 0)
                r.AppendLine(compared.Count == 0 ? "Za mało okrążeń do porównania – przejedź kilka okrążeń w jednej sesji."
                                                 : "Brak wyraźnych strat i wyjazdów – jedziesz równo z wzorcem.");
            else
            {
                r.AppendLine("NA NASTĘPNĄ JAZDĘ (od tego, co kosztuje najwięcej):");
                for (int i = 0; i < prio.Count; i++)
                {
                    r.AppendLine(string.Format(Inv, "  {0}. Z{1} – kosztuje ok. {2} s/okr.", i + 1, prio[i].Num, Corners.Sec(prio[i].Cost)));
                    foreach (string l in prio[i].Lines) r.AppendLine("     " + l);
                }
            }
            r.AppendLine();

            List<string> offLines = new List<string>();
            r.AppendLine("ZAKRĘTY          strata  rozrzut  wyjazdy");
            foreach (CornerStats cs in stats)
            {
                int k = corners.IndexOf(cs.Ref);
                List<Measured> mk = measured.Where(m => m.K == k).ToList();
                int offs = mk.Count(m => m.Off != 0), offsExit = mk.Count(m => m.Off > 0);
                string offTxt = offs == 0 ? "–" : string.Format("{0}/{1} ({2})", offs, mk.Count,
                    offsExit == offs ? "wyjście" : offsExit == 0 ? "wejście" : string.Format("{0}× wejście, {1}× wyjście", offs - offsExit, offsExit));
                if (offs > 0) offLines.Add(string.Format("Z{0}: {1}", cs.Ref.Num, offTxt));
                r.AppendLine(string.Format(Inv, "  Z{0,-2}          {1,7}  ±{2:0.00}    {3}", cs.Ref.Num,
                    cs.Cmps.Count > 0 ? Corners.Sec2(cs.AvgLoss) : "–", cs.StdLoss, offTxt));
            }
            r.AppendLine("  (strata = średnio na czystych przejazdach vs wzorzec, + = wolniej; rozrzut = powtarzalność)");

            List<string> reactionLines = ReactionSummary(comboDir, session);
            if (reactionLines.Count > 0)
            {
                r.AppendLine();
                r.AppendLine("PIKNIĘCIA:");
                foreach (string l in reactionLines) r.AppendLine("  " + l);
            }

            List<string> rivalLines = RivalSection(dataDir, comboDir, laps, session, refLap, corners, prio);
            if (rivalLines.Count > 0)
            {
                r.AppendLine();
                foreach (string l in rivalLines) r.AppendLine(l);
            }

            r.AppendLine();
            r.AppendLine("OKRĄŻENIA:");
            foreach (Lap l in session)
            {
                string line = string.Format("  {0:HH:mm}  {1}  {2,6}", l.Date, Lap.FormatTime(l.LapMs), Corners.Sec2((l.LapMs - refLap.LapMs) / 1000f));
                if (l.File == refLap.File) { r.AppendLine(line + "  ★ wzorzec"); continue; }
                List<Measured> ml = measured.Where(m => m.Lap == l).ToList();
                string[] outs = ml.Where(m => m.Off != 0).Select(m => "Z" + corners[m.K].Num).ToArray();
                if (outs.Length > 0) line += "  wyjazd: " + string.Join(", ", outs);
                string[] worst = ml.Where(clean).OrderByDescending(m => m.Cmp.SegDelta).Take(2)
                    .Where(m => m.Cmp.SegDelta > 0.1f).Select(m => "Z" + m.Cmp.Ref.Num + " " + Corners.Sec2(m.Cmp.SegDelta)).ToArray();
                if (worst.Length > 0) line += "  straty: " + string.Join(", ", worst);
                r.AppendLine(line);
            }
            List<string> inputLines = offLines;

            string text = r.ToString();
            Console.WriteLine(text);
            string repDir = Path.Combine(comboDir, "reports");
            Directory.CreateDirectory(repDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm", Inv);
            File.WriteAllText(Path.Combine(repDir, "raport_" + stamp + ".txt"), text, new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(comboDir, "analysis.json"), Json(combo, length, refLap, corners, stats, session, compared, ideal, tips, InputRows(measured)), new UTF8Encoding(false));
            Console.WriteLine("Raport zapisany w: " + repDir);

            List<TrackMap.CornerInfo> info = new List<TrackMap.CornerInfo>();
            foreach (CornerStats cs in stats)
            {
                TrackMap.CornerInfo ci = new TrackMap.CornerInfo();
                ci.Corner = cs.Ref;
                ci.Loss = cs.AvgLoss;
                Tip t = cs.Cmps.Count > 0 && cs.AvgLoss >= 0.05f ? Corners.MakeTip(Averaged(cs), true) : null;
                ci.Tip = t == null ? null : t.Long;
                info.Add(ci);
            }
            string map = Path.Combine(repDir, "mapa_" + stamp + ".html");
            string warn = TrackMap.Write(comboDir, map, length, refLap, info, session, tips, reactionLines, inputLines, rivalLines,
                Sectors.Bounds(comboDir, refLap.Track, refLap.Config, refLap.Trace), Rival.Load(dataDir, refLap.Car, refLap.Track, refLap.Config),
                Rival.LoadLaps(dataDir, refLap.Car, refLap.Track, refLap.Config));
            if (warn != null) Console.WriteLine("Uwaga: " + warn);
            Console.WriteLine("Mapa toru: " + map);
            if (open)
                try { System.Diagnostics.Process.Start(map); } catch (Exception) { }
            return 0;
        }

        // From pikniecia.csv, the session's brakings per corner: typical reaction to the final beep, how far from the
        // aimed point the brake went on, and the lead the trainer uses now.
        static List<string> ReactionSummary(string comboDir, List<Lap> session)
        {
            List<string> lines = new List<string>();
            string f = Path.Combine(comboDir, "pikniecia.csv");
            if (!File.Exists(f)) return lines;
            HashSet<string> names = new HashSet<string>(session.Select(l => Path.GetFileName(l.File)));
            Func<string, float> F = s => { float v; return float.TryParse(s, NumberStyles.Float, Inv, out v) ? v : float.NaN; };
            List<string[]> all = File.ReadAllLines(f).Skip(1).Select(l => l.Split(',')).Where(p => p.Length >= 12 && names.Contains(p[0])).ToList();
            // brakings that ended off track or tens of metres away (a spin, a crash) say nothing about the beep timing
            List<string[]> rows = all.Where(p => p[10] != "3" && p[10] != "4" && !(p.Length >= 14 && Math.Abs(F(p[13])) > 60) && !(Math.Abs(F(p[5])) > 2)).ToList();
            if (rows.Count == 0) return lines;
            Func<IEnumerable<float>, float> Med = xs => { List<float> l = xs.Where(x => !float.IsNaN(x)).OrderBy(x => x).ToList(); return l.Count == 0 ? float.NaN : l[l.Count / 2]; };
            Func<IEnumerable<string[]>, float> MedMiss = rs => Med(rs.Where(p => p.Length >= 14).Select(p => F(p[13])));
            Func<IEnumerable<string[]>, float> MedReact = rs => Med(rs.Select(p => F(p[5])));
            float allMiss = MedMiss(rows);
            List<float> absMiss = rows.Where(p => p.Length >= 14).Select(p => Math.Abs(F(p[13]))).Where(x => !float.IsNaN(x)).ToList();
            lines.Add(string.Format(Inv, "Hamujesz zwykle {0} s po ostatnim piknięciu, {1} od punktu{2}.",
                MedReact(rows).ToString("+0.00;-0.00", Inv),
                float.IsNaN(allMiss) ? "–" : string.Format(Inv, "{0:+0;-0;0} m (zwykle ±{1:0} m)", allMiss, Med(absMiss)),
                all.Count > rows.Count ? string.Format("; pominięte {0} hamowań z wyjazdem", all.Count - rows.Count) : ""));
            // only the corners that need attention: usually off the point by 10 m or more, or often early / late
            foreach (var g in rows.GroupBy(p => int.Parse(p[1], Inv)).OrderBy(g => g.Key))
            {
                float miss = MedMiss(g);
                List<float> re = g.Select(p => F(p[5])).Where(x => !float.IsNaN(x)).ToList();
                int early = re.Count(x => x < -0.35f), late = re.Count(x => x > 0.35f);
                if (float.IsNaN(miss) || (Math.Abs(miss) < 10 && Math.Max(early, late) * 4 < g.Count())) continue;
                bool isEarly = miss <= -10 || (miss < 10 && early > late);
                lines.Add(string.Format(Inv, "Z{0}: {1} w {2} z {3} hamowań{4} – {5}", g.Key,
                    isEarly ? "za wcześnie" : "za późno", isEarly ? early : late, g.Count(),
                    Math.Abs(miss) >= 10 ? string.Format(Inv, ", zwykle {0:0} m {1} punktu", Math.Abs(miss), miss < 0 ? "przed" : "za") : "",
                    isEarly ? "zaufaj sygnałowi, hamuj na ostatnie piknięcie" : "noga na hamulec razem z ostatnim piknięciem"));
            }
            return lines;
        }

        // Pedal and steering work in every corner of every complete lap of the session (valid or not),
        // with whether the car left the track there. For analysis.json and the inputs summary.
        class Measured { public Lap Lap; public int K; public CornerCmp Cmp; public int Off; }

        static List<Measured> MeasureSession(List<Lap> session, Lap refLap, List<Corner> corners, float length)
        {
            List<Measured> all = new List<Measured>();
            foreach (Lap l in session)
            {
                if (l.Trace == null || Math.Abs(l.Trace.Length - length) > 1) l.BuildTrace(length);
                if (l.Trace.Filled < l.Trace.N - 5) continue;
                int[] outAt = OffTrack(l, corners, length);
                for (int k = 0; k < corners.Count; k++)
                    all.Add(new Measured { Lap = l, K = k, Cmp = Corners.Compare(refLap.Trace, corners, k, l.Trace), Off = outAt[k] });
            }
            return all;
        }

        // You against the rival from the "rywale" folder, sector by sector: best lap, best sectors, average,
        // and what follows from it (consistency, the sector with the biggest gap and its corners).
        static List<string> RivalSection(string dataDir, string comboDir, List<Lap> laps, List<Lap> session, Lap refLap, List<Corner> corners, List<Priority> prio)
        {
            List<string> lines = new List<string>();
            Rival rv = Rival.Load(dataDir, refLap.Car, refLap.Track, refLap.Config);
            if (rv == null) return lines;
            float[] bounds = Sectors.Bounds(comboDir, refLap.Track, refLap.Config, refLap.Trace);
            if (bounds == null || bounds.Length + 1 != rv.SectorCount)
            {
                lines.Add(string.Format("RYWAL: {0} – najlepsze {1} (granice sektorów toru nieznane, porównanie tylko całego okrążenia).", rv.Name, Lap.FormatTime(rv.BestLapMs)));
                return lines;
            }
            int n = rv.SectorCount;
            float length = refLap.Trace.Length;
            Func<Lap, int[]> times = l => { if (l.Trace == null || Math.Abs(l.Trace.Length - length) > 1) l.BuildTrace(length); return Sectors.Times(l.Trace, bounds, l.LapMs); };
            int[] myBestLap = times(refLap);
            List<int[]> validAll = laps.Where(l => l.Valid).Select(times).Where(x => x != null).ToList();
            List<int[]> validSession = session.Where(l => l.Valid).Select(times).Where(x => x != null).ToList();
            if (myBestLap == null || validAll.Count == 0) return lines;
            int[] myBestSec = Enumerable.Range(0, n).Select(i => validAll.Min(x => x[i])).ToArray();
            float[] myAvg = validSession.Count > 0 ? Enumerable.Range(0, n).Select(i => (float)validSession.Average(x => x[i])).ToArray() : null;

            Func<IEnumerable<float>, string> row = xs => string.Join("", xs.Select(x => (x / 1000f).ToString("0.00", Inv).PadLeft(8)).ToArray());
            Func<IEnumerable<float>, string> gapRow = xs => string.Join("", xs.Select(x => (x / 1000f).ToString("+0.00;-0.00", Inv).PadLeft(8)).ToArray());
            string head = string.Join("", Enumerable.Range(1, n).Select(i => ("S" + i).PadLeft(8)).ToArray());
            lines.Add(string.Format("RYWAL: {0} – najlepsze {1}, czystych okrążeń {2} z {3}", rv.Name, Lap.FormatTime(rv.BestLapMs), rv.CleanLaps, rv.Laps));
            lines.Add(string.Format("  {0,-30}{1}   okrążenie", "", head));
            Action<string, IEnumerable<float>, float> add = (label, xs, total) =>
                lines.Add(string.Format("  {0,-30}{1}   {2}", label, row(xs), Lap.FormatTime((int)Math.Round(total))));
            add(rv.Name + " najlepsze okrążenie", rv.BestLapSectors.Select(x => (float)x), rv.BestLapMs);
            add("Ty najlepsze okrążenie", myBestLap.Select(x => (float)x), refLap.LapMs);
            lines.Add(string.Format("  {0,-30}{1}   {2}", "  różnica", gapRow(Enumerable.Range(0, n).Select(i => (float)(myBestLap[i] - rv.BestLapSectors[i]))),
                ((refLap.LapMs - rv.BestLapMs) / 1000f).ToString("+0.000;-0.000", Inv)));
            add(rv.Name + " z najlepszych sektorów", rv.BestSectors.Select(x => (float)x), rv.IdealMs);
            add("Ty z najlepszych sektorów", myBestSec.Select(x => (float)x), myBestSec.Sum());
            add(rv.Name + " średnio (czyste)", rv.AvgSectors, rv.AvgSectors.Sum());
            if (myAvg != null) add(string.Format("Ty średnio ({0} ważnych)", validSession.Count), myAvg, myAvg.Sum());

            // what follows
            lines.Add("  Wnioski:");
            float bestGap = (refLap.LapMs - rv.BestLapMs) / 1000f;
            int myValid = session.Count(l => l.Valid);
            if (myAvg != null)
            {
                float avgGap = (myAvg.Sum() - rv.AvgSectors.Sum()) / 1000f;
                lines.Add(string.Format(Inv, "  - Na najlepszym okrążeniu tracisz {0:0.0} s, średnio {1:0.0} s. Ważne okrążenia: Ty {2} z {3}, {4} {5} z {6}.",
                    bestGap, avgGap, myValid, session.Count, rv.Name, rv.Laps - rv.CutLaps, rv.Laps));
                if (avgGap - bestGap > 0.4f)
                    lines.Add(string.Format(Inv, "    Większość różnicy to powtarzalność: {0} jeździ swoje tempo okrążenie za okrążeniem. Najpierw czyste okrążenia, potem szybsze.", rv.Name));
            }
            int worst = Enumerable.Range(0, n).OrderByDescending(i => myBestLap[i] - rv.BestLapSectors[i]).First();
            float from = worst == 0 ? 0 : bounds[worst - 1], to = worst < bounds.Length ? bounds[worst] : length;
            List<int> inSector = corners.Where(c => c.ApexD >= from && c.ApexD < to).Select(c => c.Num).ToList();
            string prioIn = string.Join(", ", prio.Where(p => inSector.Contains(p.Num)).Select(p => "Z" + p.Num).ToArray());
            lines.Add(string.Format(Inv, "  - Najwięcej na najlepszym okrążeniu tracisz w S{0} ({1:+0.00} s): zakręty {2}.{3}",
                worst + 1, (myBestLap[worst] - rv.BestLapSectors[worst]) / 1000f, string.Join(", ", inSector.Select(x => "Z" + x).ToArray()),
                prioIn.Length > 0 ? " " + prioIn + " jest wyżej w \"Na następną jazdę\" – zacznij od tego." : ""));
            lines.Add(string.Format(Inv, "  - Twoje najlepsze sektory razem dają {0} – {1:0.0} s do najlepszego okrążenia rywala ({2}){3}.",
                Lap.FormatTime(myBestSec.Sum()), (myBestSec.Sum() - rv.BestLapMs) / 1000f, rv.Name,
                myAvg != null && myBestSec.Sum() < rv.AvgSectors.Sum() ? string.Format(", i już szybciej niż jego średnie okrążenie") : ""));
            return lines;
        }

        class Priority
        {
            public int Num;
            public float Cost;                 // seconds per lap: average loss + off-tracks
            public List<string> Lines = new List<string>();
            public string Text() { return string.Format(Inv, "Z{0} (ok. {1} s/okr.): {2}", Num, Corners.Sec(Cost), string.Join(" ", Lines.ToArray())); }
        }

        const float OffTrackCostS = 3f;        // what an off-track costs (seconds lost + an invalid lap), spread over the attempts

        // The three corners that cost the most, each with what to do, backed by your own laps:
        // how often and where you leave the track there, and what your fast runs of that corner do differently
        // from your slow ones.
        static List<Priority> Priorities(List<CornerStats> stats, List<Measured> measured, List<Corner> corners)
        {
            List<Priority> list = new List<Priority>();
            foreach (CornerStats cs in stats)
            {
                int k = corners.IndexOf(cs.Ref);
                List<Measured> mk = measured.Where(m => m.K == k).ToList();
                if (mk.Count == 0) continue;
                List<Measured> offs = mk.Where(m => m.Off != 0).ToList();
                Priority p = new Priority { Num = cs.Ref.Num };
                p.Cost = Math.Max(0, cs.AvgLoss) + OffTrackCostS * offs.Count / mk.Count;

                if (offs.Count >= 2)
                {
                    List<Measured> exit = offs.Where(m => m.Off > 0).ToList(), entry = offs.Where(m => m.Off < 0).ToList();
                    bool onExit = exit.Count >= entry.Count;
                    List<Measured> main = onExit ? exit : entry;
                    string cause;
                    if (onExit)
                        cause = main.Count(m => m.Cmp.Lap.CounterSteer >= 0.15f) * 2 >= main.Count
                            ? "zwykle po uślizgu tyłu – dodawaj gaz płynniej, aż koła się wyprostują"
                            : "wynosi Cię szeroko na wyjściu – spokojniej z gazem, aż koła się wyprostują";
                    else
                        cause = main.Count(m => !float.IsNaN(m.Cmp.BrakeDiff) && m.Cmp.BrakeDiff > 12 && m.Cmp.BrakeDiff < 100) * 2 >= main.Count
                            ? "zwykle po za późnym hamowaniu – hamuj na ostatnie piknięcie, nie później"
                            : "za szybko na wejściu – zacznij hamować odrobinę wcześniej";
                    p.Lines.Add(string.Format("Wypadasz w {0} z {1} przejazdów ({2}× na {3}): {4}.",
                        offs.Count, mk.Count, main.Count, onExit ? "wyjściu" : "wejściu", cause));
                }

                string fast = FastVsSlow(cs);
                if (fast != null) p.Lines.Add(fast);
                else if (cs.Cmps.Count > 0 && cs.AvgLoss >= 0.05f)
                {
                    Tip t = Corners.MakeTip(Averaged(cs), true);
                    if (t != null) p.Lines.Add(t.Long.Substring(t.Long.IndexOf(':') + 1).Trim());
                }
                if (p.Cost >= 0.05f && p.Lines.Count > 0) list.Add(p);
            }
            return list.OrderByDescending(p => p.Cost).Take(3).ToList();
        }

        // Fastest third vs slowest third of the clean runs of one corner: the biggest differences in how you
        // drove them, in plain words. Needs at least 6 runs.
        static string FastVsSlow(CornerStats cs)
        {
            List<CornerCmp> runs = cs.Cmps.OrderBy(c => c.SegDelta).ToList();
            if (runs.Count < 6) return null;
            int third = runs.Count / 3;
            List<CornerCmp> fast = runs.Take(third).ToList(), slow = runs.Skip(runs.Count - third).ToList();
            float gain = slow.Average(c => c.SegDelta) - fast.Average(c => c.SegDelta);
            if (gain < 0.1f) return null;
            // medians: one odd run (a missed braking point, a moment) must not decide the advice
            Func<Func<CornerCmp, float>, float> D = f => Median(fast.Select(f)) - Median(slow.Select(f));
            var found = new List<Tuple<float, string>>();
            Action<float, float, string> add = (v, unit, text) => { if (!float.IsNaN(v) && Math.Abs(v) >= unit) found.Add(Tuple.Create(Math.Abs(v) / unit, text)); };
            float brake = D(c => c.Lap.BrakeD), minSpd = D(c => c.Lap.MinSpeed), thr = D(c => c.Lap.ThrottleD);
            float release = D(c => c.Lap.BrakeReleaseD), overlap = D(c => c.Lap.OverlapS), apex = D(c => c.ApexOff);
            add(brake, 8, string.Format("hamujesz {0:0} m {1}", Math.Abs(brake), brake > 0 ? "później" : "wcześniej"));
            add(minSpd, 3, string.Format("szczyt {0:+0;-0} km/h", minSpd));
            add(thr, 15, string.Format("pełny gaz {0:0} m {1}", Math.Abs(thr), thr < 0 ? "wcześniej" : "później"));
            add(release, 15, string.Format("puszczasz hamulec {0:0} m {1}", Math.Abs(release), release > 0 ? "później (dalej w zakręt)" : "wcześniej"));
            add(overlap, 0.4f, overlap < 0 ? "mniej trzymasz gaz i hamulec naraz" : "dłużej trzymasz gaz i hamulec naraz");
            add(apex, 0.8f, string.Format(Inv, "szczyt {0:0.0} m {1}", Math.Abs(apex), apex > 0 ? "bliżej wewnętrznej" : "szerzej"));
            if (found.Count == 0) return null;
            string[] best = found.OrderByDescending(x => x.Item1).Take(2).Select(x => x.Item2).ToArray();
            return string.Format(Inv, "W Twoich szybkich przejazdach tego zakrętu (o {0} s lepszych od wolnych): {1}.", Corners.Sec(gain), string.Join(", ", best));
        }

        static List<object> InputRows(List<Measured> all)
        {
            List<object> rows = new List<object>();
            foreach (Measured m in all)
            {
                {
                    Lap l = m.Lap;
                    CornerCmp x = m.Cmp;
                    Corner c = x.Lap;
                    rows.Add(new Dictionary<string, object> {
                        { "lap", Path.GetFileName(l.File) }, { "valid", l.Valid }, { "corner", x.Ref.Num },
                        { "seg_delta_s", J(x.SegDelta) }, { "min_speed", J(c.MinSpeed) }, { "brake_m", J(c.BrakeD) },
                        { "brake_release_m", J(c.BrakeReleaseD) }, { "throttle_m", J(c.ThrottleD) },
                        { "overlap_s", J(c.OverlapS) }, { "brake_gas_s", J(c.BrakeGasS) }, { "steer_max", J(c.SteerMax) }, { "gas_steer", J(c.GasSteer) },
                        { "counter_steer", J(c.CounterSteer) }, { "steer_corrections", c.SteerCorrections }, { "gas_lifts", c.GasLifts },
                        { "coast_s", J(c.CoastS) }, { "off_track", m.Off } });
                }
            }
            return rows;
        }

        // Per corner: 0 = stayed on track, otherwise metres from the apex where 3+ wheels first left it (+ = after).
        // A corner owns the track from 50 m before its braking to 50 m before the next corner's braking,
        // so a spin on the exit counts for the corner that caused it.
        public static int[] OffTrack(Lap l, List<Corner> corners, float length)
        {
            int[] r = new int[corners.Count];
            for (int i = 0; i < l.Samples.Count; i++)
            {
                if (l.Samples[i].TyresOut < 3) continue;
                float d = Lap.UnwrapPos(l.Samples[i].Pos, i, l.Samples.Count) * length;
                int k = corners.Count - 1;   // before the first corner's braking = exit of the last one
                for (int j = 0; j < corners.Count; j++) if (d >= corners[j].OnsetD - 50) k = j;
                if (r[k] != 0) continue;
                int m = (int)Math.Round(d - corners[k].ApexD);
                r[k] = m == 0 ? 1 : m;
            }
            return r;
        }

        static string M(float v) { return float.IsNaN(v) ? "–" : v.ToString("+0;-0;0", Inv) + " m"; }

        static string LineText(float off)
        {
            if (float.IsNaN(off)) return "–";
            if (Math.Abs(off) < 0.5f) return "ok";
            return string.Format(Inv, "{0:0.0} m {1}", Math.Abs(off), off < 0 ? "szerzej" : "ciaśniej");
        }

        static CornerCmp Averaged(CornerStats cs)
        {
            CornerCmp a = new CornerCmp();
            a.Ref = cs.Ref;
            a.Lap = new Corner();
            a.Lap.Num = cs.Ref.Num;
            a.BrakeDiff = Avg(cs.Cmps.Select(c => c.BrakeDiff));
            a.ThrDiff = Avg(cs.Cmps.Select(c => c.ThrDiff));
            a.MinSpdDiff = Avg(cs.Cmps.Select(c => c.MinSpdDiff));
            a.ExitSpdDiff = Avg(cs.Cmps.Select(c => c.ExitSpdDiff));
            a.CoastDiff = Avg(cs.Cmps.Select(c => c.CoastDiff));
            a.PeakBrakeDiff = Avg(cs.Cmps.Select(c => c.PeakBrakeDiff));
            a.SegDelta = cs.AvgLoss;
            a.EntryOff = Avg(cs.Cmps.Select(c => c.EntryOff));
            a.ApexOff = Avg(cs.Cmps.Select(c => c.ApexOff));
            a.ExitOff = Avg(cs.Cmps.Select(c => c.ExitOff));
            a.Lap.BrakeD = Avg(cs.Cmps.Select(c => c.Lap.BrakeD));
            a.Lap.BrakeReleaseD = Avg(cs.Cmps.Select(c => c.Lap.BrakeReleaseD));
            a.Lap.OverlapS = Avg(cs.Cmps.Select(c => c.Lap.OverlapS));
            a.Lap.BrakeGasS = Avg(cs.Cmps.Select(c => c.Lap.BrakeGasS));
            a.Lap.GasSteer = Avg(cs.Cmps.Select(c => c.Lap.GasSteer));
            a.Lap.CounterSteer = Avg(cs.Cmps.Select(c => c.Lap.CounterSteer));
            a.Lap.SteerCorrections = (int)Math.Round(Avg(cs.Cmps.Select(c => (float)c.Lap.SteerCorrections)));
            a.Lap.GasLifts = (int)Math.Round(Avg(cs.Cmps.Select(c => (float)c.Lap.GasLifts)));
            a.Lap.MinSpeed = cs.Ref.MinSpeed + a.MinSpdDiff;
            a.Lap.PeakBrake = cs.Ref.PeakBrake + (float.IsNaN(a.PeakBrakeDiff) ? 0 : a.PeakBrakeDiff);
            return a;
        }

        static string Json(string combo, float length, Lap refLap, List<Corner> corners, List<CornerStats> stats,
            List<Lap> session, List<Lap> compared, float ideal, List<string> tips, List<object> inputs)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["combo"] = combo;
            root["track_length_m"] = Math.Round(length);
            root["reference"] = new Dictionary<string, object> { { "file", Path.GetFileName(refLap.File) }, { "lap_ms", refLap.LapMs }, { "date", refLap.Date.ToString("s", Inv) } };
            root["ideal_lap_ms"] = (int)Math.Round(ideal * 1000);
            List<object> cl = new List<object>();
            foreach (CornerStats cs in stats)
            {
                Corner c = cs.Ref;
                List<object> per = new List<object>();
                foreach (CornerCmp x in cs.Cmps)
                    per.Add(new Dictionary<string, object> {
                        { "seg_delta_s", J(x.SegDelta) }, { "brake_diff_m", J(x.BrakeDiff) }, { "min_speed_diff", J(x.MinSpdDiff) },
                        { "throttle_diff_m", J(x.ThrDiff) }, { "exit_speed_diff", J(x.ExitSpdDiff) }, { "coast_diff_s", J(x.CoastDiff) },
                        { "peak_brake_diff", J(x.PeakBrakeDiff) }, { "brake_force_diff", J(x.BrakeForceDiff) }, { "entry_offset_m", J(x.EntryOff) }, { "apex_offset_m", J(x.ApexOff) },
                        { "exit_offset_m", J(x.ExitOff) } });
                cl.Add(new Dictionary<string, object> {
                    { "num", c.Num }, { "apex_m", J(c.ApexD) }, { "min_speed", J(c.MinSpeed) }, { "brake_m", J(c.BrakeD) }, { "lift_m", J(c.LiftD) },
                    { "throttle_m", J(c.ThrottleD) }, { "peak_brake", J(c.PeakBrake) }, { "brake_force", J(c.BrakeForce) }, { "entry_speed", J(c.EntrySpeed) }, { "exit_speed", J(c.ExitSpeed) },
                    { "coast_s", J(c.CoastS) }, { "segment_m", new object[] { J(c.StartD), J(c.EndD) } },
                    { "avg_loss_s", J(cs.AvgLoss) }, { "std_loss_s", J(cs.StdLoss) }, { "laps", per } });
            }
            root["corners"] = cl;
            List<object> ll = new List<object>();
            foreach (Lap l in session)
                ll.Add(new Dictionary<string, object> { { "file", Path.GetFileName(l.File) }, { "lap_ms", l.LapMs }, { "valid", l.Valid }, { "compared_index", compared.IndexOf(l) } });
            root["session_laps"] = ll;
            root["tips"] = tips;
            root["inputs"] = inputs;
            return new JavaScriptSerializer().Serialize(root);
        }
    }
}
