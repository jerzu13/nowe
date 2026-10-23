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
    // Live changes from the AC Coach window: posted by the UI thread, applied by the coaching loop.
    public partial class LiveCoach
    {
        // ---------- live changes from the panel window ----------
        // The panel runs on its own thread; it posts changes that the coaching loop applies between ticks.
        readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();
        public volatile string StState = "Start…", StCombo = "", StRef = "", StLast = "", StFocus = "";
        public Options Opt { get { return o; } }
        public bool Running { get { return !stop; } }

        public volatile string StTarget = "", StStep = "";

        public void Post(Action a) { pending.Enqueue(a); }

        // target lap time from the panel; 0 = off. Saved per car + track in <combo>\cel.txt.
        public void SetTarget(int ms)
        {
            Post(delegate
            {
                o.TargetMs = ms;
                if (comboDir == null) panelTargetPending = ms;
                else TargetPlan.SaveTarget(comboDir, ms);
                Console.WriteLine(ms > 0 ? L.T("[PANEL] Cel czasu: ", "[SET] Lap time target: ") + Lap.FormatTime(ms) : L.T("[PANEL] Cel czasu wyłączony", "[SET] Lap time target off"));
                RebuildPlan(true);
            });
        }

        public void Stop() { stop = true; }

        public void SetTrain(bool on)
        {
            Post(delegate
            {
                o.Train = on;
                beeper.Muted = o.Mute || !on;
                Console.WriteLine(on ? L.T("[PANEL] Piknięcia do hamowania WŁĄCZONE", "[SET] Braking beeps ON") : L.T("[PANEL] Piknięcia do hamowania WYŁĄCZONE", "[SET] Braking beeps OFF"));
            });
        }

        public void SetQuiet(bool quiet)
        {
            Post(delegate { o.Quiet = quiet; Console.WriteLine(quiet ? L.T("[PANEL] Tryb cichy: tylko czasy i notatki", "[SET] Quiet: lap times and notes only") : L.T("[PANEL] Uwagi głosowe po zakrętach WŁĄCZONE", "[SET] Voice tips after turns ON")); });
        }

        public void SetFocusMode(bool on, int corner)
        {
            Post(delegate
            {
                o.Focus = on;
                o.FocusCorner = corner;
                focusNum = 0; focusStreak = 0; focusTip = null; focusReminded = false;
                focusDone.Clear();
                StFocus = on ? L.T("wybieram…", "choosing…") : "";
                if (!on) Console.WriteLine(L.T("[PANEL] Fokus WYŁĄCZONY – uwagi o wszystkich zakrętach", "[SET] Focus OFF – tips for all turns"));
                else if (refCorners != null && refCorners.Count > 0) ChooseFocus(true);
                else Console.WriteLine(L.T("[PANEL] Fokus włączy się, gdy będzie wzorzec", "[SET] Focus starts once there is a best lap"));
            });
        }

        public void SetMute(bool mute)
        {
            Post(delegate { o.Mute = mute; voice.Muted = mute; beeper.Muted = mute || !o.Train; Console.WriteLine(mute ? L.T("[PANEL] Wyciszone", "[SET] Muted") : L.T("[PANEL] Dźwięk włączony", "[SET] Sound on")); });
        }

        public void SetVoice(string name) { Post(delegate { o.VoiceName = name; voice.SetVoice(name); Console.WriteLine(L.T("[PANEL] Głos: ", "[SET] Voice: ") + voice.VoiceName); }); }
        public void SetRate(int rate) { Post(delegate { o.Rate = rate; voice.SetRate(rate); }); }
        public void SetVolume(int vol) { Post(delegate { o.Volume = vol; voice.SetVolume(vol); }); }
        public void SetBeepVolume(int vol) { Post(delegate { o.BeepVolume = vol; beeper.SetVolume(vol); }); }

        public void SetBeepLead(float lead)   // NaN = learn from the reactions
        {
            Post(delegate { o.BeepLead = lead; if (comboDir != null) UpdateBeepLead(true); });
        }
    }
}
