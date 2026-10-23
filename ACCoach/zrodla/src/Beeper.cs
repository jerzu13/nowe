using System;
using System.IO;
using System.Media;
using System.Text;

namespace ACCoach
{
    // Low-latency countdown beeps for braking points (speech is too slow to start for "brake now").
    public sealed class Beeper : IDisposable
    {
        // final beep tells how hard to brake: [0] light = two low taps, [1] medium = mid tone, [2] full = long high tone
        SoundPlayer shortBeep;
        SoundPlayer[] finalBeep = new SoundPlayer[3];
        public volatile bool Muted;   // live switch from the panel window (beeps off / mute all)

        public Beeper(bool mute, int volume = 100)
        {
            Muted = mute;
            SetVolume(volume);
        }

        // 0–100 %; rebuilds the tones at the new amplitude.
        public void SetVolume(int volume)
        {
            double a = Math.Max(0, Math.Min(100, volume)) / 100.0;
            SoundPlayer s = new SoundPlayer(Tone(880, 70, amp: a));
            SoundPlayer[] f = {
                new SoundPlayer(Tone(587, 60, 50, 60, a)),
                new SoundPlayer(Tone(1047, 110, amp: a)),
                new SoundPlayer(Tone(1320, 220, amp: a)) };
            s.Load();
            foreach (SoundPlayer p in f) p.Load();
            SoundPlayer oldS = shortBeep;
            SoundPlayer[] oldF = finalBeep;
            shortBeep = s;
            finalBeep = f;
            if (oldS != null) oldS.Dispose();
            foreach (SoundPlayer p in oldF) if (p != null) p.Dispose();
        }

        // force: 0 light, 1 medium, 2 full (only used for the final beep)
        public void Beep(bool final, int force = 2)
        {
            if (Muted) return;
            SoundPlayer p = final ? finalBeep[Math.Max(0, Math.Min(2, force))] : shortBeep;
            if (p != null) p.Play();
        }

        // 16-bit mono PCM sine with 5 ms fade in/out to avoid clicks; optional gap + second tone of the same pitch.
        static MemoryStream Tone(double hz, int ms, int gapMs = 0, int ms2 = 0, double amp = 1.0)
        {
            const int rate = 44100;
            int n1 = rate * ms / 1000, gap = rate * gapMs / 1000, n2 = rate * ms2 / 1000, n = n1 + gap + n2, fade = rate * 5 / 1000;
            MemoryStream m = new MemoryStream();
            BinaryWriter w = new BinaryWriter(m, Encoding.ASCII);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            for (int i = 0; i < n; i++)
            {
                int j = i < n1 ? i : i - n1 - gap, len = i < n1 ? n1 : n2;
                double env = (i >= n1 && j < 0) ? 0 : Math.Min(1.0, Math.Min(j, len - 1 - j) / (double)fade);
                w.Write((short)(Math.Sin(2 * Math.PI * hz * i / rate) * 30000 * amp * env));
            }
            w.Flush();
            m.Position = 0;
            return m;
        }

        public void Dispose()
        {
            if (shortBeep != null) shortBeep.Dispose();
            foreach (SoundPlayer p in finalBeep) if (p != null) p.Dispose();
        }
    }
}
