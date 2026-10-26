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
    // Layout helpers: fixed rows inside group boxes.
    public partial class MainForm : Form
    {
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
}
