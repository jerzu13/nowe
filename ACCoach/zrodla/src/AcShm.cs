using System;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace ACCoach
{
    // One snapshot of the data the game publishes in shared memory.
    public class Telemetry
    {
        public int PhysicsPacket, GraphicsPacket;
        public int Status, Session, CompletedLaps;
        public int CurrentTimeMs, LastTimeMs, BestTimeMs;
        public int IsInPit, IsInPitLane, Sector;
        public float Pos, X, Z;
        public float Gas, Brake, Clutch, Steer, SpeedKmh, LatG, LonG;
        public int Gear, Rpm, TyresOut;
        public float[] Ext = new float[Wheels.N];   // wheels and tyres, see Wheels
    }

    public class StaticInfo
    {
        public string Car = "", Track = "", Config = "";
        public float SplineLength;
        public int SectorCount, MaxRpm;
    }

    // Reads Assetto Corsa shared memory pages (acpmf_physics / acpmf_graphics / acpmf_static).
    // Offsets follow the official AC shared memory layout (structs packed to 4 bytes, wchar = 2 bytes).
    public sealed class AcShm : IDisposable
    {
        public const int STATUS_OFF = 0, STATUS_REPLAY = 1, STATUS_LIVE = 2, STATUS_PAUSE = 3;

        MemoryMappedFile phys, gfx, stat;
        MemoryMappedViewAccessor pa, ga, sa;
        readonly byte[] pbuf = new byte[800];
        readonly byte[] gbuf = new byte[300];
        readonly byte[] sbuf = new byte[600];

        // prefix "acpmf" = the game; "acpmf_test" = the test tools (AcSim, AcReplay), selected with --test-shm
        public static AcShm TryOpen(string prefix = "acpmf")
        {
            AcShm s = new AcShm();
            try
            {
                s.phys = MemoryMappedFile.OpenExisting(@"Local\" + prefix + "_physics", MemoryMappedFileRights.Read);
                s.gfx = MemoryMappedFile.OpenExisting(@"Local\" + prefix + "_graphics", MemoryMappedFileRights.Read);
                s.stat = MemoryMappedFile.OpenExisting(@"Local\" + prefix + "_static", MemoryMappedFileRights.Read);
                s.pa = s.phys.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                s.ga = s.gfx.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                s.sa = s.stat.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                return s;
            }
            catch (Exception)
            {
                s.Dispose();
                return null;
            }
        }

        static void Fill(MemoryMappedViewAccessor a, byte[] buf)
        {
            int n = (int)Math.Min(buf.Length, a.Capacity);
            a.ReadArray(0, buf, 0, n);
        }

        static int I(byte[] b, int off) { return BitConverter.ToInt32(b, off); }
        static float F(byte[] b, int off) { return BitConverter.ToSingle(b, off); }

        static string S(byte[] b, int off, int chars)
        {
            string s = Encoding.Unicode.GetString(b, off, chars * 2);
            int z = s.IndexOf('\0');
            return (z >= 0 ? s.Substring(0, z) : s).Trim();
        }

        public Telemetry Read()
        {
            Fill(pa, pbuf);
            Fill(ga, gbuf);
            Telemetry t = new Telemetry();
            // physics page
            t.PhysicsPacket = I(pbuf, 0);
            t.Gas = F(pbuf, 4);
            t.Brake = F(pbuf, 8);
            t.Gear = I(pbuf, 16) - 1;          // AC: 0 = R, 1 = N, 2 = 1st  ->  -1 / 0 / 1
            t.Rpm = I(pbuf, 20);
            t.Steer = F(pbuf, 24);
            t.SpeedKmh = F(pbuf, 28);
            t.LatG = F(pbuf, 44);              // accG[0]
            t.LonG = F(pbuf, 52);              // accG[2]
            t.TyresOut = I(pbuf, 61 * 4);
            t.Clutch = F(pbuf, 91 * 4);
            // wheels: FL, FR, RL, RR (slip ratio/angle and TC/ABS activity need AC 1.14+, older versions read 0)
            float[] e = t.Ext;
            e[Wheels.Yaw] = F(pbuf, 300);           // localAngularVel[1]
            for (int w = 0; w < 4; w++)
            {
                e[Wheels.SlipRatio + w] = F(pbuf, 640 + 4 * w);
                e[Wheels.SlipAngle + w] = F(pbuf, 656 + 4 * w);
                e[Wheels.Load + w] = F(pbuf, 72 + 4 * w);
                e[Wheels.Core + w] = F(pbuf, 152 + 4 * w);
                e[Wheels.Pressure + w] = F(pbuf, 88 + 4 * w);
                e[Wheels.BrakeTemp + w] = F(pbuf, 348 + 4 * w);
            }
            e[Wheels.Tc] = F(pbuf, 672);
            e[Wheels.Abs] = F(pbuf, 676);
            // graphics page
            t.GraphicsPacket = I(gbuf, 0);
            t.Status = I(gbuf, 4);
            t.Session = I(gbuf, 8);
            t.CompletedLaps = I(gbuf, 132);
            t.CurrentTimeMs = I(gbuf, 140);
            t.LastTimeMs = I(gbuf, 144);
            t.BestTimeMs = I(gbuf, 148);
            t.IsInPit = I(gbuf, 160);
            t.Sector = I(gbuf, 164);
            t.Pos = F(gbuf, 248);
            t.X = F(gbuf, 252);
            t.Z = F(gbuf, 260);
            t.IsInPitLane = I(gbuf, 276);
            return t;
        }

        public StaticInfo ReadStatic()
        {
            Fill(sa, sbuf);
            StaticInfo si = new StaticInfo();
            si.Car = S(sbuf, 68, 33);
            si.Track = S(sbuf, 134, 33);
            si.SectorCount = I(sbuf, 400);
            si.MaxRpm = I(sbuf, 412);
            si.SplineLength = F(sbuf, 520);
            si.Config = S(sbuf, 524, 33);
            return si;
        }

        public void Dispose()
        {
            if (pa != null) pa.Dispose();
            if (ga != null) ga.Dispose();
            if (sa != null) sa.Dispose();
            if (phys != null) phys.Dispose();
            if (gfx != null) gfx.Dispose();
            if (stat != null) stat.Dispose();
        }
    }
}
