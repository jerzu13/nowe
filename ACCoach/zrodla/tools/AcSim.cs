using System;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

// Test-only fake of Assetto Corsa's shared memory: drives laps on a 3 km track with 3 corners,
// with a different mistake on each lap, so ACCoach can be tested without the game.
static class AcSim
{
    const float L = 3000f;
    static readonly float[] Dc = { 600f, 1500f, 2400f };      // apex positions (m)
    static readonly float[] Vmin = { 90f, 60f, 120f };         // apex speeds (km/h)
    const float Vmax = 230f;

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

    // per-lap variation: decel m/s² per corner, apex speed offset, throttle delay (m) after apex
    static void LapParams(int lap, float[] decel, float[] vadd, float[] delay)
    {
        for (int i = 0; i < 3; i++) { decel[i] = 14f; vadd[i] = 0; delay[i] = 10f; }
        if (lap == 2) { vadd[1] = 3; }                              // best lap: quicker corner 2
        if (lap == 3) { decel[0] = 7f; vadd[0] = -8; }              // corner 1: brakes early and gently, slow apex
        if (lap == 5) { delay[2] = 60f; }                           // corner 3: late throttle
        if (lap == 6) { delay[0] = 45f; vadd[1] = -6; }             // corner 1 late throttle, corner 2 slow
    }

    static float Speed(float d, float[] decel, float[] vadd, float[] delay, out float brake, out float gas)
    {
        float v = Vmax / 3.6f;
        brake = 0; gas = 1;
        int mode = 0; float modeDecel = 0;
        for (int k = -1; k <= 1; k++)
            for (int i = 0; i < 3; i++)
            {
                float dc = Dc[i] + k * L;
                float vm = (Vmin[i] + vadd[i]) / 3.6f;
                float c;
                int m;
                if (d <= dc) { c = (float)Math.Sqrt(vm * vm + 2 * decel[i] * (dc - d)); m = 1; }
                else if (d <= dc + delay[i]) { c = vm; m = 2; }
                else { c = (float)Math.Sqrt(vm * vm + 2 * 6f * (d - dc - delay[i])); m = 3; }
                if (c < v) { v = c; mode = m; modeDecel = decel[i]; }
            }
        if (mode == 1) { brake = Math.Min(1f, modeDecel / 14f); gas = 0; }
        else if (mode == 2) { gas = 0.3f; }
        return v;
    }

    // Wheels and tyres (AC 1.14+ physics page): tyres warm up over the laps; lap 4 locks the fronts into
    // corner 2, lap 5 spins the rears out of corner 3, lap 6 slides the rear out of corner 1.
    static void Wheels(MemoryMappedViewAccessor p, float d, float v, float brake, float gas, int lap)
    {
        int near = -1;
        for (int i = 0; i < 3; i++) if (Math.Abs(d - Dc[i]) < 110) near = i;
        float lat = near >= 0 ? 1.3f * (float)Math.Exp(-Math.Pow((d - Dc[near]) / 70.0, 2)) : 0;
        p.Write(44, lat);                                     // accG[0]
        p.Write(52, brake > 0 ? -1.4f * brake : gas * 0.4f);  // accG[2]
        p.Write(300, lat * 9.81f / Math.Max(v, 1f));          // yaw rate
        float sa = 0.06f * lat, srF = 0.01f, srR = 0.02f * gas, saR = 0.8f * sa;
        if (lap == 3 && brake > 0.5f && d > Dc[1] - 120 && d < Dc[1]) srF = -0.4f;
        if (lap == 4 && gas > 0.5f && d > Dc[2] + 20 && d < Dc[2] + 100) srR = 0.3f;
        if (lap == 5 && d > Dc[0] && d < Dc[0] + 80) saR = 1.8f * sa + 0.02f;
        float core = Math.Min(88f, 55f + 6f * lap), press = Math.Min(27.5f, 24f + 0.6f * lap);
        for (int w = 0; w < 4; w++)
        {
            p.Write(72 + 4 * w, 3200f + (w < 2 ? 400 * brake : 300 * gas));
            p.Write(88 + 4 * w, press + 0.2f * w);
            p.Write(152 + 4 * w, core + w);
            p.Write(348 + 4 * w, 250f + 150f * brake);
            p.Write(640 + 4 * w, w < 2 ? srF : srR);
            p.Write(656 + 4 * w, w < 2 ? sa : saR);
        }
    }

