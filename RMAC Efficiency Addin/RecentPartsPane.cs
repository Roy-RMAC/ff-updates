using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Drawing;

namespace RMAC_Efficiency_Addin
{
    internal sealed class RecentPartsPane : UserControl
    {
        private readonly ListBox _lstRecent;
        private readonly Button _btnPlace;
        private readonly Button _btnPlaceGrounded;

        public event Action<string>? PlaceRequested;
        public event Action<string>? PlaceGroundedRequested;

        public RecentPartsPane()
        {
            Dock = DockStyle.Fill;

            // Match LayoutsController dark theme
            var bg = Color.FromArgb(45, 45, 48);
            var panelBg = Color.FromArgb(37, 37, 38);
            var border = Color.FromArgb(62, 62, 64);
            var text = Color.Gainsboro;

            var root = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                BackColor = bg
            };

            // Row 0: list area (fixed 300 like DERIVE PARTS)
            // Row 1: buttons row (40)
            // Row 2: filler
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = bg
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var grpRecent = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "RECENT PARTS",
                ForeColor = text,
                BackColor = bg,
                Padding = new Padding(10, 12, 10, 10)
            };

            var listFrame = MakeFramedPanel(panelBg, border);

            _lstRecent = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = text,
                BorderStyle = BorderStyle.None,
                SelectionMode = SelectionMode.One
            };

            _lstRecent.Items.Add("(No parts saved this session.)");
            listFrame.Controls.Add(_lstRecent);
            grpRecent.Controls.Add(listFrame);

            // Buttons row, side-by-side like FULL DERIVE / CUSTOM DERIVE
            var buttonStrip = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = bg
            };

            _btnPlace = MakeButton("Place", 190, border, text);
            _btnPlaceGrounded = MakeButton("Place - Grounded", 190, border, text);

            _btnPlace.Left = 0;
            _btnPlaceGrounded.Left = _btnPlace.Right + 8;
            _btnPlace.Top = _btnPlaceGrounded.Top = 6;

            _btnPlace.Click += (_, __) =>
            {
                var item = _lstRecent.SelectedItem as RecentPartItem;
                if (item == null) return;
                PlaceRequested?.Invoke(item.FullPath);
            };

            _btnPlaceGrounded.Click += (_, __) =>
            {
                var item = _lstRecent.SelectedItem as RecentPartItem;
                if (item == null) return;
                PlaceGroundedRequested?.Invoke(item.FullPath);
            };

            buttonStrip.Controls.Add(_btnPlace);
            buttonStrip.Controls.Add(_btnPlaceGrounded);

            layout.Controls.Add(grpRecent, 0, 0);
            layout.Controls.Add(buttonStrip, 0, 1);

            root.Controls.Add(layout);
            Controls.Add(root);
        }

        public void SetItems(IReadOnlyList<string> fullPaths)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetItems(fullPaths)));
                return;
            }

            _lstRecent.BeginUpdate();
            try
            {
                _lstRecent.Items.Clear();

                if (fullPaths == null || fullPaths.Count == 0)
                {
                    _lstRecent.Items.Add("(No parts saved this session.)");
                    return;
                }

                foreach (var p in fullPaths)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    _lstRecent.Items.Add(new RecentPartItem(p));
                }

                // Select first item by default (fast workflow)
                if (_lstRecent.Items.Count > 0 && _lstRecent.Items[0] is RecentPartItem)
                    _lstRecent.SelectedIndex = 0;
            }
            finally
            {
                _lstRecent.EndUpdate();
            }
        }

        private sealed class RecentPartItem
        {
            public string FullPath { get; }
            public string DisplayName { get; }

            public RecentPartItem(string fullPath)
            {
                FullPath = fullPath;
                DisplayName = Path.GetFileName(fullPath);
            }

            public override string ToString() => DisplayName;
        }

        private static Panel MakeFramedPanel(Color fill, Color border)
        {
            var p = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(6),
                BackColor = fill
            };

            p.Paint += (s, e) =>
            {
                using var pen = new Pen(border);
                var r = p.ClientRectangle;
                r.Width -= 1;
                r.Height -= 1;
                e.Graphics.DrawRectangle(pen, r);
            };

            return p;
        }

        private static Button MakeButton(string text, int width, Color border, Color fore)
        {
            var btn = new Button
            {
                Text = text,
                Width = width,
                Height = 28,
                BackColor = Color.FromArgb(63, 63, 70),
                ForeColor = fore,
                FlatStyle = FlatStyle.Flat
            };
            btn.FlatAppearance.BorderColor = border;
            btn.FlatAppearance.BorderSize = 1;
            return btn;
        }
    }
}
