using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.UI.Dialogs
{
    internal enum TokenScope
    {
        PartProperties,
        TopLevelAssemblyProperties
    }

    internal sealed class TemplateTokenEditorDialog : KryptonForm
    {
        private readonly KryptonTextBox _txtTemplate = new()
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Dock = DockStyle.Fill
        };

        private readonly KryptonComboBox _cmbScope = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill
        };

        private readonly KryptonComboBox _cmbToken = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill
        };

        // NOTE: PlaceholderText not available on KryptonTextBox; was previously:
        //   "Property Set name (e.g. Design Tracking Properties)"
        private readonly KryptonTextBox _txtCustomSet = new()
        {
            Dock = DockStyle.Fill,
            Visible = false
        };

        // NOTE: PlaceholderText not available on KryptonTextBox; was previously:
        //   "Property name (e.g. Description)"
        private readonly KryptonTextBox _txtCustomProp = new()
        {
            Dock = DockStyle.Fill,
            Visible = false
        };

        private readonly KryptonButton _btnInsert = new() { Text = "Insert", AutoSize = true };
        private readonly KryptonButton _btnQty = new() { Text = "QTY", AutoSize = true };

        private readonly KryptonButton _btnOk = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        private readonly KryptonButton _btnCancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

        public string Template => _txtTemplate.Text;

        public TemplateTokenEditorDialog(string title, string currentTemplate, TokenScope defaultScope = TokenScope.PartProperties)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(860, 520);

            _txtTemplate.Text = string.IsNullOrWhiteSpace(currentTemplate) ? "<Part Number>" : currentTemplate;

            BuildUi();

            _cmbScope.Items.Add("Part Properties");
            _cmbScope.Items.Add("Top Level Assembly Properties");
            _cmbScope.SelectedIndex = defaultScope == TokenScope.TopLevelAssemblyProperties ? 1 : 0;

            PopulateTokens();

            _cmbToken.SelectedIndexChanged += (_, __) =>
            {
                var def = _cmbToken.SelectedItem as TokenDef;
                bool custom = def != null && def.Kind == TokenKind.CustomIProperty;

                _txtCustomSet.Visible = custom;
                _txtCustomProp.Visible = custom;

                if (custom)
                {
                    _txtCustomSet.Focus();
                    _txtCustomSet.SelectAll();
                }
            };

            _btnInsert.Click += (_, __) => InsertSelected();
            _btnQty.Click += (_, __) => InsertTextAtCaret("<Qty>");

            _cmbToken.DoubleClick += (_, __) => InsertSelected();

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            _btnOk.Click += (_, __) =>
            {
                if (string.IsNullOrWhiteSpace(_txtTemplate.Text))
                    _txtTemplate.Text = "<Part Number>";
            };
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 6,
                RowCount = 3,
                AutoSize = true
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // scope label
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));  // scope combo
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16)); // spacer
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // token label
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));  // token combo + custom fields
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // buttons

            top.Controls.Add(new KryptonLabel { Text = "Property source:", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 0);
            top.Controls.Add(_cmbScope, 1, 0);

            top.Controls.Add(new KryptonLabel { Text = "Token:", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 3, 0);
            top.Controls.Add(_cmbToken, 4, 0);

            var btnPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(8, 0, 0, 0)
            };
            btnPanel.Controls.Add(_btnInsert);
            btnPanel.Controls.Add(_btnQty);
            top.Controls.Add(btnPanel, 5, 0);

            // custom fields under token picker
            top.Controls.Add(_txtCustomSet, 4, 1);
            top.Controls.Add(_txtCustomProp, 4, 2);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false
            };
            bottom.Controls.Add(_btnOk);
            bottom.Controls.Add(_btnCancel);

            root.Controls.Add(top, 0, 0);
            root.Controls.Add(_txtTemplate, 0, 1);
            root.Controls.Add(bottom, 0, 2);

            Controls.Add(root);
        }

        private void PopulateTokens()
        {
            _cmbToken.BeginUpdate();
            try
            {
                _cmbToken.Items.Clear();
                foreach (var def in TokenCatalog.All)
                    _cmbToken.Items.Add(def);

                _cmbToken.DisplayMember = nameof(TokenDef.DisplayName);
                _cmbToken.SelectedItem = TokenCatalog.All.FirstOrDefault(t => t.DisplayName.StartsWith("Part Number"))
                                         ?? TokenCatalog.All.FirstOrDefault();
            }
            finally
            {
                _cmbToken.EndUpdate();
            }
        }

        private void InsertSelected()
        {
            var def = _cmbToken.SelectedItem as TokenDef;
            if (def == null) return;

            string tokenToInsert;

            if (def.Kind == TokenKind.CustomIProperty)
            {
                var setName = (_txtCustomSet.Text ?? "").Trim();
                var propName = (_txtCustomProp.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(setName) || string.IsNullOrWhiteSpace(propName))
                    return;

                tokenToInsert = WrapWithScope($"<iProp:{setName}|{propName}>");
            }
            else if (def.Kind == TokenKind.IProperty)
            {
                tokenToInsert = WrapWithScope(def.TokenText);
            }
            else
            {
                // Built-ins are scope-less and handled by formatter directly
                tokenToInsert = def.TokenText;
            }

            InsertTextAtCaret(tokenToInsert);
        }

        private string WrapWithScope(string baseToken)
        {
            // baseToken formats:
            //  - Built-in: "<Part Number>" (we normally don't call this for built-ins)
            //  - iProp:    "<iProp:Set|Name>"
            //
            // We will emit:
            //  - "<Part.{...}>"
            //  - "<Top.{...}>"

            if (string.IsNullOrWhiteSpace(baseToken)) return baseToken;

            string scope = (_cmbScope.SelectedIndex == 1) ? "Top" : "Part";

            if (baseToken.StartsWith("<", StringComparison.Ordinal) && baseToken.EndsWith(">", StringComparison.Ordinal))
            {
                var inner = baseToken.Substring(1, baseToken.Length - 2);

                // Convert iProp:Set|Name -> Set.Name (safer for later parsing)
                if (inner.StartsWith("iProp:", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = inner.Substring("iProp:".Length);
                    // payload = "Set|Name"
                    var parts = payload.Split('|');
                    if (parts.Length == 2)
                    {
                        var setName = parts[0].Trim();
                        var propName = parts[1].Trim();
                        return $"<{scope}.{setName}.{propName}>";
                    }

                    // Fallback: keep raw if unexpected
                    return $"<{scope}.{inner}>";
                }

                // Otherwise: treat inner as a property name token, e.g. "Part Number"
                return $"<{scope}.{inner}>";
            }

            return baseToken;
        }


        private void InsertTextAtCaret(string insert)
        {
            if (string.IsNullOrEmpty(insert)) return;

            int start = _txtTemplate.SelectionStart;
            string text = _txtTemplate.Text ?? string.Empty;

            _txtTemplate.Text = text.Insert(start, insert);
            _txtTemplate.SelectionStart = start + insert.Length;
            _txtTemplate.SelectionLength = 0;
            _txtTemplate.Focus();
        }
    }
}