    static int Main(string[] args)
    {
        int laps = 7;
        float timeScale = 5f;
        // --order time|laps: at the line the lap timer resets a few frames before (time) or after (laps) the lap
        // counter goes up, as the game may do offline; default: both in the same frame
        string order = "";
        int lag = 4;
        // --pits N: first N seconds (game time) standing in the pit box just before the line with a lap timer that
        // keeps restarting – a crossing must not be taken from that (1.8.1 logged "Out lap" over and over)
        float pits = 0;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--laps") laps = int.Parse(args[i + 1]);
            if (args[i] == "--order") order = args[i + 1];
            if (args[i] == "--pits") pits = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
        }
        int shownCompleted = 0, shownLastMs = 0, lagLeft = 0;
        MemoryMappedFile fp, fg, fs;
        // test memory only (ACCoach --test-shm reads it); the game's acpmf_* pages are never touched
        MemoryMappedViewAccessor p = Map(@"Local\acpmf_test_physics", out fp);
        MemoryMappedViewAccessor g = Map(@"Local\acpmf_test_graphics", out fg);
        MemoryMappedViewAccessor s = Map(@"Local\acpmf_test_static", out fs);

        W(s, 68, "sim_car", 33);
        W(s, 134, "sim_track", 33);
        s.Write(400, 3);
        s.Write(412, 8000);
        s.Write(520, L);
        W(s, 524, "", 33);

        float d = pits > 0 ? 0.985f * L : 0.9f * L;   // start mid-lap (or in the pit box): the first lap is an out lap
        int completed = 0, packet = 0, lastMs = 0, bestMs = 0;
        double lapT = 0;
        float[] decel = new float[3], vadd = new float[3], delay = new float[3];
        LapParams(0, decel, vadd, delay);
        float dt = 1f / 60f * timeScale;
        g.Write(4, 2); // LIVE

        while (completed < laps)
        {
            float brake, gas;
            float v = Speed(d, decel, vadd, delay, out brake, out gas);
            if (pits > 0)
            {
                pits -= dt;
                v = 0; gas = 0; brake = 0;
                if (lapT > 2) lapT = 0;   // the timer starting over while the car stands
            }
            d += v * dt;
            lapT += dt;
            if (d >= L)
            {
                d -= L;
                completed++;
                lastMs = (int)Math.Round((lapT - d / Math.Max(v, 1)) * 1000);
                if (completed > 1 && (bestMs == 0 || lastMs < bestMs)) bestMs = lastMs;
                lapT = d / Math.Max(v, 1);
                LapParams(completed, decel, vadd, delay);
                Console.WriteLine("sim: lap " + completed + " done, " + lastMs + " ms");
                lagLeft = lag;
            }
            // what the game shows: with --order one of the two changes comes a few frames late
            bool lagging = lagLeft > 0;
            if (lagLeft > 0) lagLeft--;
            if (!lagging || order != "time") { shownCompleted = completed; shownLastMs = lastMs; }
            int shownT = lagging && order == "laps" ? (int)(lapT * 1000) + lastMs : (int)(lapT * 1000);
            int tyresOut = (completed == 4 && d > 2380 && d < 2420) ? 3 : 0;   // lap 4 is cut -> invalid
            // track is a circle; o = metres outwards from the normal line
            float o = 0;
            if (completed == 6) o += 2.5f * (float)Math.Exp(-Math.Pow((d - Dc[1]) / 60.0, 2));      // corner 2 wide at apex
            if (completed == 5 && d > 2050 && d < 2250) o -= 2.5f;                                 // corner 3 entry too far inside
            double R = L / (2 * Math.PI), th = 2 * Math.PI * d / L;
            g.Write(252, (float)((R + o) * Math.Cos(th)));
            g.Write(260, (float)((R + o) * Math.Sin(th)));
            packet++;
            p.Write(0, packet);
            p.Write(4, gas);
            p.Write(8, brake);
            p.Write(16, 4);
            p.Write(20, 6000);
            p.Write(24, 0f);
            p.Write(28, v * 3.6f);
            p.Write(61 * 4, tyresOut);
            Wheels(p, d, v, brake, gas, completed);
            g.Write(132, shownCompleted);
            g.Write(140, shownT);
            g.Write(144, shownLastMs);
            g.Write(148, bestMs);
            g.Write(248, d / L);
            g.Write(276, 0);
            g.Write(0, packet);
            Thread.Sleep(16);
        }
        g.Write(4, 0); // OFF
        Thread.Sleep(1500);
        return 0;
    }
}
