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
    public class MainForm : Form
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

            LogWriter.Install();
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

        // ---------- L.T("Jazda", "Drive") ----------

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

        // ---------- L.T("Analiza", "Analysis") ----------

        void BuildAnalysis()
        {
            Panel bar = new Panel { Dock = DockStyle.Top, Height = 46 };
            bar.Controls.Add(new Label { Text = L.T("Auto i tor:", "Car and track:"), AutoSize = true, Location = new Point(10, 15) });
            comboList = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400, Location = new Point(110, 11) };
            analyzeBtn = new Button { Text = L.T("Analizuj ostatnią sesję", "Analyse last session"), Width = 170, Height = 28, Location = new Point(520, 9) };
            Button map = new Button { Text = L.T("Otwórz mapę toru", "Open track map"), Width = 140, Height = 28, Location = new Point(696, 9) };
            Button folder = new Button { Text = L.T("Folder raportów", "Reports folder"), Width = 130, Height = 28, Location = new Point(842, 9) };
            analyzeBtn.Click += delegate { RunAnalysis(); };
            map.Click += delegate
            {
                string f = Newest(SelectedCombo(), "mapa_*.html");
                if (f == null) MessageBox.Show(this, L.T("Brak mapy – najpierw kliknij „Analizuj ostatnią sesję”.", "No map yet – click “Analyse last session” first."), "AC Coach");
                else Open(f);
            };
            folder.Click += delegate { string c = SelectedCombo(); if (c != null) { Directory.CreateDirectory(Path.Combine(c, "reports")); Open(Path.Combine(c, "reports")); } };
            comboList.SelectedIndexChanged += delegate { ShowReport(); };
            bar.Controls.AddRange(new Control[] { comboList, analyzeBtn, map, folder });
            report = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None, WordWrap = false, BackColor = SystemColors.Window };
            analysisTab.Controls.Add(report);
            analysisTab.Controls.Add(bar);
            analysisTab.Enter += delegate { FillCombos(); };
        }

        void FillCombos()
        {
            string keep = SelectedCombo();
            comboList.Items.Clear();
            string dir = DataDir();
            if (!Directory.Exists(dir)) return;
            var combos = Directory.GetDirectories(dir).Where(d => Directory.Exists(Path.Combine(d, "laps")))
                .Select(d => new { Dir = d, T = Directory.GetFiles(Path.Combine(d, "laps"), "*.csv").Select(f => File.GetLastWriteTime(f)).DefaultIfEmpty(DateTime.MinValue).Max() })
                .OrderByDescending(x => x.T).ToList();
            foreach (var c in combos) comboList.Items.Add(new ComboItem { Dir = c.Dir });
            int i = combos.FindIndex(c => c.Dir == keep);
            if (comboList.Items.Count > 0) comboList.SelectedIndex = Math.Max(0, i);
            else report.Text = L.T("Brak nagranych okrążeń w ", "No recorded laps in ") + dir + L.T("\n\nWystartuj trenera i przejedź kilka okrążeń.", "\n\nStart the trainer and drive a few laps.");
        }

        class ComboItem
        {
            public string Dir;
            public override string ToString() { return ComboName(Dir); }
        }

        string SelectedCombo()
        {
            ComboItem c = comboList.SelectedItem as ComboItem;
            return c != null ? c.Dir : null;
        }

        void RunAnalysis()
        {
            if (analysisThread != null && analysisThread.IsAlive) return;
            if (comboList.Items.Count == 0) FillCombos();
            string combo = SelectedCombo();
            if (combo == null) return;
            analyzeBtn.Enabled = false;
            analyzeBtn.Text = L.T("Analizuję…", "Analysing…");
            report.Text = L.T("Analizuję ", "Analysing ") + ComboName(combo) + "…";
            string dir = DataDir();
            bool open = mapChk.Checked;
            analysisThread = new Thread(delegate ()
            {
                try { Analyzer.Run(dir, combo, open, argTarget); }
                catch (Exception e) { Console.WriteLine(L.T("Błąd analizy: ", "Analysis error: ") + e.Message); }
                try { BeginInvoke((MethodInvoker)delegate { analyzeBtn.Enabled = true; analyzeBtn.Text = L.T("Analizuj ostatnią sesję", "Analyse last session"); ShowReport(); }); } catch (Exception) { }
            }) { IsBackground = true, Name = "analysis" };
            analysisThread.Start();
        }

        void ShowReport()
        {
            string f = Newest(SelectedCombo(), "raport_*.txt");
            report.Text = f != null ? File.ReadAllText(f, Encoding.UTF8) : L.T("Brak raportu dla ", "No report for ") + (SelectedCombo() != null ? ComboName(SelectedCombo()) : "") + L.T(" – kliknij „Analizuj ostatnią sesję”.", " – click “Analyse last session”.");
        }

        static string Newest(string combo, string pattern)
        {
            if (combo == null) return null;
            string d = Path.Combine(combo, "reports");
            if (!Directory.Exists(d)) return null;
            return Directory.GetFiles(d, pattern).OrderByDescending(f => File.GetLastWriteTime(f)).FirstOrDefault();
        }

        static void Open(string path)
        {
            try { Process.Start(path); } catch (Exception e) { Console.WriteLine(L.T("Nie udało się otworzyć ", "Could not open ") + path + ": " + e.Message); }
        }

        // ---------- L.T("Ustawienia", "Settings") ----------

        void BuildSettings()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            int top = 8;
            GroupBox g = Group(p, L.T("Foldery", "Folders"), ref top);
            dataBox = PathRow(g, L.T("Dane (okrążenia, raporty, rywale):", "Data (laps, reports, rivals):"), L.T("Otwórz", "Open"), delegate { string d = DataDir(); try { Directory.CreateDirectory(d); Open(d); } catch (Exception) { } }, out dataHint);
            acBox = PathRow(g, L.T("Assetto Corsa (folder gry):", "Assetto Corsa (game folder):"), L.T("Wykryj", "Detect"), delegate { Detect(); }, out acHint);
            Button apply = new Button { Text = L.T("Zastosuj foldery", "Apply folders"), Width = 140, Height = 27 };
            Add(g, apply, 10, y); y += 34;
            apply.Click += delegate { ApplyFolders(); };
            dataBox.TextChanged += delegate { DataHint(); };
            acBox.TextChanged += delegate { CheckAc(); };
            End(g, ref top);

            g = Group(p, L.T("Start i analiza", "Start and analysis"), ref top);
            autoChk = Check(g, L.T("Startuj trenera od razu po otwarciu AC Coach", "Start the trainer as soon as AC Coach opens"));
            mapChk = Check(g, L.T("Po analizie otwieraj mapę toru w przeglądarce", "Open the track map in the browser after the analysis"));
            autoChk.CheckedChanged += delegate { Remember(); };
            mapChk.CheckedChanged += delegate { Remember(); };
            End(g, ref top);

            g = Group(p, "Language / Język", ref top);
            ComboBox lang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            lang.Items.AddRange(new object[] { "English", "Polski" });
            lang.SelectedIndex = L.En ? 0 : 1;
            Add(g, lang, 10, y); y += 30;
            lang.SelectedIndexChanged += delegate
            {
                string code = lang.SelectedIndex == 1 ? "pl" : "en";
                if (code == st.Lang) return;
                st.Lang = code;
                Save();
                if (MessageBox.Show(this, code == "pl" ? "Język zmieni się po ponownym uruchomieniu AC Coach. Uruchomić ponownie teraz?"
                        : "The language changes when AC Coach restarts. Restart now?", "AC Coach", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    if (Running) { coach.Stop(); coachThread.Join(3000); }
                    Application.Restart();
                }
            };
            End(g, ref top);

            Label file = new Label { AutoSize = true, Location = new Point(12, top), ForeColor = SystemColors.GrayText,
                Text = L.T("Ustawienia tego użytkownika Windows zapisują się same w:\n", "Settings of this Windows user are saved automatically in:\n") + Settings.FilePath };
            p.Controls.Add(file);
            settingsTab.Controls.Add(p);
        }

        void ApplyFolders()
        {
            string d = dataBox.Text.Trim().Trim('"'), a = acBox.Text.Trim().Trim('"');
            if (d.Length > 0)
            {
                try { Directory.CreateDirectory(d); }
                catch (Exception e) { MessageBox.Show(this, L.T("Nie można użyć folderu danych:\n", "Cannot use the data folder:\n") + d + "\n\n" + e.Message, "AC Coach"); return; }
            }
            st.DataDir = d;
            st.AcRoot = a;
            TrackAi.AcRootOverride = a.Length > 0 ? a : null;
            Save();
            Console.WriteLine(L.T("Foldery zapisane. Dane: ", "Folders saved. Data: ") + DataDir() + (Running ? L.T(" (od następnego startu trenera)", " (from the next trainer start)") : ""));
        }

        string DataDir()
        {
            if (dataArg != null) return dataArg;
            return string.IsNullOrEmpty(st.DataDir) ? Program.DefaultDataDir() : st.DataDir;
        }

        void DataHint() { dataHint.Text = dataBox.Text.Trim().Length == 0 ? L.T("Puste = domyślnie: ", "Empty = default: ") + Program.DefaultDataDir() : ""; }

        void Detect()
        {
            TrackAi.AcRootOverride = null;
            string found = TrackAi.FindAcRoot(true);
            if (found == null)
                MessageBox.Show(this, L.T("Nie znaleziono Assetto Corsa. Wskaż folder gry przyciskiem „…” (ten, w którym jest acs.exe i folder content).", "Assetto Corsa not found. Pick the game folder with the “…” button (the one with acs.exe and the content folder)."), "AC Coach");
            else acBox.Text = found;
        }

        void CheckAc()
        {
            string p = acBox.Text.Trim().Trim('"');
            if (p.Length == 0) { acHint.Text = L.T("Puste = szukaj automatycznie (Steam, wszystkie dyski).", "Empty = search automatically (Steam, all drives)."); acHint.ForeColor = SystemColors.GrayText; return; }
            bool ok = false;
            try { ok = Directory.Exists(Path.Combine(p, "content", "tracks")); } catch (Exception) { }
            acHint.Text = ok ? L.T("OK – folder gry znaleziony.", "OK – game folder found.") : L.T("W tym folderze nie ma content\\tracks – to chyba nie jest folder Assetto Corsa.", "There is no content\\tracks in this folder – this does not look like the Assetto Corsa folder.");
            acHint.ForeColor = ok ? Color.DarkGreen : Color.DarkRed;
        }

        // ---------- L.T("Pomoc", "Help") ----------

        void BuildHelp()
        {
            RichTextBox help = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None, BackColor = SystemColors.Window };
            string name = L.T("JAK UZYWAC.txt", "HOW TO USE.txt");
            string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            help.Text = File.Exists(f) ? File.ReadAllText(f, Encoding.UTF8) : L.T("Brak pliku z instrukcją (", "The manual (") + name + L.T(") obok ACCoach.exe.", ") is missing next to ACCoach.exe.");
            helpTab.Controls.Add(help);
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

        // ---------- layout helpers: fixed rows inside group boxes ----------

        GroupBox Group(Control parent, string title, ref int top)
        {
            GroupBox g = new GroupBox { Text = title, Location = new Point(10, top), Width = GroupW + 20 };
            parent.Controls.Add(g);
            y = 22;
            return g;
        }

        void End(GroupBox g, ref int top)
        {
            g.Height = y + 6;
            top += g.Height + 8;
        }

        static void Add(Control parent, Control c, int x, int cy)
        {
            c.Location = new Point(x, cy);
            parent.Controls.Add(c);
        }

        static Label Lbl(string text) { return new Label { Text = text, AutoSize = true }; }

        Label Row(GroupBox g, string title)
        {
            Add(g, Lbl(title), 10, y);
            Label v = new Label { AutoSize = false, Width = GroupW - 80, Height = 18, AutoEllipsis = true };
            Add(g, v, 80, y);
            y += 22;
            return v;
        }

        CheckBox Check(GroupBox g, string text)
        {
            CheckBox c = new CheckBox { Text = text, AutoSize = true };
            Add(g, c, 10, y);
            y += 26;
            return c;
        }

        TrackBar Bar(GroupBox g, string title, int min, int max, out Label value)
        {
            Add(g, Lbl(title), 10, y + 6);
            TrackBar b = new TrackBar { Minimum = min, Maximum = max, Width = GroupW - 190, TickStyle = TickStyle.BottomRight };
            Add(g, b, 120, y);
            value = Lbl("");
            Add(g, value, GroupW - 60, y + 6);
            y += 44;
            return b;
        }

        TextBox PathRow(GroupBox g, string title, string extra, EventHandler extraClick, out Label hint)
        {
            Add(g, Lbl(title), 10, y);
            y += 20;
            TextBox box = new TextBox { Width = GroupW - 160 };
            Add(g, box, 10, y);
            Button browse = new Button { Text = "…", Width = 32, Height = 25 };
            browse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog { Description = title, SelectedPath = box.Text })
                    if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
            };
            Add(g, browse, GroupW - 144, y - 1);
            Button more = new Button { Text = extra, Width = 100, Height = 25 };
            more.Click += extraClick;
            Add(g, more, GroupW - 106, y - 1);
            y += 28;
            hint = new Label { AutoSize = false, AutoEllipsis = true, Width = GroupW, Height = 18, ForeColor = SystemColors.GrayText };
            Add(g, hint, 10, y);
            y += 24;
            return box;
        }
    }

    // Console output -> the log box (the trainer, the analysis and the voice all write with Console.WriteLine).
    public sealed class LogWriter : TextWriter
    {
        static readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
        readonly StringBuilder cur = new StringBuilder();
        const int MaxLines = 4000;

        public override Encoding Encoding { get { return Encoding.UTF8; } }

        public static void Install() { Console.SetOut(TextWriter.Synchronized(new LogWriter())); }

        public override void Write(char c)
        {
            if (c == '\r') return;
            if (c == '\n') { lines.Enqueue(cur.ToString()); cur.Clear(); return; }
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
