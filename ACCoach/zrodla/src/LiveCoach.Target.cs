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
    // Lap time target: limit model and plan, rebuilt off the coaching thread.
    public partial class LiveCoach
    {
        // ---------- target lap time ----------
        LimitModel model;
        LimitModel.Line modelLine;
        bool modelTried;
        TargetPlan plan;              // the goal driven to now (the next intermediate step)
        TargetPlan finalPlan;         // the target itself
        int idealMs;
        int panelTargetPending = -1;
        Grip.TyreStats refTyres;      // tyre temperatures of the best lap: "cold" is colder than that
        bool tyresWereCold;
        bool cliTarget;

        // the limit model reads every saved lap: built off the coaching thread, swapped in between ticks
        void RebuildModel()
        {
            if (comboDir == null || refLap == null) return;
            string dir = comboDir;
            Trace tr = refLap.Trace;
            ThreadPool.QueueUserWorkItem(delegate
            {
                LimitModel m = null;
                LimitModel.Line line = null;
                try { m = LimitModel.Build(dir); line = LimitModel.LineOf(tr); } catch (Exception) { }
                Post(delegate
                {
                    if (dir != comboDir) return;
                    model = m;
                    modelLine = refLap != null && refLap.Trace != tr ? LimitModel.LineOf(refLap.Trace) : line;
                    modelTried = true;
                    RebuildPlan(true);
                });
            });
        }

        void RebuildPlan(bool announce)
        {
            plan = finalPlan = null;
            StStep = "";
            if (o.TargetMs <= 0) { StTarget = ""; return; }
            if (refLap == null) { StTarget = Lap.FormatTime(o.TargetMs) + L.T(" – czekam na wzorzec", " – waiting for a best lap"); return; }
            if (model == null || modelLine == null)
            {
                StTarget = Lap.FormatTime(o.TargetMs) + (modelTried ? L.T(" – za mało danych (przejedź kilka czystych okrążeń)", " – not enough data (drive a few clean laps)") : L.T(" – liczę model auta…", " – building the car model…"));
                return;
            }
            finalPlan = TargetPlan.Make(model, modelLine, refLap.Trace, refLap.LapMs, idealMs, o.TargetMs);
            List<int> steps = finalPlan.Steps();
            int step = steps.Count > 0 ? steps[0] : o.TargetMs;
            plan = step == o.TargetMs ? finalPlan : TargetPlan.Make(model, modelLine, refLap.Trace, refLap.LapMs, idealMs, step);
            StTarget = Lap.FormatTime(o.TargetMs) + " – " + finalPlan.Verdict();
            StStep = finalPlan.Reached ? L.T("cel osiągnięty", "target reached") : step != o.TargetMs ? L.T("teraz cel pośredni ", "now the intermediate goal ") + Lap.FormatTime(step) : L.T("teraz cel końcowy", "now the final target");
            if (!announce) return;
            Console.ForegroundColor = ConsoleColor.Yellow;
            foreach (string l in finalPlan.Describe(model, refCorners)) Console.WriteLine(l);
            if (!finalPlan.Reached) Console.WriteLine("  " + StStep + L.T(" – głos podaje stratę do niego po każdym zakręcie i na mecie", " – the voice gives the gap to it after every turn and at the line"));
            Console.ResetColor();
            if (!o.Quiet)
                Enqueue(finalPlan.Reached ? L.T("Cel osiągnięty", "Target reached") : step != o.TargetMs ? L.T("Cel pośredni ", "Intermediate goal ") + SpokenTime(step) : L.T("Cel ", "Target ") + SpokenTime(step), false);
        }
    }
}
