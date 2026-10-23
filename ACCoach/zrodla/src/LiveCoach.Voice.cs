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
    // What the voice says after each turn and when.
    public partial class LiveCoach
    {
        void Enqueue(string text, bool front)
        {
            Msg m = new Msg(); m.Text = text; m.At = DateTime.Now;
            if (front) queue.Insert(0, m); else queue.Add(m);
            while (queue.Count > 3) queue.RemoveAt(queue.Count - 1);
        }

        void ProcessVoice()
        {
            float d = live != null ? live.FilledDistance : -1;
            if (live != null && refCorners != null && curFromLine)
            {
                if (o.Train) Training(d);
                while (nextNote < refCorners.Count && d >= refCorners[nextNote].OnsetD - 300)
                {
                    Corner c = refCorners[nextNote];
                    string note;
                    if (notes.TryGetValue(c.Num, out note) && d < c.OnsetD - 120 && SpeakAbout(c.Num)) Enqueue(note, false);
                    nextNote++;
                }
                if (o.Focus && focusNum > 0 && !focusReminded && !o.Quiet)
                {
                    Corner fc = refCorners.FirstOrDefault(c => c.Num == focusNum);
                    if (fc != null && d >= fc.OnsetD - 350 && d < fc.OnsetD - 100)
                    {
                        focusReminded = true;
                        Enqueue(focusTip != null ? string.Format(L.T("Zakręt {0}: {1}", "Turn {0}: {1}"), focusNum, focusTip) : string.Format(L.T("Fokus, zakręt {0}", "Focus, turn {0}"), focusNum), true);
                    }
                }
                if (rival != null && sectorBounds != null)
                    while (nextSector < sectorBounds.Length && d >= sectorBounds[nextSector])
                    {
                        float ts = live.TimeAt(sectorBounds[nextSector]);
                        SectorDone(nextSector, ts - sectorStartS, true);
                        sectorStartS = ts;
                        nextSector++;
                    }
                // judge each corner soon after the exit (60 m past full throttle), not at the end of its segment
                while (nextEval < refCorners.Count && d >= EvalD(refCorners[nextEval]))
                {
                    int k = nextEval;
                    Corner rc = refCorners[k];
                    float evalD = EvalD(rc);
                    // better/worse: against the best lap; what to fix: against the best version of this corner
                    CornerCmp cmp = Corners.Compare(refLap.Trace, refCorners, k, live, evalD);
                    bool ownTarget = targetTrace[k] != refLap.Trace;
                    CornerCmp tcmp = ownTarget ? Corners.Compare(targetTrace[k], targetCorners, k, live, evalD) : cmp;
                    Tip tip = Corners.MakeTip(tcmp, o.Train);
                    float total = live.TimeAt(evalD) - refLap.Trace.TimeAt(evalD);
                    List<WheelEvent> wev = CornerWheelEvents(rc.StartD - 30, evalD);
                    string wheelTxt = wev.Count > 0 ? L.T(" | koła: ", " | wheels: ") + string.Join(", ", wev.Select(w => Grip.Name(w.Kind) + string.Format(Inv, " {0:0.00} s", w.Dur))) : "";
                    string wheelKind = wev.Count > 0 ? Grip.Kinds.First(k2 => wev.Any(w => w.Kind == k2)) : null;
                    float vsPlan = float.NaN;
                    string planHint = null, planTxt = "";
                    if (plan != null)
                    {
                        vsPlan = (live.TimeAt(evalD) - live.TimeAt(rc.StartD)) - (plan.TimeAt(evalD) - plan.TimeAt(rc.StartD));
                        float faster = plan.MinSpeed(rc.StartD, evalD) - LiveMinSpeed(rc.StartD, evalD);
                        if (faster >= 4 && vsPlan >= 0.05f) planHint = string.Format(Inv, L.T("szczyt szybciej o {0:0}", "apex faster by {0:0}"), faster);
                        planTxt = string.Format(Inv, L.T(" | cel {0} (łącznie {1}){2}", " | target {0} (total {1}){2}"), Corners.Sec2(vsPlan), Corners.Sec2(live.TimeAt(evalD) - plan.TimeAt(evalD)),
                            planHint != null ? ", " + planHint + " km/h" : "");
                    }
                    Console.WriteLine(string.Format(Inv, L.T("  Z{0,-2} {1,6} s | hamowanie {2,6} siła {6,5} | szczyt {3,4:+0;-0;0} km/h | gaz {4,6} | linia szczyt {5} | łącznie {7}{8}", "  T{0,-2} {1,6} s | braking {2,6} force {6,5} | apex {3,4:+0;-0;0} km/h | throttle {4,6} | line apex {5} | total {7}{8}"),
                        cmp.Ref.Num, Corners.Sec2(cmp.SegDelta), Metres(tcmp.BrakeDiff), tcmp.MinSpdDiff, Metres(tcmp.ThrDiff), LineText(cmp.ApexOff), Pct(tcmp.BrakeForceDiff), Corners.Sec2(total), planTxt + wheelTxt));
                    if (o.Train && targetCorners[k].Braked) LogBeep(k, tcmp);
                    DecayMargin(k);
                    if (!o.Quiet)
                    {
                        string call = CornerCall(rc.Num, cmp.SegDelta, ownTarget ? tcmp.SegDelta : cmp.SegDelta, tip, vsPlan, planHint, wheelKind);
                        if (o.Focus && rc.Num == focusNum) Enqueue(FocusCall(k, cmp, call, tip), false);
                        else if (SpeakAbout(rc.Num)) Enqueue(call, false);
                    }
                    nextEval++;
                }
            }
            if (queue.Count == 0) return;
            if ((DateTime.Now - queue[0].At).TotalSeconds > 8) { queue.RemoveAt(0); return; }
            // tips are spoken right away, anywhere on track; the beeps simply play over the voice
            if (voice.Busy || (DateTime.Now - lastSpoke).TotalSeconds < 1) return;
            voice.Say(queue[0].Text);
            queue.RemoveAt(0);
            lastSpoke = DateTime.Now;
        }

        // "Zakręt 3, lepiej o 0,12" / "Zakręt 3, gorzej o 0,2, hamujesz za wcześnie" / "Zakręt 3, równo".
        // vsBest = against the best lap; vsTarget = against the best version of this corner (record / what to fix).
        // vsBest: against the best lap; vsTarget: against the best run of this corner; vsPlan: against the
        // lap-time target's plan (NaN = no target)
        static string CornerCall(int num, float vsBest, float vsTarget, Tip tip, float vsPlan = float.NaN, string planHint = null, string wheel = null)
        {
            string s = L.T("Zakręt ", "Turn ") + num + ", ";
            if (vsBest <= -0.03f) s += L.T("lepiej o ", "faster by ") + SpokenDelta(-vsBest);
            else if (vsBest >= 0.03f) s += L.T("gorzej o ", "slower by ") + SpokenDelta(vsBest);
            else s += L.T("równo", "even");
            bool tipSaid = false;
            if (vsTarget < -0.02f && vsBest < 0.03f) s += L.T(", rekord zakrętu", ", turn record");
            else if (tip != null && vsTarget > 0.05f) { s += ", " + tip.Action; tipSaid = true; }
            // what the tyres did: a locked wheel always, the rest when the corner cost time
            bool lost = vsBest >= 0.05f || vsTarget > 0.05f || (!float.IsNaN(vsPlan) && vsPlan >= 0.05f);
            if (wheel != null && !(wheel == "over" && tipSaid && (tip.Action.Contains("uślizg") || tip.Action.Contains("rear sliding"))) && (wheel.StartsWith("lock") || (lost && !tipSaid)))
            {
                s += ", " + Grip.Spoken(wheel);
                tipSaid = true;
            }
            if (!float.IsNaN(vsPlan))
            {
                if (vsPlan <= 0.02f) s += L.T(", w celu", ", on target");
                else if (vsPlan >= 0.05f)
                {
                    s += L.T(", do celu ", ", to target ") + SpokenDelta(vsPlan);
                    if (planHint != null && !tipSaid) s += ", " + planHint;
                }
            }
            return s;
        }

        // wheel events of the current lap between two distances (scans back from the newest sample)
        List<WheelEvent> CornerWheelEvents(float from, float to)
        {
            List<Sample> s = cur.Samples;
            Func<int, float> d = i => { float p = s[i].Pos; if (s[i].T < 5000 && p > 0.5f) p -= 1; return p * length; };
            int a = s.Count - 1;
            while (a > 0 && d(a) > from) a--;
            int b = s.Count;
            while (b > a && d(b - 1) > to) b--;
            return Grip.Detect(s, a, b, d);
        }

        float LiveMinSpeed(float from, float to)
        {
            int a = Math.Max(0, (int)(from / Trace.Step)), b = Math.Min(live.Filled - 1, (int)(to / Trace.Step));
            float m = float.MaxValue;
            for (int i = a; i <= b; i++) m = Math.Min(m, live.Spd[i]);
            return m == float.MaxValue ? 0 : m;
        }

        static string SpokenDelta(float s)
        {
            return s.ToString(s < 0.1f ? "0.00" : "0.0", L.Culture);
        }

        static float EvalD(Corner c)
        {
            return Math.Min(c.EndD, (float.IsNaN(c.ThrottleD) ? c.ApexD + 40 : c.ThrottleD) + 60);
        }
    }
}
