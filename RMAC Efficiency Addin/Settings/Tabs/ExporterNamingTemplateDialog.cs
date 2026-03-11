// ExporterNamingTemplateDialog.cs
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.Settings.UI
{
    internal sealed class ExporterNamingTemplateDialog : KryptonForm
    {
        // ---- Theme ----
        private readonly KryptonManager _kryptonManager = new()
        {
            GlobalPaletteMode = PaletteMode.Microsoft365Black
        };

        // ---- UI ----
        private readonly KryptonTextBox _txtTemplate = new()
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Dock = DockStyle.Fill
        };

        private readonly KryptonComboBox _cmbScope = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        private readonly KryptonComboBox _cmbTokens = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        // Custom token (property name only)
        private readonly KryptonLabel _lblCustomProp = new()
        {
            Text = "Property:",
            AutoSize = true,
            Visible = false
        };

        private readonly KryptonTextBox _txtCustomProp = new()
        {
            Visible = false
        };

        private readonly KryptonButton _btnInsert = new() { Text = "Insert" };
        private readonly KryptonButton _btnQty = new() { Text = "QTY" };

        private readonly KryptonButton _btnOk = new() { Text = "OK", DialogResult = DialogResult.OK };
        private readonly KryptonButton _btnCancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // ---- Constants ----
        private const string CustomItem = "(Custom\u2026)";

        private readonly bool _allowBlank;

        public string Template => _txtTemplate.Text;

        public ExporterNamingTemplateDialog(string title, string currentTemplate, bool allowBlank = false)
        {
            _allowBlank = allowBlank;

            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(900, 480);
            Size = new Size(1150, 600);

            _txtTemplate.Text = (!allowBlank && string.IsNullOrWhiteSpace(currentTemplate))
                ? "<Part.Part Number>"
                : (currentTemplate ?? "");

            BuildDropdowns();
            BuildLayout();
            WireEvents();

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        // ---------------------------------------------------------------
        // Dropdown population
        // ---------------------------------------------------------------

        private void BuildDropdowns()
        {
            // Scope
            _cmbScope.Items.Clear();
            _cmbScope.Items.AddRange(new object[] { "Part", "Top Level Assembly" });
            _cmbScope.SelectedIndex = 0;

            // Tokens: flat list sorted alphabetically, Custom pinned at bottom
            _cmbTokens.Items.Clear();

            var sorted = IPropertyTokenRegistry.All.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var token in sorted)
                _cmbTokens.Items.Add(token);

            _cmbTokens.Items.Add(CustomItem);

            if (_cmbTokens.Items.Count > 0)
                _cmbTokens.SelectedIndex = 0;
        }

        // ---------------------------------------------------------------
        // Layout
        // ---------------------------------------------------------------

        private void BuildLayout()
        {
            const int ctrlHeight = 28;

            // ---- Root: 4 rows, single column ----
            // Row 0: Source/Token combos + Insert/QTY buttons + Custom property (all on one line)
            // Row 1: Template text area (fills remaining)
            // Row 2: Help note
            // Row 3: OK/Cancel buttons
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(10),
                AutoSize = false,
                BackColor = Color.Transparent
            };
            root.RowStyles.Clear();
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // toolbar row
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // template text
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // help note
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // OK/Cancel

            var comboMargin = new Padding(0, 3, 0, 3);
            var labelMargin = new Padding(0, 0, 6, 0);
            var btnMargin = new Padding(0, 3, 0, 3);

            // ---- Row 0: Source + Token + Insert + QTY + Custom property ----
            var toolbarRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 11,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 6),
                BackColor = Color.Transparent
            };
            toolbarRow.ColumnStyles.Clear();
            toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // 0: "Source:" label
            toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));  // 1: scope combo
            toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));   // 2: spacer
            toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // 3: "Token:" label
            toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // 4: token combo (fills)
            toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));    // 5: spacer
            toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // 6: Insert button
            toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 4));    // 7: spacer
            toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // 8: QTY button
            toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // 9: "Property:" label
            toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // 10: custom property textbox

            _cmbScope.AutoSize = false;
            _cmbScope.Height = ctrlHeight;
            _cmbScope.Margin = comboMargin;
            _cmbScope.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            _cmbTokens.AutoSize = false;
            _cmbTokens.Height = ctrlHeight;
            _cmbTokens.Margin = comboMargin;
            _cmbTokens.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _cmbTokens.DropDownWidth = 300;
            _cmbTokens.MaxDropDownItems = 20;

            _btnInsert.AutoSize = false;
            _btnInsert.Size = new Size(80, ctrlHeight);
            _btnInsert.Margin = btnMargin;

            _btnQty.AutoSize = false;
            _btnQty.Size = new Size(60, ctrlHeight);
            _btnQty.Margin = btnMargin;

            _lblCustomProp.Margin = new Padding(8, 4, 4, 0);
            _lblCustomProp.Anchor = AnchorStyles.Left;

            _txtCustomProp.AutoSize = false;
            _txtCustomProp.Size = new Size(160, ctrlHeight);
            _txtCustomProp.Margin = btnMargin;

            toolbarRow.Controls.Add(new KryptonLabel { Text = "Source:", AutoSize = true, Margin = labelMargin, Anchor = AnchorStyles.Left }, 0, 0);
            toolbarRow.Controls.Add(_cmbScope, 1, 0);
            toolbarRow.Controls.Add(new KryptonLabel { Text = "Token:", AutoSize = true, Margin = labelMargin, Anchor = AnchorStyles.Left }, 3, 0);
            toolbarRow.Controls.Add(_cmbTokens, 4, 0);
            toolbarRow.Controls.Add(_btnInsert, 6, 0);
            toolbarRow.Controls.Add(_btnQty, 8, 0);
            toolbarRow.Controls.Add(_lblCustomProp, 9, 0);
            toolbarRow.Controls.Add(_txtCustomProp, 10, 0);

            // ---- Row 2: Help note ----
            var helpNote = new KryptonLabel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 6, 0, 4),
                LabelStyle = LabelStyle.NormalPanel
            };
            helpNote.Values.Text =
                "Formatting:  <Token|NumberFormat|Multiplier|StripUnit>\r\n" +
                "  e.g.  <THICKNESS|0.0>  \u2192  1.6       <THICKNESS|0.0|254|N>  \u2192  406.4\r\n" +
                "  NumberFormat = C# format (0.0, 0.00, G)   Multiplier = scale factor   StripUnit = N to remove, Y to keep (default)";
            helpNote.StateCommon.ShortText.Font = new Font("Consolas", 8f);
            helpNote.StateCommon.ShortText.MultiLine = InheritBool.True;

            // ---- Row 4: OK / Cancel buttons ----
            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 0),
                BackColor = Color.Transparent
            };

            _btnOk.AutoSize = false;
            _btnOk.Size = new Size(90, 34);
            _btnOk.Margin = new Padding(0);

            _btnCancel.AutoSize = false;
            _btnCancel.Size = new Size(90, 34);
            _btnCancel.Margin = new Padding(0, 0, 8, 0);

            bottom.Controls.Add(_btnOk);
            bottom.Controls.Add(_btnCancel);

            // ---- Assemble ----
            root.Controls.Add(toolbarRow, 0, 0);
            root.Controls.Add(_txtTemplate, 0, 1);
            root.Controls.Add(helpNote, 0, 2);
            root.Controls.Add(bottom, 0, 3);

            Controls.Clear();
            Controls.Add(root);
        }

        // ---------------------------------------------------------------
        // Events
        // ---------------------------------------------------------------

        private void WireEvents()
        {
            _btnInsert.Click += (_, __) => InsertSelectedToken();
            _cmbTokens.DoubleClick += (_, __) => InsertSelectedToken();

            _btnQty.Click += (_, __) => InsertTextAtCaret("<Qty>");

            _cmbTokens.SelectedIndexChanged += (_, __) =>
            {
                bool isCustom = _cmbTokens.SelectedItem is string s && s == CustomItem;
                _lblCustomProp.Visible = isCustom;
                _txtCustomProp.Visible = isCustom;

                if (isCustom)
                {
                    if (string.IsNullOrWhiteSpace(_txtCustomProp.Text))
                        _txtCustomProp.Text = "THICKNESS";

                    _txtCustomProp.Focus();
                    _txtCustomProp.SelectAll();
                }
            };

            _btnOk.Click += (_, __) =>
            {
                if (!_allowBlank && string.IsNullOrWhiteSpace(_txtTemplate.Text))
                    _txtTemplate.Text = "<Part.Part Number>";

                if (string.Equals(_txtTemplate.Text.Trim(), "<Part Number>", StringComparison.OrdinalIgnoreCase))
                    _txtTemplate.Text = "<Part.Part Number>";
            };
        }

        private void InsertSelectedToken()
        {
            string scope = _cmbScope.SelectedIndex == 1 ? "Top" : "Part";

            // Custom
            if (_cmbTokens.SelectedItem is string s && s == CustomItem)
            {
                var propName = (_txtCustomProp.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(propName)) return;

                InsertTextAtCaret($"<{scope}.{propName}>");
                return;
            }

            // Registry token
            if (_cmbTokens.SelectedItem is not IPropertyToken token)
                return;

            if (token.IsScoped)
                InsertTextAtCaret($"<{scope}.{token.Name}>");
            else
                InsertTextAtCaret($"<{token.Name}>");
        }

        private void InsertTextAtCaret(string insert)
        {
            if (string.IsNullOrWhiteSpace(insert)) return;

            int start = _txtTemplate.SelectionStart;
            string text = _txtTemplate.Text ?? "";

            _txtTemplate.Text = text.Insert(start, insert);
            _txtTemplate.SelectionStart = start + insert.Length;
            _txtTemplate.SelectionLength = 0;
            _txtTemplate.Focus();
        }
    }
}
