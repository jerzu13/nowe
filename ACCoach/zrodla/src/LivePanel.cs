using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ACCoach
{
    // Window shown next to the console while the trainer runs: current state plus settings that apply at once
    // (beeps, voice tips, focus, mute, voice, volumes, beep lead). Closing it ends the trainer.
    public class LivePanel : LayoutForm
    {
        [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);

        static LivePanel open;
        static Thread thread;

        public static void Start(LiveCoach coach, Settings settings)
        {
            thread = new Thread(delegate ()
            {
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    open = new LivePanel(coach, settings);
                    Application.Run(open);
                }
                catch (Exception e) { Console.WriteLine("Okno ustawień niedostępne: " + e.Message); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        // the trainer stopped (Ctrl+C, end of session with --once): close the window too
        public static void CloseIfOpen()
        {
            LivePanel p = open;
            if (p == null) return;
            try { p.BeginInvoke((MethodInvoker)delegate { p.closingFromCoach = true; p.Close(); }); } catch (Exception) { }
            if (thread != null) thread.Join(2000);
        }

        readonly LiveCoach coach;
        readonly Settings st;
        readonly List<string> voiceNames = new List<string>();
        bool closingFromCoach, loading = true;

        Label stateLbl, comboLbl, refLbl, lastLbl, focusLbl, rateLbl, volLbl, beepVolLbl;
        CheckBox trainChk, tipsChk, focusChk, muteChk, learnChk, topChk, consoleChk;
        NumericUpDown focusNum, leadNum;
        ComboBox voiceBox;
        TrackBar rateBar, volBar, beepVolBar;

        LivePanel(LiveCoach coach, Settings settings)
        {
            this.coach = coach;
            st = settings;
            W = 440;
            Options o = coach.Opt;
            Text = "AC Coach – na żywo";
            MinimizeBox = true;
            StartPosition = FormStartPosition.Manual;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int top = 10;

            // --- state
            GroupBox g = Group("Stan", ref top);
            stateLbl = Lbl("Start…");
            stateLbl.Font = new Font(Font, FontStyle.Bold);
            Add(g, stateLbl, 10, y); y += 22;
            comboLbl = Lbl("");
            Add(g, comboLbl, 10, y); y += 22;
            refLbl = Row(g, "Wzorzec:");
            lastLbl = Row(g, "Ostatnie:");
            focusLbl = Row(g, "Fokus:");
            End(g, ref top);

            // --- live settings
            g = Group("Ustawienia – działają od razu", ref top);
            trainChk = Check(g, "Piknięcia do hamowania + uwagi o linii", o.Train);
            tipsChk = Check(g, "Uwagi głosowe po zakrętach (wyłączone = tylko czasy i notatki)", !o.Quiet);
            focusChk = new CheckBox { Text = "Fokus na jednym zakręcie", AutoSize = true, Checked = o.Focus };
            Add(g, focusChk, 10, y + 2);
            Add(g, Lbl("zakręt:"), 220, y + 4);
            focusNum = new NumericUpDown { Minimum = 0, Maximum = 40, Width = 50, Value = Math.Min(40, Math.Max(0, o.FocusCorner)) };
            Add(g, focusNum, 270, y + 1);
            Add(g, Lbl("0 = auto"), 326, y + 4);
            y += 30;
            muteChk = Check(g, "Wycisz wszystko", o.Mute);

            Add(g, Lbl("Głos:"), 10, y + 4);
            voiceBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = W - 120 };
            voiceBox.Items.Add("Automatycznie (pierwszy polski głos)");
            voiceNames.Add("");
            foreach (string[] v in Voice.Installed())
            {
                voiceBox.Items.Add(v[0] + (v[1].Length > 0 ? "  (" + v[1] + ")" : ""));
                voiceNames.Add(v[0]);
            }
            int vi = voiceNames.IndexOf(o.VoiceName ?? "");
            voiceBox.SelectedIndex = vi < 0 ? 0 : vi;
            Add(g, voiceBox, 120, y);
            y += 32;
            volBar = Bar(g, "Głośność głosu:", 0, 100, out volLbl);
            volBar.TickFrequency = 10;
            beepVolBar = Bar(g, "Głośność piknięć:", 0, 100, out beepVolLbl);
            beepVolBar.TickFrequency = 10;
            rateBar = Bar(g, "Tempo mowy:", -10, 10, out rateLbl);
            volBar.Value = o.Volume;
            beepVolBar.Value = o.BeepVolume;
            rateBar.Value = Math.Max(-10, Math.Min(10, o.Rate));

            learnChk = Check(g, "Ostatnie piknięcie: ucz się mojego czasu reakcji", float.IsNaN(o.BeepLead));
            Add(g, Lbl("albo stale"), 30, y + 3);
            leadNum = new NumericUpDown { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05m, Width = 60,
                Value = float.IsNaN(o.BeepLead) ? (decimal)Math.Round(st.BeepLead, 2) : (decimal)Math.Round(Math.Max(0f, Math.Min(1f, o.BeepLead)), 2) };
            Add(g, leadNum, 110, y);
            Add(g, Lbl("s przed punktem hamowania"), 175, y + 3);
            y += 32;
            End(g, ref top);

            // --- window
            topChk = new CheckBox { Text = "Zawsze na wierzchu", AutoSize = true };
            consoleChk = new CheckBox { Text = "Pokaż okno z logiem", AutoSize = true, Checked = true };
            Add(this, topChk, 16, top);
            Add(this, consoleChk, 200, top);
            top += 28;

            Button save = new Button { Text = "Zapisz jako domyślne", Width = 150, Height = 30 };
            Button analyze = new Button { Text = "Analiza sesji", Width = 105, Height = 30 };
            Button folder = new Button { Text = "Folder danych", Width = 105, Height = 30 };
            Button end = new Button { Text = "Zakończ", Width = 80, Height = 30 };
            save.Click += delegate { SaveDefaults(); };
            analyze.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "analyze --pause") { UseShellExecute = true }); } catch (Exception) { }
            };
            folder.Click += delegate
            {
                try { Process.Start("explorer.exe", "\"" + coach.Opt.DataDir + "\""); } catch (Exception) { }
            };
            end.Click += delegate { Close(); };
            int x = 12;
            foreach (Button b in new[] { save, analyze, folder, end })
            {
                Add(this, b, x, top);
                x += b.Width + 6;
            }
            ClientSize = new Size(W + 20 + 24, top + 42);
            Location = new Point(Math.Max(wa.Left, wa.Right - Width - 10), wa.Top + 10);

            UpdateLabels();
            focusNum.Enabled = focusChk.Checked;
            leadNum.Enabled = !learnChk.Checked;

            // live changes -> trainer
            trainChk.CheckedChanged += delegate { if (!loading) coach.SetTrain(trainChk.Checked); };
            tipsChk.CheckedChanged += delegate { if (!loading) coach.SetQuiet(!tipsChk.Checked); };
            focusChk.CheckedChanged += delegate { focusNum.Enabled = focusChk.Checked; if (!loading) coach.SetFocusMode(focusChk.Checked, (int)focusNum.Value); };
            focusNum.ValueChanged += delegate { if (!loading && focusChk.Checked) coach.SetFocusMode(true, (int)focusNum.Value); };
            muteChk.CheckedChanged += delegate { if (!loading) coach.SetMute(muteChk.Checked); };
            voiceBox.SelectedIndexChanged += delegate { if (!loading) coach.SetVoice(voiceNames[Math.Max(0, voiceBox.SelectedIndex)]); };
            volBar.ValueChanged += delegate { UpdateLabels(); if (!loading) coach.SetVolume(volBar.Value); };
            beepVolBar.ValueChanged += delegate { UpdateLabels(); if (!loading) coach.SetBeepVolume(beepVolBar.Value); };
            rateBar.ValueChanged += delegate { UpdateLabels(); if (!loading) coach.SetRate(rateBar.Value); };
            learnChk.CheckedChanged += delegate { leadNum.Enabled = !learnChk.Checked; SendLead(); };
            leadNum.ValueChanged += delegate { if (!learnChk.Checked) SendLead(); };
            topChk.CheckedChanged += delegate { TopMost = topChk.Checked; };
            consoleChk.CheckedChanged += delegate { ShowConsole(consoleChk.Checked); };
            loading = false;

            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += delegate { RefreshState(); };
            timer.Start();
            RefreshState();
        }

        void SendLead()
        {
            if (!loading) coach.SetBeepLead(learnChk.Checked ? float.NaN : (float)leadNum.Value);
        }

        void UpdateLabels()
        {
            volLbl.Text = volBar.Value + "%";
            beepVolLbl.Text = beepVolBar.Value + "%";
            rateLbl.Text = (rateBar.Value > 0 ? "+" : "") + rateBar.Value;
        }

        void RefreshState()
        {
            stateLbl.Text = coach.StState;
            comboLbl.Text = coach.StCombo;
            refLbl.Text = coach.StRef.Length > 0 ? coach.StRef : "– (przejedź jedno czyste okrążenie)";
            lastLbl.Text = coach.StLast.Length > 0 ? coach.StLast : "–";
            focusLbl.Text = coach.Opt.Focus ? (coach.StFocus.Length > 0 ? coach.StFocus : "wybieram…") : "wyłączony";
        }

        void SaveDefaults()
        {
            Options o = new Options { Train = trainChk.Checked, Quiet = !tipsChk.Checked, Focus = focusChk.Checked };
            st.Mode = Settings.ModeOf(o);
            st.FocusCorner = (int)focusNum.Value;
            st.Mute = muteChk.Checked;
            st.VoiceName = voiceNames[Math.Max(0, voiceBox.SelectedIndex)];
            st.Volume = volBar.Value;
            st.BeepVolume = beepVolBar.Value;
            st.Rate = rateBar.Value;
            st.LearnLead = learnChk.Checked;
            st.BeepLead = (float)leadNum.Value;
            try
            {
                st.Save();
                MessageBox.Show(this, "Zapisano – trener tak wystartuje następnym razem.\n" + Settings.FilePath, "AC Coach", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e) { MessageBox.Show(this, "Nie udało się zapisać:\n" + e.Message, "AC Coach"); }
        }

        static void ShowConsole(bool show)
        {
            try { ShowWindow(GetConsoleWindow(), show ? 5 : 0); } catch (Exception) { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!closingFromCoach && coach.Running && e.CloseReason == CloseReason.UserClosing &&
                MessageBox.Show(this, "Zakończyć trenera? Okrążenia są już zapisane.", "AC Coach",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            ShowConsole(true);   // never leave a hidden console behind (a .bat waits on "pause")
            if (!closingFromCoach) coach.Stop();
            base.OnFormClosing(e);
        }

        CheckBox Check(GroupBox g, string text, bool on)
        {
            CheckBox c = new CheckBox { Text = text, AutoSize = true, Checked = on };
            Add(g, c, 10, y);
            y += 26;
            return c;
        }

        Label Row(GroupBox g, string title)
        {
            Add(g, Lbl(title), 10, y);
            Label v = Lbl("");
            Add(g, v, 80, y);
            y += 22;
            return v;
        }
    }
}
