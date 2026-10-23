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
    // (tab "Jazda"), session analysis, folders and help. Everything the trainer prints lands in the log.
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

        const int GroupW = 440;       // inner width of the groups on the left of "Jazda" and in "Ustawienia"
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
            driveTab = new TabPage("Jazda");
            analysisTab = new TabPage("Analiza");
            settingsTab = new TabPage("Ustawienia");
            helpTab = new TabPage("Pomoc");
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
            Shown += delegate { Console.WriteLine("AC Coach " + typeof(MainForm).Assembly.GetName().Version.ToString(3) + " – dane: " + DataDir()); };
        }

        // ---------- top bar ----------

        void BuildTop()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(10, 8, 10, 8) };
            startBtn = new Button { Text = "▶  Start trenera", Width = 190, Height = 40, Location = new Point(10, 9), Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold) };
            startBtn.Click += delegate { if (Running) StopCoach(); else StartCoach(); };
            stateLbl = new Label { AutoSize = true, Location = new Point(215, 10), Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Text = "Trener zatrzymany" };
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
            catch (Exception e) { MessageBox.Show(this, "Nie można użyć folderu danych:\n" + o.DataDir + "\n\n" + e.Message, "AC Coach"); return; }
            o.TargetMs = argTarget;   // per car + track target comes from cel.txt when the session starts
            coach = new LiveCoach(o);
            coachThread = new Thread(delegate ()
            {
                try { coach.Run(); }
                catch (Exception e) { Console.WriteLine("Błąd trenera: " + e.Message); }
            }) { IsBackground = true, Name = "coach" };
            coachThread.Start();
            startBtn.Text = "■  Zatrzymaj trenera";
            tabs.SelectedTab = driveTab;
        }

        void StopCoach()
        {
            if (coach == null) return;
            coach.Stop();
            startBtn.Enabled = false;   // re-enabled by Tick once the coaching thread has finished
            startBtn.Text = "Zatrzymuję…";
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

        // ---------- "Jazda" ----------

        void BuildDrive()
        {
            Panel left = new Panel { Dock = DockStyle.Left, Width = GroupW + 44, AutoScroll = true };
            int top = 8;

            GroupBox g = Group(left, "Stan", ref top);
            refLbl = Row(g, "Wzorzec:");
            lastLbl = Row(g, "Ostatnie:");
            focusLbl = Row(g, "Fokus:");
            End(g, ref top);

            g = Group(left, "Cel czasu okrążenia", ref top);
            targetFor = new Label { AutoSize = false, Width = GroupW, Height = 18, ForeColor = SystemColors.GrayText, AutoEllipsis = true };
            Add(g, targetFor, 10, y); y += 22;
            targetBox = new TextBox { Width = 90 };
            Add(g, targetBox, 10, y);
            Button setT = new Button { Text = "Ustaw cel", Width = 90, Height = 25 };
            Button offT = new Button { Text = "Wyłącz", Width = 70, Height = 25 };
            Add(g, setT, 106, y - 1);
            Add(g, offT, 202, y - 1);
            Add(g, Lbl("np. 1:35.000"), 280, y + 3);
            y += 30;
            targetLbl = new Label { AutoSize = false, Width = GroupW, Height = 34 };
            Add(g, targetLbl, 10, y); y += 36;
            stepLbl = new Label { AutoSize = false, Width = GroupW, Height = 18, Font = new Font(Font, FontStyle.Bold) };
            Add(g, stepLbl, 10, y); y += 22;
            setT.Click += delegate
            {
                int ms = TargetPlan.ParseTime(targetBox.Text);
                if (ms <= 0) { MessageBox.Show(this, "Wpisz czas okrążenia, np. 1:35.000", "AC Coach"); return; }
                SetTarget(ms);
            };
            offT.Click += delegate { SetTarget(0); };
            targetBox.KeyDown += delegate (object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { setT.PerformClick(); e.SuppressKeyPress = true; } };
            End(g, ref top);

            g = Group(left, "Głos i piknięcia – działają od razu, także w trakcie jazdy", ref top);
            trainChk = Check(g, "Piknięcia do hamowania + uwagi o linii");
            tipsChk = Check(g, "Uwagi głosowe po zakrętach (wyłączone = tylko czasy i notatki)");
            focusChk = new CheckBox { Text = "Fokus na jednym zakręcie", AutoSize = true };
            Add(g, focusChk, 10, y + 2);
            Add(g, Lbl("zakręt:"), 220, y + 4);
            focusNum = new NumericUpDown { Minimum = 0, Maximum = 40, Width = 50 };
            Add(g, focusNum, 270, y + 1);
            Add(g, Lbl("0 = auto"), 326, y + 4);
            y += 30;
            muteChk = Check(g, "Wycisz wszystko");
            Add(g, Lbl("Głos:"), 10, y + 4);
            voiceBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = GroupW - 110 };
            voiceBox.Items.Add("Automatycznie (pierwszy polski głos)");
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
                    Text = "Brak polskiego głosu. Dodaj: Ustawienia Windows > Czas i język > Mowa > Dodaj głosy > Polski." };
                Add(g, warn, 10, y); y += 34;
            }
            volBar = Bar(g, "Głośność głosu:", 0, 100, out volLbl);
            beepVolBar = Bar(g, "Głośność piknięć:", 0, 100, out beepVolLbl);
            rateBar = Bar(g, "Tempo mowy:", -10, 10, out rateLbl);
            volBar.TickFrequency = beepVolBar.TickFrequency = 10;
            learnChk = Check(g, "Ostatnie piknięcie: ucz się mojego czasu reakcji");
            Add(g, Lbl("albo stale"), 30, y + 3);
            leadNum = new NumericUpDown { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05m, Width = 60 };
            Add(g, leadNum, 110, y);
            Add(g, Lbl("s przed punktem hamowania"), 175, y + 3);
            y += 32;
            testBtn = new Button { Text = "Test głosu i piknięć", Width = 160, Height = 27 };
            testBtn.Click += delegate { TestVoice(); };
            Add(g, testBtn, 10, y);
            y += 34;
            End(g, ref top);

            // the trainer's log, where the console used to be
            Panel right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 8, 8, 8) };
            Panel logBar = new Panel { Dock = DockStyle.Top, Height = 28 };
            logBar.Controls.Add(new Label { Text = "Log trenera – czasy, uwagi głosowe, piknięcia, koła", AutoSize = true, Location = new Point(0, 6), Font = new Font(Font, FontStyle.Bold) });
            followChk = new CheckBox { Text = "przewijaj", AutoSize = true, Checked = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Button clear = new Button { Text = "Wyczyść", Width = 80, Height = 24, Anchor = AnchorStyles.Top | AnchorStyles.Right };
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
            try { st.Save(); } catch (Exception e) { Console.WriteLine("Nie udało się zapisać ustawień: " + e.Message); }
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
            if (combo == null) { MessageBox.Show(this, "Najpierw przejedź kilka okrążeń – cel ustawia się dla konkretnego auta i toru.", "AC Coach"); return; }
            TargetPlan.SaveTarget(combo, ms);
            Console.WriteLine(ms > 0 ? "Cel czasu " + Lap.FormatTime(ms) + " dla " + ComboName(combo) + " – trener oceni go po starcie." : "Cel czasu wyłączony dla " + ComboName(combo));
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
                try { Program.VoiceTest(t); } catch (Exception e) { Console.WriteLine("Test głosu: " + e.Message); }
                try { BeginInvoke((MethodInvoker)delegate { testBtn.Enabled = true; }); } catch (Exception) { }
            }) { IsBackground = true }.Start();
        }

        // ---------- "Analiza" ----------

        void BuildAnalysis()
        {
            Panel bar = new Panel { Dock = DockStyle.Top, Height = 46 };
            bar.Controls.Add(new Label { Text = "Auto i tor:", AutoSize = true, Location = new Point(10, 15) });
            comboList = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420, Location = new Point(80, 11) };
            analyzeBtn = new Button { Text = "Analizuj ostatnią sesję", Width = 170, Height = 28, Location = new Point(510, 9) };
            Button map = new Button { Text = "Otwórz mapę toru", Width = 140, Height = 28, Location = new Point(686, 9) };
            Button folder = new Button { Text = "Folder raportów", Width = 130, Height = 28, Location = new Point(832, 9) };
            analyzeBtn.Click += delegate { RunAnalysis(); };
            map.Click += delegate
            {
                string f = Newest(SelectedCombo(), "mapa_*.html");
                if (f == null) MessageBox.Show(this, "Brak mapy – najpierw kliknij „Analizuj ostatnią sesję”.", "AC Coach");
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
            else report.Text = "Brak nagranych okrążeń w " + dir + "\n\nWystartuj trenera i przejedź kilka okrążeń.";
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
            analyzeBtn.Text = "Analizuję…";
            report.Text = "Analizuję " + ComboName(combo) + "…";
            string dir = DataDir();
            bool open = mapChk.Checked;
            analysisThread = new Thread(delegate ()
            {
                try { Analyzer.Run(dir, combo, open, argTarget); }
                catch (Exception e) { Console.WriteLine("Błąd analizy: " + e.Message); }
                try { BeginInvoke((MethodInvoker)delegate { analyzeBtn.Enabled = true; analyzeBtn.Text = "Analizuj ostatnią sesję"; ShowReport(); }); } catch (Exception) { }
            }) { IsBackground = true, Name = "analysis" };
            analysisThread.Start();
        }

        void ShowReport()
        {
            string f = Newest(SelectedCombo(), "raport_*.txt");
            report.Text = f != null ? File.ReadAllText(f, Encoding.UTF8) : "Brak raportu dla " + (SelectedCombo() != null ? ComboName(SelectedCombo()) : "") + " – kliknij „Analizuj ostatnią sesję”.";
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
            try { Process.Start(path); } catch (Exception e) { Console.WriteLine("Nie udało się otworzyć " + path + ": " + e.Message); }
        }

        // ---------- "Ustawienia" ----------

        void BuildSettings()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            int top = 8;
            GroupBox g = Group(p, "Foldery", ref top);
            dataBox = PathRow(g, "Dane (okrążenia, raporty, rywale):", "Otwórz", delegate { string d = DataDir(); try { Directory.CreateDirectory(d); Open(d); } catch (Exception) { } }, out dataHint);
            acBox = PathRow(g, "Assetto Corsa (folder gry):", "Wykryj", delegate { Detect(); }, out acHint);
            Button apply = new Button { Text = "Zastosuj foldery", Width = 140, Height = 27 };
            Add(g, apply, 10, y); y += 34;
            apply.Click += delegate { ApplyFolders(); };
            dataBox.TextChanged += delegate { DataHint(); };
            acBox.TextChanged += delegate { CheckAc(); };
            End(g, ref top);

            g = Group(p, "Start i analiza", ref top);
            autoChk = Check(g, "Startuj trenera od razu po otwarciu AC Coach");
            mapChk = Check(g, "Po analizie otwieraj mapę toru w przeglądarce");
            autoChk.CheckedChanged += delegate { Remember(); };
            mapChk.CheckedChanged += delegate { Remember(); };
            End(g, ref top);

            Label file = new Label { AutoSize = true, Location = new Point(12, top), ForeColor = SystemColors.GrayText,
                Text = "Ustawienia tego użytkownika Windows zapisują się same w:\n" + Settings.FilePath };
            p.Controls.Add(file);
            settingsTab.Controls.Add(p);
        }

        void ApplyFolders()
        {
            string d = dataBox.Text.Trim().Trim('"'), a = acBox.Text.Trim().Trim('"');
            if (d.Length > 0)
            {
                try { Directory.CreateDirectory(d); }
                catch (Exception e) { MessageBox.Show(this, "Nie można użyć folderu danych:\n" + d + "\n\n" + e.Message, "AC Coach"); return; }
            }
            st.DataDir = d;
            st.AcRoot = a;
            TrackAi.AcRootOverride = a.Length > 0 ? a : null;
            Save();
            Console.WriteLine("Foldery zapisane. Dane: " + DataDir() + (Running ? " (od następnego startu trenera)" : ""));
        }

        string DataDir()
        {
            if (dataArg != null) return dataArg;
            return string.IsNullOrEmpty(st.DataDir) ? Program.DefaultDataDir() : st.DataDir;
        }

        void DataHint() { dataHint.Text = dataBox.Text.Trim().Length == 0 ? "Puste = domyślnie: " + Program.DefaultDataDir() : ""; }

        void Detect()
        {
            TrackAi.AcRootOverride = null;
            string found = TrackAi.FindAcRoot(true);
            if (found == null)
                MessageBox.Show(this, "Nie znaleziono Assetto Corsa. Wskaż folder gry przyciskiem „…” (ten, w którym jest acs.exe i folder content).", "AC Coach");
            else acBox.Text = found;
        }

        void CheckAc()
        {
            string p = acBox.Text.Trim().Trim('"');
            if (p.Length == 0) { acHint.Text = "Puste = szukaj automatycznie (Steam, wszystkie dyski)."; acHint.ForeColor = SystemColors.GrayText; return; }
            bool ok = false;
            try { ok = Directory.Exists(Path.Combine(p, "content", "tracks")); } catch (Exception) { }
            acHint.Text = ok ? "OK – folder gry znaleziony." : "W tym folderze nie ma content\\tracks – to chyba nie jest folder Assetto Corsa.";
            acHint.ForeColor = ok ? Color.DarkGreen : Color.DarkRed;
        }

        // ---------- "Pomoc" ----------

        void BuildHelp()
        {
            RichTextBox help = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None, BackColor = SystemColors.Window };
            string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "JAK UZYWAC.txt");
            help.Text = File.Exists(f) ? File.ReadAllText(f, Encoding.UTF8) : "Brak pliku z instrukcją (JAK UZYWAC.txt) obok ACCoach.exe.";
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
            if (!running && !startBtn.Enabled || (!running && startBtn.Text != "▶  Start trenera"))
            {
                startBtn.Enabled = true;
                startBtn.Text = "▶  Start trenera";
            }
            if (running)
            {
                stateLbl.Text = coach.StState;
                comboLbl.Text = coach.StCombo;
                refLbl.Text = coach.StRef.Length > 0 ? coach.StRef : "– (przejedź jedno czyste okrążenie)";
                lastLbl.Text = coach.StLast.Length > 0 ? coach.StLast : "–";
                focusLbl.Text = coach.Opt.Focus ? (coach.StFocus.Length > 0 ? coach.StFocus : "wybieram…") : "wyłączony";
                int tm = coach.Opt.TargetMs;
                if (tm != shownTarget && !targetBox.Focused) { shownTarget = tm; targetBox.Text = tm > 0 ? Lap.FormatTime(tm) : ""; }
                targetFor.Text = coach.StCombo.Length > 0 ? "dla: " + coach.StCombo : "dla auta i toru, którym zaraz pojedziesz";
                targetLbl.Text = tm > 0 ? coach.StTarget : "Brak celu – wpisz czas i kliknij „Ustaw cel”. Trener oceni, czy jest realny, i rozpisze go na zakręty.";
                stepLbl.Text = tm > 0 ? coach.StStep : "";
            }
            else
            {
                stateLbl.Text = "Trener zatrzymany";
                comboLbl.Text = "Kliknij „Start trenera” – możesz to zrobić przed albo po uruchomieniu gry.";
                if ((DateTime.Now - newestAt).TotalSeconds > 5) { newestCombo = Analyzer.NewestCombo(DataDir()); newestAt = DateTime.Now; }
                string combo = newestCombo;
                int tm = combo != null ? TargetPlan.LoadTarget(combo) : 0;
                if (tm != shownTarget && !targetBox.Focused) { shownTarget = tm; targetBox.Text = tm > 0 ? Lap.FormatTime(tm) : ""; }
                targetFor.Text = combo != null ? "dla: " + ComboName(combo) + " (ostatnio jeżdżone)" : "cel ustawisz po pierwszych okrążeniach";
                targetLbl.Text = tm > 0 ? "Ocena celu i plan na zakręty pojawią się po starcie trenera." : "Brak celu – wpisz czas i kliknij „Ustaw cel”.";
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
            if (l.Contains("[GŁOS]")) return Color.FromArgb(110, 200, 255);
            if (l.Contains("NOWY WZORZEC") || l.Contains("cel osiągnięty")) return Color.FromArgb(110, 220, 120);
            if (l.Contains("[WYJAZD]") || l.Contains("NIEWAŻNE") || l.Contains("[PÓŹNO]") || l.Contains("[WCZEŚNIE]") || l.Contains("ZIMNE") || l.StartsWith("Błąd")) return Color.FromArgb(255, 150, 90);
            if (l.StartsWith("FOKUS") || l.StartsWith("CEL CZASU") || l.Contains("cel pośredni") || l.Contains("cel końcowy")) return Color.FromArgb(240, 200, 80);
            if (l.Contains("[PIK ") || l.Contains("[HAMUJ]")) return Color.FromArgb(130, 130, 140);
            if (l.StartsWith("Okrążenie")) return Color.White;
            return Color.Gainsboro;
        }
    }
}
