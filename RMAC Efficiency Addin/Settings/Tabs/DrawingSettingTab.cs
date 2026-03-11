// UI/Settings/DrawingSettingsTab.cs
using Inventor;
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Krypton.Navigator;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.UI.Settings
{
    /// <summary>
    /// Settings tab for drawing-related settings currently stored in AddinSettings:
    /// - DrawingAssemblyTitleBlockName
    /// - DrawingPartsTitleBlockName
    /// </summary>
    internal sealed class DrawingSettingsTab : SettingsTabBase
    {
        public DrawingSettingsTab(ISettingsTabHost host) : base(host) { }

        public override string TabTitle => "Drawings";

        private KryptonPage? _tab;

        private const int RowHeight = 34;
        private const int ButtonColWidth = 230; // widened slightly for DPI + uniform button sizing

        private readonly KryptonTextBox _txtAsmTitleBlock = new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        private readonly KryptonTextBox _txtPartsTitleBlock = new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        private readonly KryptonButton _btnLoadTitleBlocks = new()
        {
            Text = "Load title blocks\u2026",
            AutoSize = false
        };

        private readonly KryptonButton _btnResetDefaults = new()
        {
            Text = "Reset defaults",
            AutoSize = false
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

            // Use fixed row heights so buttons don't get clipped under DPI scaling.
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ButtonColWidth));

            int r = 0;
            void AddRow(string labelText, Control middle, Control? right = null)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, RowHeight));

                var lbl = new KryptonLabel
                {
                    Text = labelText,
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 8, 0)
                };

                middle.Margin = new Padding(0, 5, 0, 5);
                middle.Dock = DockStyle.Fill;

                layout.Controls.Add(lbl, 0, r);
                layout.Controls.Add(middle, 1, r);

                if (right != null)
                {
                    right.Margin = new Padding(10, 5, 0, 5);
                    right.Anchor = AnchorStyles.Left;
                    layout.Controls.Add(right, 2, r);
                }
                else
                {
                    layout.Controls.Add(new Panel { Width = 1, Height = 1 }, 2, r);
                }

                r++;
            }

            // --- Button sizing: uniform width + height matching textbox ---
            int fieldH = _txtAsmTitleBlock.Height;
            _btnLoadTitleBlocks.Height = fieldH;
            _btnResetDefaults.Height = fieldH;

            _btnLoadTitleBlocks.AutoSize = false;
            _btnResetDefaults.AutoSize = false;

            // Uniform width based on longest caption (with padding)
            int wLoad = TextRenderer.MeasureText(_btnLoadTitleBlocks.Text, _btnLoadTitleBlocks.Font).Width;
            int wReset = TextRenderer.MeasureText(_btnResetDefaults.Text, _btnResetDefaults.Font).Width;
            int btnW = Math.Max(wLoad, wReset) + 28; // padding for borders/DPI

            // Clamp to keep within button column (leave some margin)
            int maxBtnW = ButtonColWidth - 12;
            if (btnW > maxBtnW) btnW = maxBtnW;

            _btnLoadTitleBlocks.Width = btnW;
            _btnResetDefaults.Width = btnW;

            // Updated label text per request
            AddRow("Assembly Title Block", _txtAsmTitleBlock, _btnLoadTitleBlocks);
            AddRow("Part Title Block", _txtPartsTitleBlock, _btnResetDefaults);

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(_txtInfo, 0, r);
            layout.SetColumnSpan(_txtInfo, 3);

            _txtInfo.Text =
                "These are Inventor Title Block definition names used by RMAC tools.\r\n\r\n" +
                "If the title block name does not exist in a drawing template, related automation may fail.\r\n" +
                "Use 'Load title blocks\u2026' with a drawing open to pick from available definitions.";

            _tab.Controls.Add(layout);
            return _tab;
        }

        public override void WireEvents()
        {
            _txtAsmTitleBlock.TextChanged += (_, __) => OnChanged();
            _txtPartsTitleBlock.TextChanged += (_, __) => OnChanged();

            _btnLoadTitleBlocks.Click += (_, __) => LoadTitleBlocksFromActiveDrawing();

            _btnResetDefaults.Click += (_, __) =>
            {
                if (MessageBox.Show(
                        Host.Owner,
                        "Reset drawing title block names to defaults?",
                        "Drawings",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                // Defaults match AddinSettings defaults (v8 migration)
                _txtAsmTitleBlock.Text = "RMAC_Title_Block";
                _txtPartsTitleBlock.Text = "RMAC Parts Title";
                Host.MarkChanged();
            };
        }

        public override void LoadFromSettings()
        {
            var s = AddinSettings.Current;

            _txtAsmTitleBlock.Text = s.DrawingAssemblyTitleBlockName ?? "RMAC_Title_Block";
            _txtPartsTitleBlock.Text = s.DrawingPartsTitleBlockName ?? "RMAC Parts Title";
        }

        public override void ApplyToSettings()
        {
            var s = AddinSettings.Current;

            s.DrawingAssemblyTitleBlockName = (_txtAsmTitleBlock.Text ?? "").Trim();
            s.DrawingPartsTitleBlockName = (_txtPartsTitleBlock.Text ?? "").Trim();
        }

        public override void Validate(List<string> warnings, List<string> errors)
        {
            var asm = (_txtAsmTitleBlock.Text ?? "").Trim();
            var prt = (_txtPartsTitleBlock.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(asm))
                errors.Add("Drawings: Assembly title block name is blank.");

            if (string.IsNullOrWhiteSpace(prt))
                errors.Add("Drawings: Parts title block name is blank.");

            // Not an error, but common gotcha
            if (asm.Equals(prt, StringComparison.OrdinalIgnoreCase))
                warnings.Add("Drawings: Assembly and Parts title block names are identical. Ensure this is intentional.");
        }

        private void LoadTitleBlocksFromActiveDrawing()
        {
            var invApp = Host.InventorApp;
            if (invApp == null)
            {
                MessageBox.Show(Host.Owner, "Inventor application reference is not available.", "Drawings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                if (invApp.ActiveDocument == null || invApp.ActiveDocument.DocumentType != DocumentTypeEnum.kDrawingDocumentObject)
                {
                    MessageBox.Show(Host.Owner, "Open a drawing (.idw/.dwg) first, then click 'Load title blocks\u2026'.", "Drawings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var dd = (DrawingDocument)invApp.ActiveDocument;
                var defs = dd.TitleBlockDefinitions;

                var names = new List<string>();
                foreach (TitleBlockDefinition d in defs)
                {
                    if (!string.IsNullOrWhiteSpace(d?.Name))
                        names.Add(d.Name);
                }

                names = names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();

                if (names.Count == 0)
                {
                    MessageBox.Show(Host.Owner, "No Title Block definitions were found in the active drawing.", "Drawings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!ShowTitleBlockPicker(names, out var asmPick, out var prtPick))
                    return;

                _txtAsmTitleBlock.Text = asmPick;
                _txtPartsTitleBlock.Text = prtPick;
                Host.MarkChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Host.Owner, "Failed to read title blocks from the active drawing.\r\n\r\n" + ex.Message, "Drawings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool ShowTitleBlockPicker(List<string> names, out string asmPick, out string prtPick)
        {
            asmPick = (_txtAsmTitleBlock.Text ?? "").Trim();
            prtPick = (_txtPartsTitleBlock.Text ?? "").Trim();

            using var f = new KryptonForm
            {
                Text = "Select title blocks",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                AutoScaleMode = AutoScaleMode.Dpi,
                Font = SystemFonts.MessageBoxFont,
                ClientSize = new Size(560, 160)
            };

            var tlp = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 2,
                RowCount = 3
            };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var cmbAsm = new KryptonComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            var cmbPrt = new KryptonComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbAsm.Items.AddRange(names.Cast<object>().ToArray());
            cmbPrt.Items.AddRange(names.Cast<object>().ToArray());

            void SelectIfPresent(KryptonComboBox cmb, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) { cmb.SelectedIndex = 0; return; }
                var idx = names.FindIndex(n => n.Equals(value, StringComparison.OrdinalIgnoreCase));
                cmb.SelectedIndex = idx >= 0 ? idx : 0;
            }

            SelectIfPresent(cmbAsm, asmPick);
            SelectIfPresent(cmbPrt, prtPick);

            // Updated label text per request
            var lblAsm = new KryptonLabel { Text = "Assembly Title Block", Dock = DockStyle.Fill };
            var lblPrt = new KryptonLabel { Text = "Part Title Block", Dock = DockStyle.Fill };

            tlp.Controls.Add(lblAsm, 0, 0);
            tlp.Controls.Add(cmbAsm, 1, 0);
            tlp.Controls.Add(lblPrt, 0, 1);
            tlp.Controls.Add(cmbPrt, 1, 1);

            var btnOk = new KryptonButton { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
            var btnCancel = new KryptonButton { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true
            };
            flow.Controls.Add(btnOk);
            flow.Controls.Add(btnCancel);

            tlp.Controls.Add(flow, 1, 2);

            f.Controls.Add(tlp);
            f.AcceptButton = btnOk;
            f.CancelButton = btnCancel;

            if (f.ShowDialog(Host.Owner) != DialogResult.OK)
                return false;

            asmPick = cmbAsm.SelectedItem?.ToString() ?? names[0];
            prtPick = cmbPrt.SelectedItem?.ToString() ?? names[0];
            return true;
        }
    }
}
