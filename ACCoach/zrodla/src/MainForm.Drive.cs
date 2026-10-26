using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ACCoach
{
    // Tab "Drive": status, lap time target, live voice and beep settings, the trainer log.
    public partial class MainForm : Form
    {
        // ---------- drive tab ----------

        void BuildDrive()
        {
            Panel left = new Panel { Dock = DockStyle.Left, Width = GroupW + 44, AutoScroll = true };
            int top = 8;

            GroupBox g = Group(left, L.T("Stan", "Status"), ref top);
            refLbl = Row(g, L.T("Wzorzec:", "Best lap:"));
            lastLbl = Row(g, L.T("Ostatnie:", "Last lap:"));
            focusLbl = Row(g, L.T("Fokus:", "Focus:"));
            End(g, ref top);

            g = Group(left, L.T("Cel czasu okrążenia", "Lap time target"), ref top);
            targetFor = new Label { AutoSize = false, Width = GroupW, Height = 18, ForeColor = SystemColors.GrayText, AutoEllipsis = true };
            Add(g, targetFor, 10, y); y += 22;
            targetBox = new TextBox { Width = 90 };
            Add(g, targetBox, 10, y);
            Button setT = new Button { Text = L.T("Ustaw cel", "Set target"), Width = 90, Height = 25 };
            Button offT = new Button { Text = L.T("Wyłącz", "Off"), Width = 70, Height = 25 };
            Add(g, setT, 106, y - 1);
            Add(g, offT, 202, y - 1);
            Add(g, Lbl(L.T("np. 1:35.000", "e.g. 1:35.000")), 280, y + 3);
            y += 30;
            targetLbl = new Label { AutoSize = false, Width = GroupW, Height = 34 };
            Add(g, targetLbl, 10, y); y += 36;
            stepLbl = new Label { AutoSize = false, Width = GroupW, Height = 18, Font = new Font(Font, FontStyle.Bold) };
            Add(g, stepLbl, 10, y); y += 22;
            setT.Click += delegate
            {
                int ms = TargetPlan.ParseTime(targetBox.Text);
                if (ms <= 0) { MessageBox.Show(this, L.T("Wpisz czas okrążenia, np. 1:35.000", "Type a lap time, e.g. 1:35.000"), "AC Coach"); return; }
                SetTarget(ms);
            };
            offT.Click += delegate { SetTarget(0); };
            targetBox.KeyDown += delegate (object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { setT.PerformClick(); e.SuppressKeyPress = true; } };
            End(g, ref top);

            g = Group(left, L.T("Głos i piknięcia – działają od razu, także w trakcie jazdy", "Voice and beeps – applied at once, also while driving"), ref top);
            trainChk = Check(g, L.T("Piknięcia do hamowania + uwagi o linii", "Braking beeps + racing line cues"));
            tipsChk = Check(g, L.T("Uwagi głosowe po zakrętach (wyłączone = tylko czasy i notatki)", "Voice tips after turns (off = lap times and notes only)"));
            focusChk = new CheckBox { Text = L.T("Fokus na jednym zakręcie", "Focus on one turn"), AutoSize = true };
            Add(g, focusChk, 10, y + 2);
            Add(g, Lbl(L.T("zakręt:", "turn:")), 220, y + 4);
            focusNum = new NumericUpDown { Minimum = 0, Maximum = 40, Width = 50 };
            Add(g, focusNum, 270, y + 1);
            Add(g, Lbl("0 = auto"), 326, y + 4);
            y += 30;
            muteChk = Check(g, L.T("Wycisz wszystko", "Mute everything"));
            Add(g, Lbl(L.T("Głos:", "Voice:")), 10, y + 4);
            voiceBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = GroupW - 110 };
            voiceBox.Items.Add(L.T("Automatycznie (pierwszy polski głos)", "Automatic (first English voice)"));
            voiceNames.Add("");
            List<string[]> voices = Voice.Installed();
            foreach (string[] v in voices)
            {
                voiceBox.Items.Add(v[0] + (v[1].Length > 0 ? "  (" + v[1] + ")" : ""));
                voiceNames.Add(v[0]);
            }
            Add(g, voiceBox, 120, y);
            y += 30;
            if (!voices.Exists(v => v[1].StartsWith("pl")))
            {
                Label warn = new Label { AutoSize = false, Width = GroupW, Height = 32, ForeColor = Color.DarkRed,
                    Text = L.T("Brak polskiego głosu. Dodaj: Ustawienia Windows > Czas i język > Mowa > Dodaj głosy > Polski.", "No English voice. Add one: Windows Settings > Time && language > Speech > Add voices > English.") };
                Add(g, warn, 10, y); y += 34;
            }
            volBar = Bar(g, L.T("Głośność głosu:", "Voice volume:"), 0, 100, out volLbl);
            beepVolBar = Bar(g, L.T("Głośność piknięć:", "Beep volume:"), 0, 100, out beepVolLbl);
            rateBar = Bar(g, L.T("Tempo mowy:", "Speech rate:"), -10, 10, out rateLbl);
            volBar.TickFrequency = beepVolBar.TickFrequency = 10;
            learnChk = Check(g, L.T("Ostatnie piknięcie: ucz się mojego czasu reakcji", "Last beep: learn my reaction time"));
            Add(g, Lbl(L.T("albo stale", "or fixed")), 30, y + 3);
            leadNum = new NumericUpDown { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05m, Width = 60 };
            Add(g, leadNum, 110, y);
            Add(g, Lbl(L.T("s przed punktem hamowania", "s before the braking point")), 175, y + 3);
            y += 32;
            testBtn = new Button { Text = L.T("Test głosu i piknięć", "Test voice and beeps"), Width = 160, Height = 27 };
            testBtn.Click += delegate { TestVoice(); };
            Add(g, testBtn, 10, y);
            y += 34;
            End(g, ref top);

            // the trainer's log, where the console used to be
            Panel right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 8, 8, 8) };
            Panel logBar = new Panel { Dock = DockStyle.Top, Height = 28 };
            logBar.Controls.Add(new Label { Text = L.T("Log trenera – czasy, uwagi głosowe, piknięcia, koła", "Trainer log – lap times, voice tips, beeps, wheels"), AutoSize = true, Location = new Point(0, 6), Font = new Font(Font, FontStyle.Bold) });
            followChk = new CheckBox { Text = L.T("przewijaj", "auto-scroll"), AutoSize = true, Checked = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Button clear = new Button { Text = L.T("Wyczyść", "Clear"), Width = 80, Height = 24, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            clear.Click += delegate { log.Clear(); };
            logBar.Controls.Add(followChk);
            logBar.Controls.Add(clear);
            logBar.Resize += delegate { clear.Location = new Point(logBar.Width - clear.Width, 1); followChk.Location = new Point(clear.Left - followChk.Width - 10, 5); };
            log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(24, 24, 27), ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None, WordWrap = false, DetectUrls = false };
            right.Controls.Add(log);
            right.Controls.Add(logBar);
            driveTab.Controls.Add(right);
            driveTab.Controls.Add(left);

            // live changes: straight to the running trainer, and remembered for the next start
            trainChk.CheckedChanged += delegate { Live(x => x.Train = trainChk.Checked, c => c.SetTrain(trainChk.Checked)); };
            tipsChk.CheckedChanged += delegate { Live(x => x.Quiet = !tipsChk.Checked, c => c.SetQuiet(!tipsChk.Checked)); };
            focusChk.CheckedChanged += delegate
            {
                focusNum.Enabled = focusChk.Checked;
                Live(x => { x.Focus = focusChk.Checked; x.FocusCorner = (int)focusNum.Value; }, c => c.SetFocusMode(focusChk.Checked, (int)focusNum.Value));
            };
            focusNum.ValueChanged += delegate { if (focusChk.Checked) Live(x => x.FocusCorner = (int)focusNum.Value, c => c.SetFocusMode(true, (int)focusNum.Value)); else Remember(); };
            muteChk.CheckedChanged += delegate { Live(x => x.Mute = muteChk.Checked, c => c.SetMute(muteChk.Checked)); };
            voiceBox.SelectedIndexChanged += delegate { string n = voiceNames[Math.Max(0, voiceBox.SelectedIndex)]; Live(x => x.VoiceName = n.Length > 0 ? n : null, c => c.SetVoice(n)); };
            volBar.ValueChanged += delegate { BarLabels(); Live(x => x.Volume = volBar.Value, c => c.SetVolume(volBar.Value)); };
            beepVolBar.ValueChanged += delegate { BarLabels(); Live(x => x.BeepVolume = beepVolBar.Value, c => c.SetBeepVolume(beepVolBar.Value)); };
            rateBar.ValueChanged += delegate { BarLabels(); Live(x => x.Rate = rateBar.Value, c => c.SetRate(rateBar.Value)); };
            learnChk.CheckedChanged += delegate { leadNum.Enabled = !learnChk.Checked; SendLead(); };
            leadNum.ValueChanged += delegate { if (!learnChk.Checked) SendLead(); };
        }

        void SendLead()
        {
            float lead = learnChk.Checked ? float.NaN : (float)leadNum.Value;
            Live(x => x.BeepLead = lead, c => c.SetBeepLead(lead));
        }

        void Live(Action<Options> direct, Action<LiveCoach> live)
        {
            if (loading) return;
            if (Running) live(coach); else direct(o);
            Remember();
        }

        void Remember()
        {
            if (loading) return;
            st.Train = trainChk.Checked;
            st.Quiet = !tipsChk.Checked;
            st.Focus = focusChk.Checked;
            st.FocusCorner = (int)focusNum.Value;
            st.Mute = muteChk.Checked;
            st.VoiceName = voiceNames[Math.Max(0, voiceBox.SelectedIndex)];
            st.Volume = volBar.Value;
            st.BeepVolume = beepVolBar.Value;
            st.Rate = rateBar.Value;
            st.LearnLead = learnChk.Checked;
            st.BeepLead = (float)leadNum.Value;
            st.OpenMap = mapChk.Checked;
            st.AutoStart = autoChk.Checked;
            saveDue = true;   // written by Tick, so dragging a slider does not write the file 50 times
        }

        void Save()
        {
            saveDue = false;
            try { st.Save(); } catch (Exception e) { Console.WriteLine(L.T("Nie udało się zapisać ustawień: ", "Could not save the settings: ") + e.Message); }
        }

        void BarLabels()
        {
            volLbl.Text = volBar.Value + "%";
            beepVolLbl.Text = beepVolBar.Value + "%";
            rateLbl.Text = (rateBar.Value > 0 ? "+" : "") + rateBar.Value;
        }

        // target: for the car + track being driven, or (trainer stopped) the one driven last
        void SetTarget(int ms)
        {
            shownTarget = ms;
            targetBox.Text = ms > 0 ? Lap.FormatTime(ms) : "";
            if (Running) { coach.SetTarget(ms); return; }
            string combo = Analyzer.NewestCombo(DataDir());
            if (combo == null) { MessageBox.Show(this, L.T("Najpierw przejedź kilka okrążeń – cel ustawia się dla konkretnego auta i toru.", "Drive a few laps first – the target is set for a particular car and track."), "AC Coach"); return; }
            TargetPlan.SaveTarget(combo, ms);
            Console.WriteLine(ms > 0 ? L.T("Cel czasu ", "Lap time target ") + Lap.FormatTime(ms) + L.T(" dla ", " for ") + ComboName(combo) + L.T(" – trener oceni go po starcie.", " – the trainer will judge it after the start.") : L.T("Cel czasu wyłączony dla ", "Lap time target off for ") + ComboName(combo));
        }

        static string ComboName(string dir) { return Path.GetFileName(dir).Replace("__", " | "); }

        void TestVoice()
        {
            Options t = new Options { Mute = muteChk.Checked, Rate = rateBar.Value, Volume = volBar.Value, BeepVolume = beepVolBar.Value,
                VoiceName = voiceNames[Math.Max(0, voiceBox.SelectedIndex)] };
            if (t.VoiceName.Length == 0) t.VoiceName = null;
            testBtn.Enabled = false;
            new Thread(delegate ()
            {
                try { Program.VoiceTest(t); } catch (Exception e) { Console.WriteLine(L.T("Test głosu: ", "Voice test: ") + e.Message); }
                try { BeginInvoke((MethodInvoker)delegate { testBtn.Enabled = true; }); } catch (Exception) { }
            }) { IsBackground = true }.Start();
        }
    }
}
