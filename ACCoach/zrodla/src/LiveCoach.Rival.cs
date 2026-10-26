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
    // Rival from the server results: sector gaps while driving.
    public partial class LiveCoach
    {
        // ---------- rival ----------

        void SetupRival()
        {
            rival = Rival.Load(o.DataDir, si.Car, si.Track, si.Config);
            if (rival == null) return;
            sectorBounds = Sectors.Bounds(comboDir, si.Track, si.Config, refLap != null ? refLap.Trace : null);
            if (sectorBounds == null || sectorBounds.Length + 1 != rival.SectorCount)
            {
                Console.WriteLine(string.Format(L.T("Rywal {0}: nie znam granic sektorów tego toru – porównanie tylko na mecie.", "Rival {0}: sector boundaries of this track unknown – compared at the line only."), rival.Name));
                sectorBounds = null;
                return;
            }
            mySectorBest = null;
            foreach (Lap l in allLaps.Where(l => l.Valid)) UpdateMySectors(l, false);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(string.Format(Inv, L.T("RYWAL: {0} – najlepsze {1} ({2}), z najlepszych sektorów {3}", "RIVAL: {0} – best {1} ({2}), from best sectors {3}"),
                rival.Name, Lap.FormatTime(rival.BestLapMs), string.Join(" / ", rival.BestLapSectors.Select(x => (x / 1000.0).ToString("0.000", Inv)).ToArray()),
                Lap.FormatTime(rival.IdealMs)));
            Console.ResetColor();
            if (mySectorBest != null)
                Console.WriteLine(string.Format(Inv, L.T("      Twoje najlepsze sektory: {0}, razem {1}", "      Your best sectors: {0}, total {1}"),
                    string.Join(" / ", mySectorBest.Select(x => (x / 1000.0).ToString("0.000", Inv)).ToArray()), Lap.FormatTime(mySectorBest.Sum())));
        }

        void UpdateMySectors(Lap l, bool announce = true)
        {
            if (sectorBounds == null) return;
            if (l.Trace == null || Math.Abs(l.Trace.Length - length) > 1) l.BuildTrace(length);
            int[] s = Sectors.Times(l.Trace, sectorBounds, l.LapMs);
            if (s == null) return;
            if (mySectorBest == null) { mySectorBest = s; return; }
            for (int i = 0; i < s.Length; i++)
                if (s[i] < mySectorBest[i])
                {
                    mySectorBest[i] = s[i];
                    if (announce) Console.WriteLine(string.Format(Inv, L.T("      Twój najlepszy sektor {0}: {1:0.000} s", "      Your best sector {0}: {1:0.000} s"), i + 1, s[i] / 1000.0));
                }
        }

        // After each sector: your time against the rival's best lap (and your own best sector) on the console,
        // and "Sektor 1, plus 0,6 do rywala" by voice (the last sector goes into the lap-time call instead).
        void SectorDone(int i, float s, bool speak)
        {
            if (i >= rival.SectorCount) return;
            float gap = s - rival.BestLapSectors[i] / 1000f;
            Console.WriteLine(string.Format(Inv, L.T("  [SEKTOR {0}] {1:0.000} s | {2} {3:0.000} (najlepszy {4:0.000}, średnio {5:0.000}) | {6:+0.00;-0.00}{7}", "  [SECTOR {0}] {1:0.000} s | {2} {3:0.000} (best {4:0.000}, average {5:0.000}) | {6:+0.00;-0.00}{7}"),
                i + 1, s, rival.Name, rival.BestLapSectors[i] / 1000.0, rival.BestSectors[i] / 1000.0, rival.AvgSectors[i] / 1000.0, gap,
                mySectorBest != null ? string.Format(Inv, L.T(" | Twój najlepszy {0:0.000}", " | your best {0:0.000}"), mySectorBest[i] / 1000.0) : ""));
            if (speak && !o.Quiet) Enqueue(string.Format(L.T("Sektor {0}, {1} do rywala", "Sector {0}, {1} to the rival"), i + 1, Rival.Spoken(gap)), false);
        }
    }
}
