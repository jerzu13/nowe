using System;
using System.Drawing;
using System.Windows.Forms;

namespace ACCoach
{
    // Common look of the AC Coach windows: fixed-position rows inside group boxes.
    public abstract class LayoutForm : Form
    {
        protected int W = 560;   // inner width of the groups
        protected int y;         // layout cursor inside the current group

        protected LayoutForm()
        {
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7f, 15f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
        }

        // --- layout helpers (fixed positions: predictable on every Windows DPI, scaled by AutoScaleMode.Font)
        protected GroupBox Group(string title, ref int top)
        {
            GroupBox g = new GroupBox { Text = title, Location = new Point(12, top), Width = W + 20 };
            Controls.Add(g);
            y = 22;
            return g;
        }

        protected void End(GroupBox g, ref int top)
        {
            g.Height = y + 6;
            top += g.Height + 8;
        }

        protected static void Add(Control parent, Control c, int x, int cy)
        {
            c.Location = new Point(x, cy);
            parent.Controls.Add(c);
        }

        protected static Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true };
        }

        protected RadioButton Radio(GroupBox g, string text)
        {
            RadioButton r = new RadioButton { Text = text, AutoSize = true };
            Add(g, r, 10, y);
            y += 24;
            return r;
        }

        protected TrackBar Bar(GroupBox g, string title, int min, int max, out Label value)
        {
            Add(g, Lbl(title), 10, y + 6);
            TrackBar b = new TrackBar { Minimum = min, Maximum = max, Width = W - 190, TickStyle = TickStyle.BottomRight };
            Add(g, b, 120, y);
            value = Lbl("");
            Add(g, value, W - 60, y + 6);
            y += 44;
            return b;
        }

        protected TextBox PathRow(GroupBox g, string title, string extra, EventHandler extraClick, out Label hint)
        {
            Add(g, Lbl(title), 10, y);
            y += 20;
            TextBox box = new TextBox { Width = W - 160 };
            Add(g, box, 10, y);
            Button browse = new Button { Text = "…", Width = 32, Height = 25 };
            browse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog { Description = title, SelectedPath = box.Text })
                    if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
            };
            Add(g, browse, W - 144, y - 1);
            Button more = new Button { Text = extra, Width = 100, Height = 25 };
            more.Click += extraClick;
            Add(g, more, W - 106, y - 1);
            y += 28;
            hint = new Label { AutoSize = false, AutoEllipsis = true, Width = W, Height = 18, ForeColor = SystemColors.GrayText };
            Add(g, hint, 10, y);
            y += 24;
            return box;
        }
    }
}
