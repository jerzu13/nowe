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
    // The one AC Coach window: start/stop, state, lap-time target and live settings with the trainer's log
    // (tab L.T("Jazda", "Drive")), session analysis, folders and help. Everything the trainer prints lands in the log.
    public partial class MainForm : Form
    {
        readonly Settings st;
        readonly Options o;
        readonly int argTarget;       // --cel from the command line, 0 = per car + track (cel.txt)
        readonly string dataArg;      // --data from the command line: used, but not saved in the settings
        LiveCoach coach;
        Thread coachThread, analysisThread;
        bool saveDue, loading = true;
        readonly List<string> voiceNames = new List<string>();

        // top bar
        Button startBtn;
        Label stateLbl, comboLbl;
        TabControl tabs;
        TabPage driveTab, analysisTab, settingsTab, helpTab;
        // drive tab
        Label refLbl, lastLbl, focusLbl, targetLbl, stepLbl, targetFor, rateLbl, volLbl, beepVolLbl;
        TextBox targetBox;
        CheckBox trainChk, tipsChk, focusChk, muteChk, learnChk, followChk;
        NumericUpDown focusNum, leadNum;
        ComboBox voiceBox;
        TrackBar rateBar, volBar, beepVolBar;
        Button testBtn;
        RichTextBox log;
        int shownTarget = -1;
        string newestCombo;
        DateTime newestAt = DateTime.MinValue;
        // analysis tab
        ComboBox comboList;
        Button analyzeBtn;
        RichTextBox report;
        // settings tab
        TextBox dataBox, acBox;
        Label dataHint, acHint;
        CheckBox autoChk, mapChk;

        const int GroupW = 440;       // inner width of the groups on the left of the drive tab and in the settings tab
        int y;

        public MainForm(Settings settings, Options options, int argTarget, string dataArg, bool autoStart, string startTab)
        {
            st = settings;
            o = options;
            this.argTarget = argTarget;
            this.dataArg = dataArg;
            Text = "AC Coach";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7f, 15f);
            ClientSize = new Size(1280, 840);
            MinimumSize = new Size(1000, 680);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            BuildTop();
            tabs = new TabControl { Dock = DockStyle.Fill };
            driveTab = new TabPage(L.T("Jazda", "Drive"));
            analysisTab = new TabPage(L.T("Analiza", "Analysis"));
            settingsTab = new TabPage(L.T("Ustawienia", "Settings"));
            helpTab = new TabPage(L.T("Pomoc", "Help"));
            tabs.TabPages.AddRange(new[] { driveTab, analysisTab, settingsTab, helpTab });
            Controls.Add(tabs);
            Controls.SetChildIndex(tabs, 0);   // fill below the top bar
            BuildDrive();
            BuildAnalysis();
            BuildSettings();
            BuildHelp();
            LoadValues();
            loading = false;

            LogWriter.Install(DataDir());
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += delegate { Tick(); };
            timer.Start();

            if (startTab == "analyze") { tabs.SelectedTab = analysisTab; Shown += delegate { RunAnalysis(); }; }
            else if (startTab == "config") tabs.SelectedTab = settingsTab;
            else if (startTab == "voicetest") Shown += delegate { TestVoice(); };
            if (autoStart) Shown += delegate { StartCoach(); };
            Shown += delegate { Console.WriteLine("AC Coach " + typeof(MainForm).Assembly.GetName().Version.ToString(3) + L.T(" – dane: ", " – data: ") + DataDir()); };
        }

        // ---------- top bar ----------

        void BuildTop()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(10, 8, 10, 8) };
            startBtn = new Button { Text = L.T("▶  Start trenera", "▶  Start trainer"), Width = 190, Height = 40, Location = new Point(10, 9), Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold) };
            startBtn.Click += delegate { if (Running) StopCoach(); else StartCoach(); };
            stateLbl = new Label { AutoSize = true, Location = new Point(215, 10), Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Text = L.T("Trener zatrzymany", "Trainer stopped") };
            comboLbl = new Label { AutoSize = true, Location = new Point(216, 33), ForeColor = SystemColors.GrayText };
            top.Controls.AddRange(new Control[] { startBtn, stateLbl, comboLbl });
            Controls.Add(top);
        }

        bool Running { get { return coachThread != null && coachThread.IsAlive; } }

        void StartCoach()
        {
            if (Running) return;
            o.DataDir = DataDir();
            try { Directory.CreateDirectory(o.DataDir); }
            catch (Exception e) { MessageBox.Show(this, L.T("Nie można użyć folderu danych:\n", "Cannot use the data folder:\n") + o.DataDir + "\n\n" + e.Message, "AC Coach"); return; }
            o.TargetMs = argTarget;   // per car + track target comes from cel.txt when the session starts
            coach = new LiveCoach(o);
            coachThread = new Thread(delegate ()
            {
                try { coach.Run(); }
                catch (Exception e) { Console.WriteLine(L.T("Błąd trenera: ", "Trainer error: ") + e.Message); }
            }) { IsBackground = true, Name = "coach" };
            coachThread.Start();
            startBtn.Text = L.T("■  Zatrzymaj trenera", "■  Stop trainer");
            tabs.SelectedTab = driveTab;
        }

        void StopCoach()
        {
            if (coach == null) return;
            coach.Stop();
            startBtn.Enabled = false;   // re-enabled by Tick once the coaching thread has finished
            startBtn.Text = L.T("Zatrzymuję…", "Stopping…");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (Running)
            {
                coach.Stop();
                coachThread.Join(3000);
            }
            if (saveDue) Save();
            base.OnFormClosing(e);
        }

        // ---------- values ----------

        void LoadValues()
        {
            trainChk.Checked = o.Train;
            tipsChk.Checked = !o.Quiet;
            focusChk.Checked = o.Focus;
            focusNum.Value = Math.Min(40, Math.Max(0, o.FocusCorner));
            focusNum.Enabled = o.Focus;
            muteChk.Checked = o.Mute;
            int vi = voiceNames.IndexOf(o.VoiceName ?? "");
            voiceBox.SelectedIndex = vi < 0 ? 0 : vi;
            volBar.Value = Math.Max(0, Math.Min(100, o.Volume));
            beepVolBar.Value = Math.Max(0, Math.Min(100, o.BeepVolume));
            rateBar.Value = Math.Max(-10, Math.Min(10, o.Rate));
            BarLabels();
            learnChk.Checked = float.IsNaN(o.BeepLead);
            leadNum.Value = (decimal)Math.Round(Math.Max(0f, Math.Min(1f, float.IsNaN(o.BeepLead) ? st.BeepLead : o.BeepLead)), 2);
            leadNum.Enabled = !learnChk.Checked;
            dataBox.Text = st.DataDir;
            acBox.Text = st.AcRoot;
            DataHint();
            CheckAc();
            autoChk.Checked = st.AutoStart;
            mapChk.Checked = !o.NoOpen;
        }

        // ---------- every 250 ms: state, target, log ----------

        void Tick()
        {
            if (saveDue) Save();
            bool running = Running;
            // --once (tests): the session is over and the trainer stopped by itself -> close the window
            if (o.Once && !running && coach != null) { LogWriter.Drain(log, false); Close(); return; }
            if (!running && !startBtn.Enabled || (!running && startBtn.Text != L.T("▶  Start trenera", "▶  Start trainer")))
            {
                startBtn.Enabled = true;
                startBtn.Text = L.T("▶  Start trenera", "▶  Start trainer");
            }
            if (running)
            {
                stateLbl.Text = coach.StState;
                comboLbl.Text = coach.StCombo;
                refLbl.Text = coach.StRef.Length > 0 ? coach.StRef : L.T("– (przejedź jedno czyste okrążenie)", "– (drive one clean lap)");
                lastLbl.Text = coach.StLast.Length > 0 ? coach.StLast : "–";
                focusLbl.Text = coach.Opt.Focus ? (coach.StFocus.Length > 0 ? coach.StFocus : L.T("wybieram…", "choosing…")) : L.T("wyłączony", "off");
                int tm = coach.Opt.TargetMs;
                if (tm != shownTarget && !targetBox.Focused) { shownTarget = tm; targetBox.Text = tm > 0 ? Lap.FormatTime(tm) : ""; }
                targetFor.Text = coach.StCombo.Length > 0 ? L.T("dla: ", "for: ") + coach.StCombo : L.T("dla auta i toru, którym zaraz pojedziesz", "for the car and track you are about to drive");
                targetLbl.Text = tm > 0 ? coach.StTarget : L.T("Brak celu – wpisz czas i kliknij „Ustaw cel”. Trener oceni, czy jest realny, i rozpisze go na zakręty.", "No target – type a time and click “Set target”. The trainer will judge whether it is realistic and split it across the turns.");
                stepLbl.Text = tm > 0 ? coach.StStep : "";
            }
            else
            {
                stateLbl.Text = L.T("Trener zatrzymany", "Trainer stopped");
                comboLbl.Text = L.T("Kliknij „Start trenera” – możesz to zrobić przed albo po uruchomieniu gry.", "Click “Start trainer” – before or after starting the game.");
                if ((DateTime.Now - newestAt).TotalSeconds > 5) { newestCombo = Analyzer.NewestCombo(DataDir()); newestAt = DateTime.Now; }
                string combo = newestCombo;
                int tm = combo != null ? TargetPlan.LoadTarget(combo) : 0;
                if (tm != shownTarget && !targetBox.Focused) { shownTarget = tm; targetBox.Text = tm > 0 ? Lap.FormatTime(tm) : ""; }
                targetFor.Text = combo != null ? L.T("dla: ", "for: ") + ComboName(combo) + L.T(" (ostatnio jeżdżone)", " (driven last)") : L.T("cel ustawisz po pierwszych okrążeniach", "you can set a target after your first laps");
                targetLbl.Text = tm > 0 ? L.T("Ocena celu i plan na zakręty pojawią się po starcie trenera.", "The verdict and the plan per turn appear once the trainer starts.") : L.T("Brak celu – wpisz czas i kliknij „Ustaw cel”.", "No target – type a time and click “Set target”.");
                stepLbl.Text = "";
            }
            LogWriter.Drain(log, followChk.Checked);
        }

    }

    // Console output -> the log box (the trainer, the analysis and the voice all write with Console.WriteLine).
    public sealed class LogWriter : TextWriter
    {
        static readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
        readonly StringBuilder cur = new StringBuilder();
        const int MaxLines = 4000;

        public override Encoding Encoding { get { return Encoding.UTF8; } }

        // every line also goes to <data>\log.txt (the run before is kept as log_prev.txt), to send with a problem report
        static StreamWriter file;

        public static void Install(string dataDir)
        {
            try
            {
                Directory.CreateDirectory(dataDir);
                string f = Path.Combine(dataDir, "log.txt");
                if (File.Exists(f)) File.Copy(f, Path.Combine(dataDir, "log_prev.txt"), true);
                file = new StreamWriter(f, false, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception) { file = null; }
            Console.SetOut(TextWriter.Synchronized(new LogWriter()));
        }

        public override void Write(char c)
        {
            if (c == '\r') return;
            if (c == '\n')
            {
                string line = cur.ToString();
                lines.Enqueue(line);
                cur.Clear();
                if (file != null) try { file.WriteLine(DateTime.Now.ToString("HH:mm:ss ") + line); } catch (Exception) { file = null; }
                return;
            }
            cur.Append(c);
        }

        public override void Write(string s) { if (s != null) foreach (char c in s) Write(c); }
        public override void WriteLine(string s) { Write(s); Write('\n'); }

        public static void Drain(RichTextBox box, bool follow)
        {
            if (lines.IsEmpty) return;
            string line;
            int n = 0;
            box.SuspendLayout();
            while (n < 500 && lines.TryDequeue(out line))
            {
                box.SelectionStart = box.TextLength;
                box.SelectionLength = 0;
                box.SelectionColor = ColorOf(line);
                box.AppendText(line + "\n");
                n++;
            }
            if (box.Lines.Length > MaxLines)
            {
                box.Select(0, box.GetFirstCharIndexFromLine(box.Lines.Length - MaxLines));
                box.ReadOnly = false; box.SelectedText = ""; box.ReadOnly = true;
            }
            box.ResumeLayout();
            if (follow) { box.SelectionStart = box.TextLength; box.ScrollToCaret(); }
        }

        static Color ColorOf(string l)
        {
            // both languages: the markers are in the texts of LiveCoach
            if (l.Contains("[GŁOS]") || l.Contains("[VOICE]")) return Color.FromArgb(110, 200, 255);
            if (l.Contains("NOWY WZORZEC") || l.Contains("NEW BEST LAP") || l.Contains("cel osiągnięty") || l.Contains("target reached")) return Color.FromArgb(110, 220, 120);
            if (l.Contains("[WYJAZD]") || l.Contains("[OFF TRACK]") || l.Contains("NIEWAŻNE") || l.Contains("INVALID") || l.Contains("[PÓŹNO]") || l.Contains("[LATE]")
                || l.Contains("[WCZEŚNIE]") || l.Contains("[EARLY]") || l.Contains("ZIMNE") || l.Contains("COLD") || l.StartsWith("Błąd") || l.Contains(" error: ")) return Color.FromArgb(255, 150, 90);
            if (l.StartsWith("FOKUS") || l.StartsWith("FOCUS") || l.StartsWith("CEL CZASU") || l.StartsWith("LAP TIME TARGET") || l.Contains("cel pośredni") || l.Contains("intermediate goal")
                || l.Contains("cel końcowy") || l.Contains("final target")) return Color.FromArgb(240, 200, 80);
            if (l.Contains("[PIK ") || l.Contains("[HAMUJ]") || l.Contains("[BEEP ") || l.Contains("[BRAKE]")) return Color.FromArgb(130, 130, 140);
            if (l.StartsWith("Okrążenie") || l.StartsWith("Lap ")) return Color.White;
            return Color.Gainsboro;
        }
    }
}
