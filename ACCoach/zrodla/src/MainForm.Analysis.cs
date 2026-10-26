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
    // Tab "Analysis": session report and track map.
    public partial class MainForm : Form
    {
        // ---------- analysis tab ----------

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
    }
}
