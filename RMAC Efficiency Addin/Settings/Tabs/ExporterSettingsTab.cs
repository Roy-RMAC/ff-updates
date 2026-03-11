// Settings/Tabs/ExporterSettingsTab.cs
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RMAC_Efficiency_Addin.Settings.UI;
using Krypton.Navigator;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.UI.Settings
{
    internal sealed class ExporterSettingsTab : SettingsTabBase
    {
        public ExporterSettingsTab(ISettingsTabHost host) : base(host) { }

        public override string TabTitle => "Exporter";

        private KryptonPage? _tab;
        private bool _isLoading;
        private bool _suppressDetailEvents;
        private bool _suppressListEvents;

        // ---- Global exporter toggles ----
        private readonly KryptonCheckBox _chkExportTopAsmStep = new() { Text = "Export top-level assembly STEP", AutoSize = true };
        private readonly KryptonCheckBox _chkExportBomExcel = new() { Text = "Export BOM (Excel)", AutoSize = true };
        private readonly KryptonCheckBox _chkExportMasterPdf = new() { Text = "Export construction drawings PDF (all sheets)", AutoSize = true };
        // ---- Top-level naming templates ----
        private readonly KryptonTextBox _txtTopStepName = new() { ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly KryptonButton _btnTopStepName = new() { Text = "Edit\u2026" };
        private readonly KryptonTextBox _txtTopBomName = new() { ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly KryptonButton _btnTopBomName = new() { Text = "Edit\u2026" };
        private readonly KryptonTextBox _txtTopDrawingName = new() { ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly KryptonButton _btnTopDrawingName = new() { Text = "Edit\u2026" };

        // ---- Category list (left panel) ----
        private readonly KryptonListBox _list = new() { Dock = DockStyle.Fill };
        private readonly KryptonButton _btnAdd = new() { Text = "Add" };
        private readonly KryptonButton _btnRemove = new() { Text = "Remove" };
        private readonly KryptonButton _btnReset = new() { Text = "Reset exporter defaults" };

        // ---- Category data ----
        private sealed class CategoryRow
        {
            public string OriginalCategory { get; set; } = "";
            public string Category { get; set; } = "";
            public string FolderName { get; set; } = "";
            public bool PackageDrawings { get; set; }
            public bool ExportDXF { get; set; }
            public bool DxfPostProcessing { get; set; }
            public bool ExportSTEP { get; set; }
            public bool ExportBOM { get; set; }
            public string DrawingTemplate { get; set; } = "<Part.Part Number>";
            public string DxfTemplate { get; set; } = "<Part.Part Number>";
            public string StepTemplate { get; set; } = "<Part.Part Number>";
            public string BomTemplate { get; set; } = "<Part.Part Number>";
        }

        private readonly List<CategoryRow> _rows = new();

        // ---- Detail panel controls (right panel) ----
        private readonly KryptonTextBox _txtCategory = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly KryptonTextBox _txtFolder = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };

        private readonly KryptonCheckBox _chkDrawings = new() { Text = "Drawings (PDF package)", AutoSize = true };
        private readonly KryptonCheckBox _chkDXF = new() { Text = "DXF", AutoSize = true };
        private readonly KryptonCheckBox _chkDXFPost = new() { Text = "DXF post processing", AutoSize = true };
        private readonly KryptonCheckBox _chkSTEP = new() { Text = "STEP", AutoSize = true };
        private readonly KryptonCheckBox _chkBOM = new() { Text = "BOM", AutoSize = true };

        private readonly KryptonTextBox _txtNameDrawings = new() { ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly KryptonButton _btnNameDrawings = new() { Text = "Edit\u2026" };
        private readonly KryptonTextBox _txtNameDxf = new() { ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly KryptonButton _btnNameDxf = new() { Text = "Edit\u2026" };
        private readonly KryptonTextBox _txtNameStep = new() { ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly KryptonButton _btnNameStep = new() { Text = "Edit\u2026" };
        private readonly KryptonTextBox _txtNameBom = new() { ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly KryptonButton _btnNameBom = new() { Text = "Edit\u2026" };

        // ---- BOM column builder ----
        private readonly KryptonButton _btnAddBomCol = new() { Text = "+ Column" };
        private readonly KryptonButton _btnRemoveBomCol = new() { Text = "- Column" };

        private sealed class BomColumnCard
        {
            public Panel CardPanel { get; set; } = new();
            public KryptonComboBox TokenCombo { get; set; } = new();
            public KryptonTextBox CustomTokenText { get; set; } = new();
            public KryptonTextBox HeaderText { get; set; } = new();
            public KryptonCheckBox FormatCheck { get; set; } = new();
            public KryptonComboBox FormatCombo { get; set; } = new();
            public KryptonCheckBox StripUnitCheck { get; set; } = new();
        }

        private readonly List<BomColumnCard> _bomCards = new();
        private FlowLayoutPanel _bomColumnsHost = null!;

        private const int ListWidth = 180;
        private const int LabelWidth = 120;
        private const int BtnWidth = 80;

        // ---------------------------------------------------------------
        // Layout
        // ---------------------------------------------------------------

        public override KryptonPage BuildTab()
        {
            if (_tab != null) return _tab;

            _tab = new KryptonPage { Text = TabTitle, Padding = new Padding(10) };

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // Row 0: global options
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Row 1: categories (fill remaining)
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // Row 2: BOM columns (autosize)
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // Row 3: reset button

            // Row 0: global options
            outer.Controls.Add(BuildGlobalSection(), 0, 0);

            // Row 1: master-detail inside a group box
            var grpCategories = new KryptonGroupBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 0)
            };
            grpCategories.Values.Heading = "Categories";

            var masterDetail = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            masterDetail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ListWidth));
            masterDetail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            masterDetail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            masterDetail.Controls.Add(BuildListPanel(), 0, 0);
            masterDetail.Controls.Add(BuildDetailPanel(), 1, 0);

            grpCategories.Panel.Controls.Add(masterDetail);
            outer.Controls.Add(grpCategories, 0, 1);

            // Row 2: BOM columns builder
            outer.Controls.Add(BuildBomColumnsSection(), 0, 2);

            // Row 3: reset button
            _btnReset.AutoSize = true;
            _btnReset.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _btnReset.Padding = new Padding(10, 4, 10, 4);
            _btnReset.Margin = new Padding(0, 8, 0, 0);

            outer.Controls.Add(_btnReset, 0, 3);

            _tab.Controls.Add(outer);
            return _tab;
        }

        private Control BuildGlobalSection()
        {
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

            int r = 0;
            void AddRow(string labelText, Control middle, Control? right = null)
            {
                top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var lbl = MakeLabel(labelText);
                lbl.Anchor = AnchorStyles.Left;
                lbl.Margin = new Padding(0, 6, 8, 6);
                middle.Margin = new Padding(0, 3, 0, 3);

                top.Controls.Add(lbl, 0, r);
                top.Controls.Add(middle, 1, r);

                if (right != null)
                {
                    right.Margin = new Padding(8, 3, 0, 3);
                    right.Dock = DockStyle.Fill;
                    top.Controls.Add(right, 2, r);
                }
                r++;
            }

            var opts = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };
            opts.Controls.Add(_chkExportTopAsmStep);
            opts.Controls.Add(_chkExportBomExcel);
            opts.Controls.Add(_chkExportMasterPdf);

            AddRow("Options", opts);

            // Top-level naming templates
            AddRow("STEP file name", _txtTopStepName, _btnTopStepName);
            AddRow("BOM file name", _txtTopBomName, _btnTopBomName);
            AddRow("Drawing PDF name", _txtTopDrawingName, _btnTopDrawingName);

            return top;
        }

        private Control BuildListPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(0),
                Margin = new Padding(0, 0, 8, 0)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            panel.Controls.Add(_list, 0, 0);
            panel.SetColumnSpan(_list, 2);

            _btnAdd.Dock = DockStyle.Fill;
            _btnAdd.Margin = new Padding(0, 4, 2, 8);
            _btnRemove.Dock = DockStyle.Fill;
            _btnRemove.Margin = new Padding(2, 4, 0, 8);

            panel.Controls.Add(_btnAdd, 0, 1);
            panel.Controls.Add(_btnRemove, 1, 1);

            return panel;
        }

        private Control BuildDetailPanel()
        {
            var detail = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(8, 0, 0, 0)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelWidth));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, BtnWidth));

            int r = 0;

            void AddSectionHeader(string text)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var lbl = new KryptonLabel
                {
                    Text = text,
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    LabelStyle = LabelStyle.NormalPanel,
                    Margin = new Padding(0, r == 0 ? 0 : 12, 0, 4)
                };
                lbl.StateCommon.ShortText.Font = new Font(
                    SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);
                layout.Controls.Add(lbl, 0, r);
                layout.SetColumnSpan(lbl, 3);
                r++;
            }

            void AddFieldRow(string label, Control field, Control? button = null)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var lbl = MakeLabel(label);
                lbl.Anchor = AnchorStyles.Left;
                lbl.Margin = new Padding(0, 6, 8, 6);
                field.Margin = new Padding(0, 3, 0, 3);

                layout.Controls.Add(lbl, 0, r);
                layout.Controls.Add(field, 1, r);

                if (button != null)
                {
                    button.Dock = DockStyle.Fill;
                    button.Margin = new Padding(4, 3, 0, 3);
                    layout.Controls.Add(button, 2, r);
                }
                else
                {
                    layout.SetColumnSpan(field, 2);
                }
                r++;
            }

            void AddCheckboxRow(KryptonCheckBox chk)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                chk.Margin = new Padding(0, 2, 0, 2);
                layout.Controls.Add(chk, 0, r);
                layout.SetColumnSpan(chk, 3);
                r++;
            }

            // Category identity
            AddSectionHeader("Category");
            AddFieldRow("Category key", _txtCategory);
            AddFieldRow("Folder name", _txtFolder);

            // Actions
            AddSectionHeader("Actions");
            AddCheckboxRow(_chkDrawings);
            AddCheckboxRow(_chkDXF);
            AddCheckboxRow(_chkDXFPost);
            AddCheckboxRow(_chkSTEP);
            AddCheckboxRow(_chkBOM);

            // File naming
            AddSectionHeader("File naming");
            AddFieldRow("Drawings", _txtNameDrawings, _btnNameDrawings);
            AddFieldRow("DXF", _txtNameDxf, _btnNameDxf);
            AddFieldRow("STEP", _txtNameStep, _btnNameStep);
            AddFieldRow("BOM", _txtNameBom, _btnNameBom);

            detail.Controls.Add(layout);
            return detail;
        }

        // ---------------------------------------------------------------
        // BOM columns builder
        // ---------------------------------------------------------------

        private Control BuildBomColumnsSection()
        {
            var grp = new KryptonGroupBox
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            grp.Values.Heading = "BOM Columns";

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Button row
            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 4)
            };

            _btnAddBomCol.AutoSize = true;
            _btnAddBomCol.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _btnAddBomCol.Margin = new Padding(0, 0, 4, 0);
            _btnRemoveBomCol.AutoSize = true;
            _btnRemoveBomCol.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _btnRemoveBomCol.Margin = new Padding(0, 0, 12, 0);

            btnRow.Controls.Add(_btnAddBomCol);
            btnRow.Controls.Add(_btnRemoveBomCol);

            // Horizontally scrolling cards host
            _bomColumnsHost = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            outer.Controls.Add(btnRow, 0, 0);
            outer.Controls.Add(_bomColumnsHost, 0, 1);

            grp.Panel.Controls.Add(outer);
            return grp;
        }

        private BomColumnCard CreateBomColumnCard(BomColumnDefinition? def = null)
        {
            def ??= new BomColumnDefinition();

            var card = new BomColumnCard();

            card.CardPanel = new Panel
            {
                Width = 150,
                AutoSize = true,
                MinimumSize = new Size(150, 0),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(3),
                Padding = new Padding(4)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 8,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            var lblMargin = new Padding(0, 0, 0, 0);
            var ctrlMargin = new Padding(0, 0, 0, 2);

            // Row 0: "Token" label
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new KryptonLabel { Text = "Token", AutoSize = true, Margin = lblMargin }, 0, 0);

            // Row 1: Token dropdown
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.TokenCombo = new KryptonComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = ctrlMargin,
                DropDownWidth = 200,
                MaxDropDownItems = 20
            };

            // Populate: "Qty" first, then sorted registry tokens, then "(Custom...)"
            card.TokenCombo.Items.Add("Qty");
            var sorted = IPropertyTokenRegistry.All
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var t in sorted)
                card.TokenCombo.Items.Add(t.Name);
            card.TokenCombo.Items.Add("(Custom\u2026)");

            // Select matching item
            SelectTokenItem(card.TokenCombo, def.TokenName);
            layout.Controls.Add(card.TokenCombo, 0, 1);

            // Row 2: Custom token text (hidden unless Custom selected)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.CustomTokenText = new KryptonTextBox
            {
                Text = "",
                Dock = DockStyle.Fill,
                Margin = ctrlMargin,
                Visible = false
            };
            layout.Controls.Add(card.CustomTokenText, 0, 2);

            // Row 3: "Column name" label
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new KryptonLabel { Text = "Column name", AutoSize = true, Margin = lblMargin }, 0, 3);

            // Row 4: Header textbox
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.HeaderText = new KryptonTextBox
            {
                Text = def.Header ?? "",
                Dock = DockStyle.Fill,
                Margin = ctrlMargin
            };
            layout.Controls.Add(card.HeaderText, 0, 4);

            // Row 5: Format checkbox
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.FormatCheck = new KryptonCheckBox
            {
                Text = "Format",
                Checked = def.FormattingEnabled,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 0)
            };
            layout.Controls.Add(card.FormatCheck, 0, 5);

            // Row 6: Format combo
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.FormatCombo = new KryptonComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = ctrlMargin,
                Enabled = def.FormattingEnabled
            };
            card.FormatCombo.Items.AddRange(new object[] { "0", "0.0", "0.00", "0.000" });
            card.FormatCombo.SelectedItem = def.NumberFormat;
            if (card.FormatCombo.SelectedIndex < 0) card.FormatCombo.SelectedIndex = 1;
            layout.Controls.Add(card.FormatCombo, 0, 6);

            // Row 7: Strip unit checkbox
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.StripUnitCheck = new KryptonCheckBox
            {
                Text = "Strip unit",
                Checked = def.StripUnit,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 0),
                Enabled = def.FormattingEnabled
            };
            layout.Controls.Add(card.StripUnitCheck, 0, 7);

            card.CardPanel.Controls.Add(layout);

            // If the token is a custom one (not in dropdown), show custom text
            bool isCustom = IsCustomToken(def.TokenName);
            if (isCustom)
            {
                card.TokenCombo.SelectedItem = "(Custom\u2026)";
                card.CustomTokenText.Text = def.TokenName;
                card.CustomTokenText.Visible = true;
            }

            // Wire formatting toggle
            card.FormatCheck.CheckedChanged += (_, __) =>
            {
                if (_isLoading) return;
                card.FormatCombo.Enabled = card.FormatCheck.Checked;
                card.StripUnitCheck.Enabled = card.FormatCheck.Checked;
                Changed();
            };

            // Wire custom token visibility
            card.TokenCombo.SelectedIndexChanged += (_, __) =>
            {
                if (_isLoading) return;
                bool custom = card.TokenCombo.SelectedItem is string s && s == "(Custom\u2026)";
                card.CustomTokenText.Visible = custom;
                if (custom && string.IsNullOrWhiteSpace(card.CustomTokenText.Text))
                    card.CustomTokenText.Text = "THICKNESS";
                Changed();
            };

            // Wire other changes
            card.CustomTokenText.TextChanged += (_, __) => { if (!_isLoading) Changed(); };
            card.HeaderText.TextChanged += (_, __) => { if (!_isLoading) Changed(); };
            card.FormatCombo.SelectedIndexChanged += (_, __) => { if (!_isLoading) Changed(); };
            card.StripUnitCheck.CheckedChanged += (_, __) => { if (!_isLoading) Changed(); };

            return card;
        }

        private static void SelectTokenItem(KryptonComboBox combo, string tokenName)
        {
            // Try exact match
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i]?.ToString(), tokenName, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            // Not found → will be handled as custom
            if (combo.Items.Count > 0)
                combo.SelectedIndex = combo.Items.Count - 1; // "(Custom...)"
        }

        private static bool IsCustomToken(string tokenName)
        {
            if (string.Equals(tokenName, "Qty", StringComparison.OrdinalIgnoreCase)) return false;
            if (IPropertyTokenRegistry.FindByName(tokenName) != null) return false;
            return true; // Not in the standard dropdown — treat as custom
        }

        private string GetCardTokenName(BomColumnCard card)
        {
            var selected = card.TokenCombo.SelectedItem?.ToString() ?? "Part Number";
            if (selected == "(Custom\u2026)")
                return (card.CustomTokenText.Text ?? "").Trim();
            return selected;
        }

        /// <summary>
        /// Creates a read-only "COMMENTS" card that is always shown at the end
        /// to indicate the Comments column is automatically appended for filtering.
        /// </summary>
        private Panel? _commentsCard;

        private void RebuildCommentsCard()
        {
            if (_commentsCard != null)
            {
                _bomColumnsHost.Controls.Remove(_commentsCard);
                _commentsCard.Dispose();
                _commentsCard = null;
            }
            _commentsCard = CreateCommentsCard();
            _bomColumnsHost.Controls.Add(_commentsCard);
        }

        private Panel CreateCommentsCard()
        {
            var panel = new Panel
            {
                Width = 150,
                AutoSize = true,
                MinimumSize = new Size(150, 0),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(3),
                Padding = new Padding(4),
                BackColor = Color.FromArgb(45, 45, 48) // slightly different to signal locked
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            var lblMargin = new Padding(0, 0, 0, 0);
            var ctrlMargin = new Padding(0, 0, 0, 2);

            // Row 0: "Token" label
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new KryptonLabel { Text = "Token", AutoSize = true, Margin = lblMargin }, 0, 0);

            // Row 1: locked token text
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var txtToken = new KryptonTextBox
            {
                Text = "Comments",
                Dock = DockStyle.Fill,
                Margin = ctrlMargin,
                ReadOnly = true,
                Enabled = false
            };
            layout.Controls.Add(txtToken, 0, 1);

            // Row 2: "Column name" label
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new KryptonLabel { Text = "Column name", AutoSize = true, Margin = lblMargin }, 0, 2);

            // Row 3: locked header text
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var txtHeader = new KryptonTextBox
            {
                Text = "COMMENTS",
                Dock = DockStyle.Fill,
                Margin = ctrlMargin,
                ReadOnly = true,
                Enabled = false
            };
            layout.Controls.Add(txtHeader, 0, 3);

            // Row 4: note
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var noteLabel = new KryptonLabel
            {
                Text = "(auto-appended\nfor filtering)",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0),
                LabelStyle = LabelStyle.NormalPanel
            };
            noteLabel.StateCommon.ShortText.Font = new Font(
                (SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, 7.5f, FontStyle.Italic);
            noteLabel.StateCommon.ShortText.MultiLine = InheritBool.True;
            layout.Controls.Add(noteLabel, 0, 4);

            panel.Controls.Add(layout);
            return panel;
        }

        // ---------------------------------------------------------------
        // Events
        // ---------------------------------------------------------------

        public override void WireEvents()
        {
            // Global toggles
            _chkExportTopAsmStep.CheckedChanged += (_, __) => Changed();
            _chkExportBomExcel.CheckedChanged += (_, __) => Changed();
            _chkExportMasterPdf.CheckedChanged += (_, __) => Changed();
            // List selection
            _list.SelectedIndexChanged += (_, __) =>
            {
                if (_suppressListEvents) return;
                LoadSelectedToDetail();
            };

            // Add / Remove / Reset
            _btnAdd.Click += (_, __) =>
            {
                var row = new CategoryRow
                {
                    OriginalCategory = "",
                    Category = "NEW",
                    FolderName = "NEW",
                    PackageDrawings = true,
                    ExportSTEP = true,
                    DrawingTemplate = "<Part.Part Number>",
                    DxfTemplate = "<Part.Part Number>",
                    StepTemplate = "<Part.Part Number>",
                    BomTemplate = "<Part.Part Number>"
                };

                _rows.Add(row);
                _list.Items.Add(row.Category);
                _list.SelectedIndex = _list.Items.Count - 1;
                Host.MarkChanged();
                _txtCategory.Focus();
                _txtCategory.SelectAll();
            };

            _btnRemove.Click += (_, __) =>
            {
                int idx = _list.SelectedIndex;
                if (idx < 0 || idx >= _rows.Count) return;

                var row = _rows[idx];
                if (MessageBox.Show(Host.Owner, $"Remove category '{row.Category}'?", "Exporter",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                _rows.RemoveAt(idx);
                _list.Items.RemoveAt(idx);

                if (_list.Items.Count > 0)
                    _list.SelectedIndex = Math.Min(idx, _list.Items.Count - 1);
                else
                    LoadSelectedToDetail();

                Host.MarkChanged();
            };

            _btnReset.Click += (_, __) =>
            {
                if (MessageBox.Show(Host.Owner, "Reset exporter settings to defaults?", "Exporter",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                AddinSettings.Current.Exporting = ExportingSettings.CreateDefaults();
                LoadFromSettings();
                Host.MarkChanged();
            };

            // Detail -> row (identity)
            _txtCategory.TextChanged += (_, __) =>
            {
                if (_isLoading || _suppressDetailEvents) return;
                var row = GetSelectedRow();
                if (row == null) return;

                row.Category = (_txtCategory.Text ?? "").Trim();

                // Update list display without triggering SelectedIndexChanged
                _suppressListEvents = true;
                try
                {
                    int idx = _list.SelectedIndex;
                    if (idx >= 0 && idx < _list.Items.Count)
                        _list.Items[idx] = row.Category;
                }
                finally
                {
                    _suppressListEvents = false;
                }

                Host.MarkChanged();
            };

            _txtFolder.TextChanged += (_, __) =>
            {
                if (_isLoading || _suppressDetailEvents) return;
                var row = GetSelectedRow();
                if (row == null) return;

                row.FolderName = (_txtFolder.Text ?? "").Trim();
                Host.MarkChanged();
            };

            // Detail -> row (actions)
            _chkDrawings.CheckedChanged += (_, __) => WriteActionToRow();
            _chkDXF.CheckedChanged += (_, __) => WriteActionToRow();
            _chkDXFPost.CheckedChanged += (_, __) => WriteActionToRow();
            _chkSTEP.CheckedChanged += (_, __) => WriteActionToRow();
            _chkBOM.CheckedChanged += (_, __) => WriteActionToRow();

            // Naming template editors (per-category)
            _btnNameDrawings.Click += (_, __) => EditTemplate("Edit Drawings naming", r => r.DrawingTemplate, (r, v) => r.DrawingTemplate = v, _txtNameDrawings);
            _btnNameDxf.Click += (_, __) => EditTemplate("Edit DXF naming", r => r.DxfTemplate, (r, v) => r.DxfTemplate = v, _txtNameDxf);
            _btnNameStep.Click += (_, __) => EditTemplate("Edit STEP naming", r => r.StepTemplate, (r, v) => r.StepTemplate = v, _txtNameStep);
            _btnNameBom.Click += (_, __) => EditTemplate("Edit BOM naming", r => r.BomTemplate, (r, v) => r.BomTemplate = v, _txtNameBom);

            // Top-level naming template editors
            _btnTopStepName.Click += (_, __) => EditTopLevelTemplate("Edit top-level STEP naming", _txtTopStepName, "<Top.Part Number>");
            _btnTopBomName.Click += (_, __) => EditTopLevelTemplate("Edit top-level BOM naming", _txtTopBomName, "<Top.Part Number>_Rev<Top.Revision Number>_BOM");
            _btnTopDrawingName.Click += (_, __) => EditTopLevelTemplate("Edit top-level Drawing naming", _txtTopDrawingName, "<Top.Project> A-01 REV<Top.Revision Number> Construction Drawings");

            // BOM column Add / Remove
            _btnAddBomCol.Click += (_, __) =>
            {
                var card = CreateBomColumnCard();
                _bomCards.Add(card);
                _bomColumnsHost.Controls.Add(card.CardPanel);
                RebuildCommentsCard();
                Host.MarkChanged();
            };

            _btnRemoveBomCol.Click += (_, __) =>
            {
                if (_bomCards.Count == 0) return;
                var last = _bomCards[_bomCards.Count - 1];
                _bomCards.RemoveAt(_bomCards.Count - 1);
                _bomColumnsHost.Controls.Remove(last.CardPanel);
                last.CardPanel.Dispose();
                RebuildCommentsCard();
                Host.MarkChanged();
            };
        }

        // ---------------------------------------------------------------
        // Settings load / apply
        // ---------------------------------------------------------------

        public override void LoadFromSettings()
        {
            _isLoading = true;
            try
            {
                var e = AddinSettings.Current.Exporting;
                e.AlwaysOverwrite = true;

                _chkExportTopAsmStep.Checked = e.ExportTopLevelAssemblySTEP;
                _chkExportBomExcel.Checked = e.ExportBOMExcel;
                _chkExportMasterPdf.Checked = e.ExportMasterPDF;

                _txtTopStepName.Text = e.TopLevelStepTemplate ?? "<Top.Part Number>";
                _txtTopBomName.Text = e.TopLevelBomTemplate ?? "<Top.Part Number>_Rev<Top.Revision Number>_BOM";
                _txtTopDrawingName.Text = e.TopLevelDrawingTemplate ?? "<Top.Project> A-01 REV<Top.Revision Number> Construction Drawings";

                _rows.Clear();

                foreach (var kv in e.ProfilesByComment
                    .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var key = (kv.Key ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    var prof = kv.Value ?? new ExportProfileSettings();
                    prof.SanitizeInPlace();

                    var naming = prof.Naming ?? ExportNamingSettings.CreateDefaults();
                    naming.SanitizeInPlace();

                    _rows.Add(new CategoryRow
                    {
                        OriginalCategory = key,
                        Category = key,
                        FolderName = prof.FolderName ?? "",
                        PackageDrawings = prof.PackageDrawings,
                        ExportDXF = prof.ExportDXF,
                        DxfPostProcessing = prof.DxfPostProcessing,
                        ExportSTEP = prof.ExportSTEP,
                        ExportBOM = prof.ExportBOM,
                        DrawingTemplate = naming.DrawingTemplate ?? "<Part.Part Number>",
                        DxfTemplate = naming.DxfTemplate ?? "<Part.Part Number>",
                        StepTemplate = naming.StepTemplate ?? "<Part.Part Number>",
                        BomTemplate = naming.BomTemplate ?? "<Part.Part Number>"
                    });
                }

                RefreshList();

                // Load BOM columns
                _bomCards.Clear();
                _bomColumnsHost.Controls.Clear();
                var bomCols = e.BomColumns ?? ExportingSettings.CreateDefaultBomColumns();
                foreach (var col in bomCols)
                {
                    var card = CreateBomColumnCard(col);
                    _bomCards.Add(card);
                    _bomColumnsHost.Controls.Add(card.CardPanel);
                }
                RebuildCommentsCard();
            }
            finally
            {
                _isLoading = false;
            }
        }

        public override void ApplyToSettings()
        {
            var e = AddinSettings.Current.Exporting;
            e.AlwaysOverwrite = true;

            e.ExportTopLevelAssemblySTEP = _chkExportTopAsmStep.Checked;
            e.ExportBOMExcel = _chkExportBomExcel.Checked;
            e.ExportMasterPDF = _chkExportMasterPdf.Checked;

            e.TopLevelStepTemplate = (_txtTopStepName.Text ?? "").Trim();
            e.TopLevelBomTemplate = (_txtTopBomName.Text ?? "").Trim();
            e.TopLevelDrawingTemplate = (_txtTopDrawingName.Text ?? "").Trim();

            var dict = new Dictionary<string, ExportProfileSettings>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in _rows)
            {
                var key = (row.Category ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;

                dict[key] = new ExportProfileSettings
                {
                    FolderName = (row.FolderName ?? "").Trim(),
                    PackageDrawings = row.PackageDrawings,
                    ExportDXF = row.ExportDXF,
                    DxfPostProcessing = row.DxfPostProcessing,
                    ExportSTEP = row.ExportSTEP,
                    ExportBOM = row.ExportBOM,
                    Enabled = true,
                    Naming = new ExportNamingSettings
                    {
                        DrawingTemplate = string.IsNullOrWhiteSpace(row.DrawingTemplate) ? "<Part.Part Number>" : row.DrawingTemplate,
                        DxfTemplate = string.IsNullOrWhiteSpace(row.DxfTemplate) ? "<Part.Part Number>" : row.DxfTemplate,
                        StepTemplate = string.IsNullOrWhiteSpace(row.StepTemplate) ? "<Part.Part Number>" : row.StepTemplate,
                        BomTemplate = string.IsNullOrWhiteSpace(row.BomTemplate) ? "<Part.Part Number>" : row.BomTemplate
                    }
                };
            }

            e.ProfilesByComment = dict;

            // Save BOM columns
            var bomCols = new List<BomColumnDefinition>();
            foreach (var card in _bomCards)
            {
                var tokenName = GetCardTokenName(card);
                if (string.IsNullOrWhiteSpace(tokenName)) continue;

                bomCols.Add(new BomColumnDefinition
                {
                    TokenName = tokenName,
                    Header = (card.HeaderText.Text ?? "").Trim(),
                    FormattingEnabled = card.FormatCheck.Checked,
                    NumberFormat = card.FormatCombo.SelectedItem?.ToString() ?? "0.0",
                    StripUnit = card.StripUnitCheck.Checked
                });
            }
            e.BomColumns = bomCols.Count > 0 ? bomCols : ExportingSettings.CreateDefaultBomColumns();

            e.SanitizeInPlace();
        }

        public override void Validate(List<string> warnings, List<string> errors)
        {
            foreach (var row in _rows)
            {
                if (string.IsNullOrWhiteSpace(row.Category))
                    errors.Add("Exporter: a category has a blank name.");
            }

            var dupes = _rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Category))
                .GroupBy(r => r.Category.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var d in dupes)
                errors.Add($"Exporter: duplicate category '{d}'.");
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private void Changed()
        {
            if (_isLoading) return;
            OnChanged();
        }

        private CategoryRow? GetSelectedRow()
        {
            int i = _list.SelectedIndex;
            return i >= 0 && i < _rows.Count ? _rows[i] : null;
        }

        private void RefreshList()
        {
            int sel = _list.SelectedIndex;
            _list.Items.Clear();

            foreach (var row in _rows)
                _list.Items.Add(row.Category);

            if (sel >= 0 && sel < _list.Items.Count)
                _list.SelectedIndex = sel;
            else if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
            else
                LoadSelectedToDetail();
        }

        private void LoadSelectedToDetail()
        {
            var row = GetSelectedRow();

            _suppressDetailEvents = true;
            try
            {
                if (row == null)
                {
                    SetDetailEnabled(false);
                    ClearDetail();
                    return;
                }

                SetDetailEnabled(true);

                _txtCategory.Text = row.Category ?? "";
                _txtFolder.Text = row.FolderName ?? "";

                _chkDrawings.Checked = row.PackageDrawings;
                _chkDXF.Checked = row.ExportDXF;
                _chkDXFPost.Checked = row.DxfPostProcessing;
                _chkSTEP.Checked = row.ExportSTEP;
                _chkBOM.Checked = row.ExportBOM;

                _txtNameDrawings.Text = row.DrawingTemplate ?? "<Part.Part Number>";
                _txtNameDxf.Text = row.DxfTemplate ?? "<Part.Part Number>";
                _txtNameStep.Text = row.StepTemplate ?? "<Part.Part Number>";
                _txtNameBom.Text = row.BomTemplate ?? "<Part.Part Number>";
            }
            finally
            {
                _suppressDetailEvents = false;
            }
        }

        private void WriteActionToRow()
        {
            if (_isLoading || _suppressDetailEvents) return;
            var row = GetSelectedRow();
            if (row == null) return;

            row.PackageDrawings = _chkDrawings.Checked;
            row.ExportDXF = _chkDXF.Checked;
            row.DxfPostProcessing = _chkDXFPost.Checked;
            row.ExportSTEP = _chkSTEP.Checked;
            row.ExportBOM = _chkBOM.Checked;

            Host.MarkChanged();
        }

        private void EditTemplate(string title, Func<CategoryRow, string> getter,
            Action<CategoryRow, string> setter, KryptonTextBox mirror)
        {
            var row = GetSelectedRow();
            if (row == null) return;

            var current = getter(row);
            var seed = string.IsNullOrWhiteSpace(current) ? "<Part.Part Number>" : current;

            using var dlg = new ExporterNamingTemplateDialog(title, seed);
            if (dlg.ShowDialog(Host.Owner) != DialogResult.OK) return;

            var val = string.IsNullOrWhiteSpace(dlg.Template) ? "<Part.Part Number>" : dlg.Template;
            setter(row, val);
            mirror.Text = val;

            Host.MarkChanged();
            Changed();
        }

        private void SetDetailEnabled(bool enabled)
        {
            _txtCategory.Enabled = enabled;
            _txtFolder.Enabled = enabled;
            _chkDrawings.Enabled = enabled;
            _chkDXF.Enabled = enabled;
            _chkDXFPost.Enabled = enabled;
            _chkSTEP.Enabled = enabled;
            _chkBOM.Enabled = enabled;
            _btnNameDrawings.Enabled = enabled;
            _btnNameDxf.Enabled = enabled;
            _btnNameStep.Enabled = enabled;
            _btnNameBom.Enabled = enabled;
        }

        private void ClearDetail()
        {
            _txtCategory.Text = "";
            _txtFolder.Text = "";
            _chkDrawings.Checked = false;
            _chkDXF.Checked = false;
            _chkDXFPost.Checked = false;
            _chkSTEP.Checked = false;
            _chkBOM.Checked = false;
            _txtNameDrawings.Text = "";
            _txtNameDxf.Text = "";
            _txtNameStep.Text = "";
            _txtNameBom.Text = "";
        }

        private void EditTopLevelTemplate(string title, KryptonTextBox mirror, string defaultTemplate)
        {
            var current = (mirror.Text ?? "").Trim();
            var seed = string.IsNullOrWhiteSpace(current) ? defaultTemplate : current;

            using var dlg = new ExporterNamingTemplateDialog(title, seed);
            if (dlg.ShowDialog(Host.Owner) != DialogResult.OK) return;

            var val = string.IsNullOrWhiteSpace(dlg.Template) ? defaultTemplate : dlg.Template;
            mirror.Text = val;

            Host.MarkChanged();
            Changed();
        }
    }
}
