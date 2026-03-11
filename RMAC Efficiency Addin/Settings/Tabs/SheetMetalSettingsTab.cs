// UI/Settings/SheetMetalSettingsTab.cs
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Navigator;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.UI.Settings
{
    /// <summary>
    /// Settings tab for sheet metal-related settings currently stored in AddinSettings:
    /// - SheetMetalThicknessParamName
    ///
    /// (If you later add DXF layer mapping / export options, they can live here too.)
    /// </summary>
    internal sealed class SheetMetalSettingsTab : SettingsTabBase
    {
        public SheetMetalSettingsTab(ISettingsTabHost host) : base(host) { }

        public override string TabTitle => "Sheet Metal";

        private KryptonPage? _tab;

        private readonly KryptonTextBox _txtThicknessParam = new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        private readonly KryptonButton _btnResetDefaults = new()
        {
            Text = "Reset defaults",
            AutoSize = false,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        private readonly KryptonTextBox _txtInfo = new()
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill
        };

        public override KryptonPage BuildTab()
        {
            if (_tab != null) return _tab;

            _tab = new KryptonPage { Text = TabTitle, Padding = new Padding(10) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(0),
                Margin = new Padding(0),
                AutoSize = false
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));

            int r = 0;
            void AddRow(string labelText, Control middle, Control? right = null)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var lbl = new KryptonLabel
                {
                    Text = labelText,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 6, 8, 6)
                };

                middle.Margin = new Padding(0, 3, 0, 3);

                layout.Controls.Add(lbl, 0, r);
                layout.Controls.Add(middle, 1, r);

                if (right != null)
                {
                    // Match the vertical margins of the textbox row so the button aligns cleanly.
                    right.Margin = new Padding(8, 3, 0, 3);
                    layout.Controls.Add(right, 2, r);
                }
                else
                {
                    layout.Controls.Add(new Panel { Width = 1, Height = 1 }, 2, r);
                }

                r++;
            }

            AddRow("Thickness property/parameter name", _txtThicknessParam, _btnResetDefaults);

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(_txtInfo, 0, r);
            layout.SetColumnSpan(_txtInfo, 3);

            _txtInfo.Text =
                "This setting defines the name of the iProperty/parameter your RMAC tools read for sheet metal thickness.\r\n\r\n" +
                "Used by exporter naming and downstream processing.\r\n\r\n" +
                "Examples:\r\n" +
                "\u2022 THICKNESS\r\n" +
                "\u2022 SM_THK\r\n";

            _tab.Controls.Add(layout);

            // TextBox height is effectively fixed in WinForms; force the button height to match.
            // Do this after controls exist so PreferredHeight is correct.
            void SyncButtonHeight()
            {
                int h = _txtThicknessParam.Height;
                if (h > 0)
                {
                    _btnResetDefaults.Height = h;
                    _btnResetDefaults.MinimumSize = new Size(0, h);
                }
            }

            // Run once now, and again when handle is created (covers DPI/font/layout timing).
            SyncButtonHeight();
            _tab.HandleCreated += (_, __) => SyncButtonHeight();

            return _tab;
        }

        public override void WireEvents()
        {
            _txtThicknessParam.TextChanged += (_, __) => OnChanged();

            _btnResetDefaults.Click += (_, __) =>
            {
                if (MessageBox.Show(
                        Host.Owner,
                        "Reset sheet metal settings to defaults?",
                        "Sheet Metal",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                _txtThicknessParam.Text = "THICKNESS";
                Host.MarkChanged();
            };
        }

        public override void LoadFromSettings()
        {
            var s = AddinSettings.Current;
            _txtThicknessParam.Text = s.SheetMetalThicknessParamName ?? "THICKNESS";
        }

        public override void ApplyToSettings()
        {
            var s = AddinSettings.Current;
            s.SheetMetalThicknessParamName = (_txtThicknessParam.Text ?? "").Trim();
        }

        public override void Validate(List<string> warnings, List<string> errors)
        {
            var name = (_txtThicknessParam.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                errors.Add("Sheet Metal: thickness property/parameter name is blank.");
        }
    }
}
