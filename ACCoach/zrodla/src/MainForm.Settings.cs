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
    // Tabs "Settings" (folders, start options, language) and "Help".
    public partial class MainForm : Form
    {
        // ---------- settings tab ----------

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

            g = Group(p, L.T("Aplikacja w grze (Assetto Corsa)", "In-game app (Assetto Corsa)"), ref top);
            gameChk = Check(g, L.T("Instaluj aplikację „AC Coach” w grze i wysyłaj jej dane", "Install the “AC Coach” app in the game and send it data"));
            gameCdChk = Check(g, L.T("Licznik metrów do punktu hamowania", "Metres to the braking point"));
            gameBarsChk = Check(g, L.T("Paski hamulca i gazu (ile wcisnąć, kolor = faza)", "Brake and throttle bars (how much to press, colour = phase)"));
            gameDeltaChk = Check(g, L.T("Strata / zysk do najlepszego okrążenia", "Time lost / gained to the best lap"));
            Button install = new Button { Text = L.T("Zainstaluj / zaktualizuj teraz", "Install / update now"), Width = 200, Height = 27 };
            Add(g, install, 10, y); y += 34;
            install.Click += delegate { InstallGameApp(true); };
            foreach (CheckBox c in new[] { gameChk, gameCdChk, gameBarsChk, gameDeltaChk })
                c.CheckedChanged += delegate { if (loading) return; RememberGameApp(); };
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
            TrackAi.FindAcRoot(true);
            InstallGameApp(false);
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

        // ---------- in-game app ----------

        void InstallGameApp(bool verbose)
        {
            if (!st.GameApp && !verbose) return;
            gameDir = GameApp.Install(st, verbose);
        }

        void RememberGameApp()
        {
            st.GameApp = gameChk.Checked;
            st.GameCountdown = gameCdChk.Checked;
            st.GameBars = gameBarsChk.Checked;
            st.GameDelta = gameDeltaChk.Checked;
            o.GameApp = st.GameApp;
            foreach (CheckBox c in new[] { gameCdChk, gameBarsChk, gameDeltaChk }) c.Enabled = st.GameApp;
            saveDue = true;
            if (st.GameApp && gameDir == null) InstallGameApp(false);
            else GameApp.WriteSettings(gameDir, st);
        }

        // ---------- help tab ----------

        void BuildHelp()
        {
            RichTextBox help = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None, BackColor = SystemColors.Window };
            string name = L.T("JAK UZYWAC.txt", "HOW TO USE.txt");
            string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            help.Text = File.Exists(f) ? File.ReadAllText(f, Encoding.UTF8) : L.T("Brak pliku z instrukcją (", "The manual (") + name + L.T(") obok ACCoach.exe.", ") is missing next to ACCoach.exe.");
            helpTab.Controls.Add(help);
        }
    }
}
