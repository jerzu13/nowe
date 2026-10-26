using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

// Test-only: replays recorded ACCoach lap CSVs into fake Assetto Corsa shared memory, so the live coach
// can be checked on real laps without the game. The first lap is played from 90% as an out lap.
// Usage: AcReplay.exe [--realtime] lap1.csv lap2.csv ...   (--realtime: at the game's pace, for voice timing)
static class AcReplay
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static MemoryMappedViewAccessor Map(string name, out MemoryMappedFile f)
    {
        f = MemoryMappedFile.CreateOrOpen(name, 4096);
        return f.CreateViewAccessor();
    }

    static void W(MemoryMappedViewAccessor a, int off, string s, int chars)
    {
        byte[] b = new byte[chars * 2];
        Encoding.Unicode.GetBytes(s, 0, s.Length, b, 0);
        a.WriteArray(off, b, 0, b.Length);
    }

    static string Meta(string header, string key)
    {
        foreach (string kv in header.TrimStart('#', ' ').Split(';'))
            if (kv.StartsWith(key + "=")) return kv.Substring(key.Length + 1);
        return "";
    }

    static int Main(string[] args)
    {
        bool realtime = Array.IndexOf(args, "--realtime") >= 0;
        args = Array.FindAll(args, a => a != "--realtime");
        if (args.Length == 0) { Console.WriteLine("Usage: AcReplay.exe lap1.csv [lap2.csv ...]"); return 2; }
        MemoryMappedFile fp, fg, fs;
        // test memory only (ACCoach --test-shm reads it); the game's acpmf_* pages are never touched
        MemoryMappedViewAccessor p = Map(@"Local\acpmf_test_physics", out fp);
        MemoryMappedViewAccessor g = Map(@"Local\acpmf_test_graphics", out fg);
        MemoryMappedViewAccessor s = Map(@"Local\acpmf_test_static", out fs);

        string first = File.ReadAllLines(args[0])[0];
        W(s, 68, Meta(first, "car"), 33);
        W(s, 134, Meta(first, "track"), 33);
        s.Write(400, 3);
        s.Write(412, 8500);
        s.Write(520, float.Parse(Meta(first, "spline"), Inv));
        W(s, 524, Meta(first, "config"), 33);
        g.Write(4, 2); // LIVE

        int packet = 0, completed = 0, lastMs = 0, bestMs = 0;
        for (int li = 0; li < args.Length; li++)
        {
            string[] lines = File.ReadAllLines(args[li]);
            int lapMs = int.Parse(Meta(lines[0], "lap_ms"), Inv);
            DateTime start = DateTime.Now;
            int t0 = -1;
            for (int r = 2; r < lines.Length; r += realtime ? 1 : 2)
            {
                // t_ms,pos,speed,gas,brake,clutch,steer,gear,rpm,lat_g,lon_g,x,z,tyres_out
                string[] c = lines[r].Split(',');
                float pos = float.Parse(c[1], Inv);
                if (li == 0 && pos < 0.9f) continue;
                packet++;
                p.Write(0, packet);
                p.Write(4, float.Parse(c[3], Inv));
                p.Write(8, float.Parse(c[4], Inv));
                p.Write(16, int.Parse(c[7], Inv));
                p.Write(20, int.Parse(c[8], Inv));
                p.Write(24, float.Parse(c[6], Inv));
                p.Write(28, float.Parse(c[2], Inv));
                p.Write(61 * 4, int.Parse(c[13], Inv));
                p.Write(44, float.Parse(c[9], Inv));
                p.Write(52, float.Parse(c[10], Inv));
                if (c.Length >= 41)   // wheels and tyres, laps recorded by AC Coach 1.4+
                {
                    p.Write(300, float.Parse(c[14], Inv));
                    int[] off = { 640, 656, 72, 152, 88, 348 };
                    for (int g2 = 0; g2 < 6; g2++)
                        for (int w = 0; w < 4; w++) p.Write(off[g2] + 4 * w, float.Parse(c[15 + g2 * 4 + w], Inv));
                    p.Write(672, float.Parse(c[39], Inv));
                    p.Write(676, float.Parse(c[40], Inv));
                }
                g.Write(132, completed);
                g.Write(140, int.Parse(c[0], Inv));
                g.Write(144, lastMs);
                g.Write(148, bestMs);
                g.Write(248, pos);
                g.Write(252, float.Parse(c[11], Inv));
                g.Write(260, float.Parse(c[12], Inv));
                g.Write(276, 0);
                g.Write(0, packet);
                if (!realtime) { Thread.Sleep(4); continue; }
                int tms = int.Parse(c[0], Inv);
                if (t0 < 0) { t0 = tms; start = DateTime.Now; }
                int wait = (int)(tms - t0 - (DateTime.Now - start).TotalMilliseconds);
                if (wait > 0) Thread.Sleep(wait);
            }
            completed++;
            lastMs = lapMs;
            if (bestMs == 0 || lapMs < bestMs) bestMs = lapMs;
            Console.WriteLine("replay: " + Path.GetFileName(args[li]) + " done");
        }
        // cross the line once more so the last lap gets saved
        packet++;
        g.Write(132, completed); g.Write(140, 50); g.Write(144, lastMs); g.Write(248, 0.001f); g.Write(0, packet);
        Thread.Sleep(500);
        g.Write(4, 0); // OFF
        Thread.Sleep(1500);
        return 0;
    }
}
