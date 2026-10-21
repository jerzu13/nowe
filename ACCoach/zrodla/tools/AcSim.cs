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

    static int Main(string[] args)
    {
        int laps = 7;
        float timeScale = 5f;
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

        float d = 0.9f * L;   // start mid-lap: the first lap is an out lap
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
            }
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
            g.Write(132, completed);
            g.Write(140, (int)(lapT * 1000));
            g.Write(144, lastMs);
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
