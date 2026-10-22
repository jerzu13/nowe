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
    // "ACCoach.exe config": the settings window of the current Windows user (saved by Settings).
    public class ConfigForm : Form
    {
        [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll")] static extern uint GetConsoleProcessList(uint[] list, uint count);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);

        public static int Run(Settings s)
        {
            // started from a shortcut: hide our own console window (never the one of a cmd the user typed in)
            try { if (GetConsoleProcessList(new uint[2], 2) == 1) ShowWindow(GetConsoleWindow(), 0); } catch (Exception) { }
            Thread t = new Thread(delegate ()
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ConfigForm(s));
            });
            t.SetApartmentState(ApartmentState.STA);   // folder dialogs need STA
            t.Start();
            t.Join();
            return 0;
        }

        readonly Settings s;
        readonly List<string> voiceNames = new List<string>();

        RadioButton rTrain, rNoTrain, rQuiet, rFocus;
        NumericUpDown focusNum, leadNum;
        ComboBox voiceBox;
        TrackBar rateBar, volBar;
        Label rateLbl, volLbl, dataHint, acHint;
        CheckBox muteChk, learnChk, mapChk;
        TextBox dataBox, acBox;
        Button testBtn;

        const int W = 560;   // inner width of the groups
        int y;               // layout cursor inside the current group

        ConfigForm(Settings settings)
        {
            s = settings;
            Text = "AC Coach – konfiguracja (" + Environment.UserName + ")";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7f, 15f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            int top = 10;

            // --- mode
            GroupBox g = Group("Tryb jazdy (start ze skrótu „AC Coach”)", ref top);
            rTrain = Radio(g, "Trening z sygnałami – piknięcia do hamowania + uwagi o linii");
            rNoTrain = Radio(g, "Bez piknięć – uwaga po zakręcie, czas na mecie");
            rQuiet = Radio(g, "Tryb cichy – tylko czasy okrążeń i notatki (np. z CrewChiefem)");
            rFocus = Radio(g, "Trening fokus – głos mówi tylko o jednym zakręcie");
            Add(g, Lbl("Zakręt:"), 30, y + 3);
            focusNum = new NumericUpDown { Minimum = 0, Maximum = 40, Width = 55 };
            Add(g, focusNum, 85, y);
            Add(g, Lbl("0 = ten, który kosztuje najwięcej"), 150, y + 3);
            y += 30;
            rFocus.CheckedChanged += delegate { focusNum.Enabled = rFocus.Checked; };
            End(g, ref top);

            // --- voice
            g = Group("Głos", ref top);
            Add(g, Lbl("Głos:"), 10, y + 3);
            voiceBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 440 };
            voiceBox.Items.Add("Automatycznie (pierwszy polski głos)");
            voiceNames.Add("");
            List<string[]> voices = Voice.Installed();
            foreach (string[] v in voices)
            {
                voiceBox.Items.Add(v[0] + (v[1].Length > 0 ? "  (" + v[1] + ")" : ""));
                voiceNames.Add(v[0]);
            }
            Add(g, voiceBox, 110, y);
            y += 30;
            if (!voices.Exists(v => v[1].StartsWith("pl")))
            {
                Label warn = Lbl("Brak polskiego głosu. Dodaj: Ustawienia Windows > Czas i język > Mowa > Dodaj głosy > Polski.");
                warn.ForeColor = Color.DarkRed;
                Add(g, warn, 10, y);
                y += 22;
            }
            rateBar = Bar(g, "Tempo mowy:", -10, 10, out rateLbl);
            volBar = Bar(g, "Głośność głosu:", 0, 100, out volLbl);
            volBar.TickFrequency = 10;
            rateBar.ValueChanged += delegate { rateLbl.Text = (rateBar.Value > 0 ? "+" : "") + rateBar.Value; };
            volBar.ValueChanged += delegate { volLbl.Text = volBar.Value + "%"; };
            muteChk = new CheckBox { Text = "Wycisz wszystko (bez głosu i piknięć)", AutoSize = true };
            Add(g, muteChk, 10, y + 4);
            testBtn = new Button { Text = "Test głosu i piknięć", Width = 160, Height = 27 };
            testBtn.Click += delegate { TestVoice(); };
            Add(g, testBtn, W - 170, y);
            y += 34;
            End(g, ref top);

            // --- beeps
            g = Group("Piknięcia do hamowania", ref top);
            learnChk = new CheckBox { Text = "Ucz się mojego czasu reakcji (zalecane)", AutoSize = true };
            Add(g, learnChk, 10, y);
            y += 26;
            Add(g, Lbl("Ostatnie piknięcie"), 30, y + 3);
            leadNum = new NumericUpDown { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05m, Width = 60 };
            Add(g, leadNum, 150, y);
            Add(g, Lbl("s przed punktem hamowania"), 215, y + 3);
            y += 30;
            learnChk.CheckedChanged += delegate { leadNum.Enabled = !learnChk.Checked; };
            End(g, ref top);

            // --- folders
            g = Group("Foldery", ref top);
            dataBox = PathRow(g, "Dane (okrążenia, raporty, rywale):", "Otwórz", delegate
            {
                string d = DataDir();
                try { Directory.CreateDirectory(d); Process.Start("explorer.exe", "\"" + d + "\""); } catch (Exception) { }
            }, out dataHint);
            dataBox.TextChanged += delegate { dataHint.Text = dataBox.Text.Trim().Length == 0 ? "Puste = domyślnie: " + Program.DefaultDataDir() : ""; };
            acBox = PathRow(g, "Assetto Corsa (folder gry):", "Wykryj", delegate { Detect(); }, out acHint);
            acBox.TextChanged += delegate { CheckAc(); };
            mapChk = new CheckBox { Text = "Po analizie otwieraj mapę toru w przeglądarce", AutoSize = true };
            Add(g, mapChk, 10, y);
            y += 28;
            End(g, ref top);

            // --- buttons
            Button start = new Button { Text = "Zapisz i uruchom trenera", Width = 180, Height = 30 };
            Button analyze = new Button { Text = "Analiza ostatniej sesji", Width = 160, Height = 30 };
            Button save = new Button { Text = "Zapisz", Width = 90, Height = 30 };
            Button close = new Button { Text = "Zamknij", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            start.Click += delegate { if (Save()) { Launch(""); Close(); } };
            analyze.Click += delegate { if (Save()) Launch("analyze --pause"); };
            save.Click += delegate { if (Save()) MessageBox.Show(this, "Zapisano:\n" + Settings.FilePath, "AC Coach", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            close.Click += delegate { Close(); };
            int x = 12;
            foreach (Button b in new[] { start, analyze, save, close })
            {
                b.Location = new Point(x, top + 4);
                Controls.Add(b);
                x += b.Width + 8;
            }
            close.Left = 12 + W + 20 - close.Width;
            save.Left = close.Left - save.Width - 8;
            AcceptButton = save;
            CancelButton = close;
            ClientSize = new Size(W + 20 + 24, top + 48);

            LoadValues();
        }

        void LoadValues()
        {
            rTrain.Checked = s.Mode == Settings.Train;
            rNoTrain.Checked = s.Mode == Settings.NoTrain;
            rQuiet.Checked = s.Mode == Settings.Quiet;
            rFocus.Checked = s.Mode == Settings.Focus;
            focusNum.Value = Math.Min(focusNum.Maximum, s.FocusCorner);
            focusNum.Enabled = rFocus.Checked;
            int vi = voiceNames.IndexOf(s.VoiceName);
            voiceBox.SelectedIndex = vi < 0 ? 0 : vi;
            rateBar.Value = s.Rate;
            volBar.Value = s.Volume;
            rateLbl.Text = (s.Rate > 0 ? "+" : "") + s.Rate;
            volLbl.Text = s.Volume + "%";
            muteChk.Checked = s.Mute;
            learnChk.Checked = s.LearnLead;
            leadNum.Value = (decimal)Math.Round(s.BeepLead, 2);
            leadNum.Enabled = !s.LearnLead;
            mapChk.Checked = s.OpenMap;
            dataBox.Text = s.DataDir;
            acBox.Text = s.AcRoot;
            dataHint.Text = s.DataDir.Length == 0 ? "Puste = domyślnie: " + Program.DefaultDataDir() : "";
            CheckAc();
        }

        void ReadValues(Settings t)
        {
            t.Mode = rNoTrain.Checked ? Settings.NoTrain : rQuiet.Checked ? Settings.Quiet : rFocus.Checked ? Settings.Focus : Settings.Train;
            t.FocusCorner = (int)focusNum.Value;
            t.VoiceName = voiceNames[Math.Max(0, voiceBox.SelectedIndex)];
            t.Rate = rateBar.Value;
            t.Volume = volBar.Value;
            t.Mute = muteChk.Checked;
            t.LearnLead = learnChk.Checked;
            t.BeepLead = (float)leadNum.Value;
            t.OpenMap = mapChk.Checked;
            t.DataDir = dataBox.Text.Trim().Trim('"');
            t.AcRoot = acBox.Text.Trim().Trim('"');
        }

        bool Save()
        {
            ReadValues(s);
            if (s.DataDir.Length > 0)
            {
                try { Directory.CreateDirectory(s.DataDir); }
                catch (Exception e)
                {
                    MessageBox.Show(this, "Nie można użyć folderu danych:\n" + s.DataDir + "\n\n" + e.Message, "AC Coach", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            try { s.Save(); }
            catch (Exception e)
            {
                MessageBox.Show(this, "Nie udało się zapisać ustawień:\n" + e.Message, "AC Coach", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }

        string DataDir()
        {
            string d = dataBox.Text.Trim().Trim('"');
            return d.Length > 0 ? d : Program.DefaultDataDir();
        }

        static void Launch(string args)
        {
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show("Nie udało się uruchomić:\n" + e.Message, "AC Coach"); }
        }

        void Detect()
        {
            TrackAi.AcRootOverride = null;
            string found = TrackAi.FindAcRoot(true);
            if (found == null)
                MessageBox.Show(this, "Nie znaleziono Assetto Corsa. Wskaż folder gry przyciskiem „…” (ten, w którym jest acs.exe i folder content).",
                    "AC Coach", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        void TestVoice()
        {
            Settings t = new Settings();
            ReadValues(t);
            Options o = new Options();
            t.Apply(o);
            testBtn.Enabled = false;
            Thread th = new Thread(delegate ()
            {
                try { Program.VoiceTest(o); } catch (Exception) { }
                try { BeginInvoke((MethodInvoker)delegate { testBtn.Enabled = true; }); } catch (Exception) { }
            });
            th.IsBackground = true;
            th.Start();
        }

        // --- layout helpers (fixed positions: predictable on every Windows DPI, scaled by AutoScaleMode.Font)
        GroupBox Group(string title, ref int top)
        {
            GroupBox g = new GroupBox { Text = title, Location = new Point(12, top), Width = W + 20 };
            Controls.Add(g);
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

        static Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true };
        }

        RadioButton Radio(GroupBox g, string text)
        {
            RadioButton r = new RadioButton { Text = text, AutoSize = true };
            Add(g, r, 10, y);
            y += 24;
            return r;
        }

        TrackBar Bar(GroupBox g, string title, int min, int max, out Label value)
        {
            Add(g, Lbl(title), 10, y + 6);
            TrackBar b = new TrackBar { Minimum = min, Maximum = max, Width = 380, TickStyle = TickStyle.BottomRight };
            Add(g, b, 110, y);
            value = Lbl("");
            Add(g, value, 500, y + 6);
            y += 44;
            return b;
        }

        TextBox PathRow(GroupBox g, string title, string extra, EventHandler extraClick, out Label hint)
        {
            Add(g, Lbl(title), 10, y);
            y += 20;
            TextBox box = new TextBox { Width = 400 };
            Add(g, box, 10, y);
            Button browse = new Button { Text = "…", Width = 32, Height = 25 };
            browse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog { Description = title, SelectedPath = box.Text })
                    if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
            };
            Add(g, browse, 416, y - 1);
            Button more = new Button { Text = extra, Width = 100, Height = 25 };
            more.Click += extraClick;
            Add(g, more, 454, y - 1);
            y += 28;
            hint = new Label { AutoSize = false, AutoEllipsis = true, Width = W, Height = 18, ForeColor = SystemColors.GrayText };
            Add(g, hint, 10, y);
            y += 24;
            return box;
        }
    }
}
