using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ACCoach
{
    // What this car can do on this track, learnt from the driver's own laps: how much lateral grip and braking
    // it held (sustained over ~0.3 s, not kerb spikes) and how it accelerates at full throttle, per speed.
    // Simulate() drives the reference line at those limits (quasi-steady-state lap simulation).
    public class LimitModel
    {
        const float G = 9.81f;
        const int Window = 15;                  // ±15 samples (~0.3 s) averaged: sustained g, not spikes
        const float BinKmh = 15f, MinKmh = 40f, MaxKmh = 280f;
        const float GripQuantile = 0.995f;

        public float LatA0, LatK;               // lateral grip  [g] = LatA0 + LatK·v²  (v in m/s; aero)
        public float BrkA0, BrkK;               // braking       [g] = BrkA0 + BrkK·v²
        public float[] AccV, AccA;              // full-throttle acceleration [g] at speed [m/s]
        public int Samples, Laps;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public float LatAt(float v) { return LatA0 + LatK * v * v; }
        public float BrkAt(float v) { return BrkA0 + BrkK * v * v; }

        public float AccAt(float v)
        {
            if (v <= AccV[0]) return AccA[0];
            for (int i = 1; i < AccV.Length; i++)
                if (v <= AccV[i]) return AccA[i - 1] + (AccA[i] - AccA[i - 1]) * (v - AccV[i - 1]) / (AccV[i] - AccV[i - 1]);
            return Math.Max(0f, AccA[AccA.Length - 1] - (v - AccV[AccV.Length - 1]) * 0.01f);
        }

        // null when there is too little data (a few clean laps over a range of speeds are needed)
        public static LimitModel Build(string comboDir)
        {
            string dir = Path.Combine(comboDir, "laps");
            if (!Directory.Exists(dir)) return null;
            int nb = (int)((MaxKmh - MinKmh) / BinKmh);
            List<float>[] lat = new List<float>[nb], brk = new List<float>[nb], acc = new List<float>[nb];
            for (int b = 0; b < nb; b++) { lat[b] = new List<float>(); brk[b] = new List<float>(); acc[b] = new List<float>(); }
            LimitModel m = new LimitModel();
            foreach (string f in Directory.GetFiles(dir, "*.csv"))
            {
                try { if (AddLap(f, lat, brk, acc)) m.Laps++; }
                catch (Exception) { }
            }
            int total = lat.Sum(l => l.Count);
            m.Samples = total;
            int minBin = Math.Max(150, total / 300);
            List<float[]> latPts = new List<float[]>(), brkPts = new List<float[]>();
            List<float> av = new List<float>(), aa = new List<float>();
            for (int b = 0; b < nb; b++)
            {
                float v = (MinKmh + (b + 0.5f) * BinKmh) / 3.6f;
                if (lat[b].Count >= minBin) latPts.Add(new[] { v, Quantile(lat[b], GripQuantile) });
                if (brk[b].Count >= 60) brkPts.Add(new[] { v, Quantile(brk[b], GripQuantile) });
                if (acc[b].Count >= 60) { av.Add(v); aa.Add(Quantile(acc[b], 0.5f)); }
            }
            if (latPts.Count < 3 || brkPts.Count < 3 || av.Count < 3) return null;
            Fit(latPts, out m.LatA0, out m.LatK);
            Fit(brkPts, out m.BrkA0, out m.BrkK);
            m.AccV = av.ToArray();
            m.AccA = aa.ToArray();
            if (m.LatA0 <= 0.3f || m.BrkA0 <= 0.3f) return null;
            return m;
        }

        static bool AddLap(string file, List<float>[] lat, List<float>[] brk, List<float>[] acc)
        {
            List<float> spd = new List<float>(), gas = new List<float>(), brake = new List<float>(), la = new List<float>(), lo = new List<float>();
            List<int> outs = new List<int>();
            foreach (string line in File.ReadLines(file))
            {
                if (line.Length == 0 || line[0] == '#' || char.IsLetter(line[0])) continue;
                string[] p = line.Split(',');
                if (p.Length < 14) continue;
                spd.Add(float.Parse(p[2], Inv)); gas.Add(float.Parse(p[3], Inv)); brake.Add(float.Parse(p[4], Inv));
                la.Add(float.Parse(p[9], Inv)); lo.Add(float.Parse(p[10], Inv)); outs.Add(int.Parse(p[13], Inv));
            }
            int n = spd.Count;
            if (n < 4 * Window) return false;
            // prefix sums: window means and "anything wrong in the window" counts
            double[] sLat = new double[n + 1], sLon = new double[n + 1];
            int[] bad = new int[n + 1], notGas = new int[n + 1], notBrake = new int[n + 1];
            for (int i = 0; i < n; i++)
            {
                sLat[i + 1] = sLat[i] + la[i];
                sLon[i + 1] = sLon[i] + lo[i];
                bool b = outs[i] > 1 || Math.Abs(la[i]) > 3 || lo[i] < -2.6f || lo[i] > 1.3f;   // off track, crash, kerb spike
                bad[i + 1] = bad[i] + (b ? 1 : 0);
                notGas[i + 1] = notGas[i] + (gas[i] < 0.98f ? 1 : 0);
                notBrake[i + 1] = notBrake[i] + (brake[i] <= 0.5f ? 1 : 0);
            }
            int w = 2 * Window + 1;
            for (int i = Window; i < n - Window; i++)
            {
                int a = i - Window, z = i + Window + 1;
                if (bad[z] - bad[a] > 0 || spd[i] < MinKmh || spd[i] >= MaxKmh) continue;
                int b = (int)((spd[i] - MinKmh) / BinKmh);
                float mLat = (float)((sLat[z] - sLat[a]) / w), mLon = (float)((sLon[z] - sLon[a]) / w);
                lat[b].Add(Math.Abs(mLat));
                if (notBrake[z] - notBrake[a] == 0) brk[b].Add(-mLon);
                if (notGas[z] - notGas[a] == 0 && Math.Abs(mLat) < 0.3f) acc[b].Add(mLon);
            }
            return true;
        }

        static float Quantile(List<float> l, float q)
        {
            float[] a = l.ToArray();
            Array.Sort(a);
            return a[Math.Min(a.Length - 1, (int)(q * a.Length))];
        }

        // least squares a = a0 + k·v²  (k ≥ 0: downforce never takes grip away)
        static void Fit(List<float[]> pts, out float a0, out float k)
        {
            double n = pts.Count, sx = 0, sy = 0, sxx = 0, sxy = 0;
            foreach (float[] p in pts) { double x = p[0] * p[0]; sx += x; sy += p[1]; sxx += x * x; sxy += x * p[1]; }
            double kk = (n * sxy - sx * sy) / (n * sxx - sx * sx);
            if (double.IsNaN(kk) || kk < 0) kk = 0;
            k = (float)kk;
            a0 = (float)((sy - kk * sx) / n);
        }

        // ---------- the line ----------

        public class Line
        {
            public int N;
            public float[] K, DS;     // curvature [1/m] and real path length of each 2 m spline step [m]
        }

        // Curvature of the reference lap's driven line; null without car positions in the lap.
        public static Line LineOf(Trace tr)
        {
            int n = tr.N;
            if (tr.Filled < n - 5 || !Corners.HasPositions(tr)) return null;
            float[] x = Smooth(Smooth(tr.X, 4), 4), z = Smooth(Smooth(tr.Z, 4), 4);
            float[] h = new float[n], k = new float[n], ds = new float[n];
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                h[i] = (float)Math.Atan2(z[j] - z[i], x[j] - x[i]);
                ds[i] = (float)Math.Sqrt((x[j] - x[i]) * (x[j] - x[i]) + (z[j] - z[i]) * (z[j] - z[i]));
            }
            for (int i = 0; i < n; i++)
            {
                double dh = h[(i + 1) % n] - h[(i - 1 + n) % n];
                while (dh > Math.PI) dh -= 2 * Math.PI;
                while (dh < -Math.PI) dh += 2 * Math.PI;
                float seg = ds[(i - 1 + n) % n] + ds[i];
                k[i] = seg > 0.1f ? (float)Math.Abs(dh) / seg : 0;
            }
            k = Smooth(k, 3);
            // the lap's start and end are driven on slightly different lines: a fake kink at the seam
            int s = Math.Min(40, n / 10);
            float ks = Math.Min(k[n - s - 1], k[s]);
            for (int i = 0; i < s; i++) { k[i] = ks; k[n - 1 - i] = ks; }
            for (int i = 0; i < n; i++) if (ds[i] < 0.5f || ds[i] > 8f) ds[i] = Trace.Step;   // position glitches
            return new Line { N = n, K = k, DS = ds };
        }

        static float[] Smooth(float[] a, int w)
        {
            int n = a.Length;
            float[] r = new float[n];
            double sum = 0;
            for (int j = -w; j <= w; j++) sum += a[(j + n) % n];
            for (int i = 0; i < n; i++)
            {
                r[i] = (float)(sum / (2 * w + 1));
                sum += a[(i + w + 1) % n] - a[(i - w + n) % n];
            }
            return r;
        }

        // Speed [m/s] at every point of the line with grip and braking scaled by f (1 = the driver's own best).
        public float[] Simulate(Line line, float f)
        {
            int n = line.N;
            float[] lim = new float[n], v = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = 90f, k = line.K[i];
                if (k > 1e-5f)
                    for (int it = 0; it < 25; it++) s = Math.Min(90f, (float)Math.Sqrt(f * LatAt(s) * G / k));
                lim[i] = v[i] = s;
            }
            for (int pass = 0; pass < 3; pass++)   // the lap is a loop: a few passes settle the start
            {
                for (int i = 0; i < n; i++)
                {
                    int p = (i - 1 + n) % n;
                    float vp = v[p], ay = vp * vp * line.K[p];
                    float a = AccAt(vp) * G * Ellipse(ay, f * LatAt(vp) * G);
                    v[i] = Math.Min(v[i], Math.Min(lim[i], (float)Math.Sqrt(vp * vp + 2 * a * line.DS[p])));
                }
                for (int i = n - 1; i >= 0; i--)
                {
                    int q = (i + 1) % n;
                    float vq = v[q], ay = vq * vq * line.K[q];
                    float a = f * BrkAt(vq) * G * Ellipse(ay, f * LatAt(vq) * G);
                    v[i] = Math.Min(v[i], (float)Math.Sqrt(vq * vq + 2 * a * line.DS[i]));
                }
            }
            return v;
        }

        // grip left for speeding up / braking while cornering at ay (friction ellipse)
        static float Ellipse(float ay, float ayMax)
        {
            float r = ay / ayMax;
            return r >= 1 ? 0 : (float)Math.Sqrt(1 - r * r);
        }

        public string Describe()
        {
            return string.Format(Inv, "przyczepność {0:0.00} g, hamowanie {1:0.00} g (przy 100 km/h), {2} okr.",
                LatAt(100 / 3.6f), BrkAt(100 / 3.6f), Laps);
        }
    }
}
