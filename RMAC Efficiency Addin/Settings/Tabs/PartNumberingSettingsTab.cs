// UI/Settings/PartNumberingSettingsTab.cs
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
    /// Settings tab for part numbering.
    ///
    /// UI is intentionally simplified:
    /// - Prefix/Suffix templates are the only user-facing template inputs (Exporter-style read-only field + Edit…)
    /// - Legacy template + top-assembly template flags are hidden; we enforce defaults in ApplyToSettings.
    /// - Skip renumber gate is backwards-compatible with older saved selections (e.g. "Project.Project", "User Defined (Custom)").
    /// </summary>
    internal sealed class PartNumberingSettingsTab : SettingsTabBase
    {
        public PartNumberingSettingsTab(ISettingsTabHost host) : base(host) { }

        public override string TabTitle => "Part Numbering";

        private KryptonPage? _tab;

        // ---- Mode ----
        private readonly KryptonComboBox _cmbMode = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        // ---- Sheet ordering ----
        private readonly KryptonComboBox _cmbSheetOrder = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        // ---- Templates (Prefix/Suffix only) ----
        private readonly KryptonTextBox _txtPrefix = new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true
        };

        private readonly KryptonTextBox _txtSuffix = new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true
        };

        private readonly KryptonButton _btnEditPrefix = new() { Text = "Edit…" };
        private readonly KryptonButton _btnEditSuffix = new() { Text = "Edit…" };

        // ---- Skip gate ----
        private readonly KryptonCheckBox _chkSkipEnabled = new()
        {
            Text = "Enable Skip Renumber gate",
            AutoSize = true
        };

        private readonly KryptonComboBox _cmbSkipPropertySelection = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        private readonly KryptonTextBox _txtSkipCustomPropName = new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        private readonly KryptonTextBox _txtSkipMatchValue = new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        private readonly KryptonButton _btnResetDefaults = new() { Text = "Reset defaults" };

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

            if (_cmbMode.Items.Count == 0)
            {
                _cmbMode.Items.Add(PartNumberingMode.Structured);
                _cmbMode.Items.Add(PartNumberingMode.Sequential);
                _cmbMode.Items.Add(PartNumberingMode.FramesOnly);
            }

            if (_cmbSheetOrder.Items.Count == 0)
            {
                _cmbSheetOrder.Items.Add("Group by Assembly");
                _cmbSheetOrder.Items.Add("All Assemblies, then Parts");
            }

            // Build selection list from IPropertyTokenRegistry (just token names, no set prefix).
            // MapSkipSelection already handles bare property names via "search all sets" fallback.
            if (_cmbSkipPropertySelection.Items.Count == 0)
            {
                foreach (var group in IPropertyTokenRegistry.Groups)
                    foreach (var token in group.Tokens)
                        _cmbSkipPropertySelection.Items.Add(token.Name);

                _cmbSkipPropertySelection.Items.Add("Custom…");
            }

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // Wider right-column to accommodate 25% wider buttons (Reset/Edit) without DPI clipping.
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));

            int r = 0;

            // Match the exporter naming row geometry (prevents clipping on DPI + keeps vertical alignment)
            const int rowHeight = 34;
            const int ctrlHeight = 24;

            void NormalizeMiddle(Control c)
            {
                if (c is KryptonTextBox tb)
                {
                    tb.AutoSize = false;
                    tb.Height = ctrlHeight;
                    tb.Margin = new Padding(0, 5, 0, 5);
                    tb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                }
                else if (c is KryptonComboBox cb)
                {
                    cb.AutoSize = false;
                    cb.Height = ctrlHeight;
                    cb.Margin = new Padding(0, 5, 0, 5);
                    cb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                }
                else
                {
                    c.Margin = new Padding(0, 3, 0, 3);
                }
            }

            void NormalizeRight(Control c)
            {
                if (c is KryptonButton b)
                {
                    b.AutoSize = false;
                    b.Height = ctrlHeight;
                    // ~25% wider than the prior 110px.
                    b.Width = 140;
                    b.Anchor = AnchorStyles.Right;
                    b.Margin = new Padding(8, 5, 0, 5);
                }
                else
                {
                    c.Margin = new Padding(8, 2, 0, 2);
                }
            }

            void AddRow(string labelText, Control middle, Control? right = null, int? fixedHeight = null)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, fixedHeight ?? rowHeight));

                var lbl = new KryptonLabel
                {
                    Text = labelText,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 8, 0)
                };

                // For complex middle controls (e.g. nested layout panels), let them size naturally.
                if (middle is not FlowLayoutPanel && middle is not TableLayoutPanel)
                    NormalizeMiddle(middle);
                else
                {
                    middle.Dock = DockStyle.Fill;
                    middle.Margin = new Padding(0, 2, 0, 2);
                }

                layout.Controls.Add(lbl, 0, r);
                layout.Controls.Add(middle, 1, r);

                if (right != null)
                {
                    NormalizeRight(right);
                    layout.Controls.Add(right, 2, r);
                }
                else
                {
                    layout.Controls.Add(new Panel { Width = 1, Height = 1 }, 2, r);
                }

                r++;
            }

            AddRow("Part numbering mode", _cmbMode, _btnResetDefaults);
            AddRow("Sheet ordering", _cmbSheetOrder);
            AddRow("Prefix template", _txtPrefix, _btnEditPrefix);
            AddRow("Suffix template", _txtSuffix, _btnEditSuffix);

            // Skip gate — flat rows, same grid as everything else
            // Checkbox spans col 1+2 (label cell left empty)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
            layout.Controls.Add(new Panel { Width = 1, Height = 1 }, 0, r); // empty label cell
            layout.Controls.Add(_chkSkipEnabled, 1, r);
            layout.SetColumnSpan(_chkSkipEnabled, 2);
            r++;

            AddRow("Skip property", _cmbSkipPropertySelection);
            AddRow("Custom name", _txtSkipCustomPropName);
            AddRow("Match value", _txtSkipMatchValue);

            // Info
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(_txtInfo, 0, r);
            layout.SetColumnSpan(_txtInfo, 3);

            _txtInfo.Text =
                "Templates:\r\n" +
                "• Use Prefix + Suffix templates.\r\n" +
                "• Tokens match the exporter naming editor (Part/Top scope).\r\n\r\n" +
                "Skip gate:\r\n" +
                "• If enabled, documents whose selected iProperty equals Match Value will be skipped.\r\n" +
                "• Use 'Custom…' to target a custom iProperty name (User Defined Properties).";

            _tab.Controls.Add(layout);

            // Enable/disable custom name box depending selection
            UpdateSkipGateUiEnabled();

            return _tab;
        }

        public override void WireEvents()
        {
            _cmbMode.SelectedIndexChanged += (_, __) => OnChanged();
            _cmbSheetOrder.SelectedIndexChanged += (_, __) => OnChanged();

            // Prefix/suffix are edited via dialog; TextChanged still marks dirty.
            _txtPrefix.TextChanged += (_, __) => OnChanged();
            _txtSuffix.TextChanged += (_, __) => OnChanged();

            _btnEditPrefix.Click += (_, __) => EditTemplate("Edit Prefix template", _txtPrefix);
            _btnEditSuffix.Click += (_, __) => EditTemplate("Edit Suffix template", _txtSuffix);

            _chkSkipEnabled.CheckedChanged += (_, __) =>
            {
                UpdateSkipGateUiEnabled();
                OnChanged();
            };

            _cmbSkipPropertySelection.SelectedIndexChanged += (_, __) =>
            {
                UpdateSkipGateUiEnabled();
                OnChanged();
            };

            _txtSkipCustomPropName.TextChanged += (_, __) => OnChanged();
            _txtSkipMatchValue.TextChanged += (_, __) => OnChanged();

            _btnResetDefaults.Click += (_, __) =>
            {
                if (MessageBox.Show(
                        Host.Owner,
                        "Reset part numbering settings to defaults?",
                        "Part Numbering",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                _cmbMode.SelectedItem = PartNumberingMode.Structured;
                _cmbSheetOrder.SelectedIndex = (int)SheetOrderingMode.GroupByAssembly;
                _txtPrefix.Text = "";
                _txtSuffix.Text = "";

                // Restore the skip gate defaults (matching AddinSettings defaults)
                _chkSkipEnabled.Checked = true;
                _cmbSkipPropertySelection.SelectedItem = "Project";
                _txtSkipCustomPropName.Text = "";
                _txtSkipMatchValue.Text = "TEST";

                Host.MarkChanged();
            };
        }

        public override void LoadFromSettings()
        {
            var s = AddinSettings.Current;

            // Mode
            var idx = _cmbMode.Items.IndexOf(s.PartNumberingMode);
            _cmbMode.SelectedIndex = idx >= 0 ? idx : 0;

            // Sheet ordering
            int orderIdx = (int)s.SheetOrderingMode;
            _cmbSheetOrder.SelectedIndex = orderIdx >= 0 && orderIdx < _cmbSheetOrder.Items.Count ? orderIdx : 0;

            // Templates (Prefix/Suffix only)
            _txtPrefix.Text = s.PartNumberPrefixTemplate ?? "";
            _txtSuffix.Text = s.PartNumberSuffixTemplate ?? "";

            // Skip gate
            _chkSkipEnabled.Checked = s.SkipRenumberEnabled;

            var sel = s.SkipRenumberPropertySelection ?? "Project";
            SelectComboString(_cmbSkipPropertySelection, sel);

            _txtSkipCustomPropName.Text = s.SkipRenumberCustomPropertyName ?? "";
            _txtSkipMatchValue.Text = s.SkipRenumberMatchValue ?? "";

            UpdateSkipGateUiEnabled();
        }

        public override void ApplyToSettings()
        {
            var s = AddinSettings.Current;

            if (_cmbMode.SelectedItem is PartNumberingMode mode)
                s.PartNumberingMode = mode;

            s.SheetOrderingMode = (SheetOrderingMode)_cmbSheetOrder.SelectedIndex;

            s.PartNumberPrefixTemplate = (_txtPrefix.Text ?? "").Trim();
            s.PartNumberSuffixTemplate = (_txtSuffix.Text ?? "").Trim();

            // Legacy/flags are intentionally removed from UI.
            // Keep the legacy template sane for fallback behaviour.
            if (string.IsNullOrWhiteSpace(s.PartNumberTemplate))
                s.PartNumberTemplate = "<PartNumber>";

            // Remove these user-facing options: enforce defaults.
            s.PartNumberTemplateUseTopLevelAssemblyProperties = false;
            s.KeepTopLevelAssemblyPartNumberFixed = true;

            s.SkipRenumberEnabled = _chkSkipEnabled.Checked;

            // Normalize legacy selections into the current values expected by the renumberer.
            var sel = (_cmbSkipPropertySelection.SelectedItem?.ToString() ?? "Project").Trim();
            sel = NormalizeSkipSelection(sel);

            s.SkipRenumberPropertySelection = sel;
            s.SkipRenumberCustomPropertyName = (_txtSkipCustomPropName.Text ?? "").Trim();
            s.SkipRenumberMatchValue = (_txtSkipMatchValue.Text ?? "").Trim();
        }

        public override void Validate(List<string> warnings, List<string> errors)
        {
            // Prefix/suffix are allowed blank.

            // Skip gate validation
            if (_chkSkipEnabled.Checked)
            {
                var sel = NormalizeSkipSelection((_cmbSkipPropertySelection.SelectedItem?.ToString() ?? "").Trim());
                if (string.IsNullOrWhiteSpace(sel))
                    errors.Add("Part Numbering: Skip gate enabled, but Property selection is blank.");

                if (IsCustomSelection(sel))
                {
                    var cn = (_txtSkipCustomPropName.Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(cn))
                        errors.Add("Part Numbering: Skip gate uses 'Custom…' but Custom property name is blank.");
                }

                var mv = (_txtSkipMatchValue.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(mv))
                    warnings.Add("Part Numbering: Skip gate enabled but Match value is blank. This will rarely match anything.");
            }
        }

        private void UpdateSkipGateUiEnabled()
        {
            var enabled = _chkSkipEnabled.Checked;
            _cmbSkipPropertySelection.Enabled = enabled;

            var selRaw = (_cmbSkipPropertySelection.SelectedItem?.ToString() ?? "").Trim();
            var sel = NormalizeSkipSelection(selRaw);
            var isCustom = enabled && IsCustomSelection(selRaw) || (enabled && IsCustomSelection(sel));

            _txtSkipCustomPropName.Enabled = isCustom;
            _txtSkipMatchValue.Enabled = enabled;
        }

        private static bool IsCustomSelection(string sel)
        {
            sel = (sel ?? "").Trim();
            return string.Equals(sel, "Custom…", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sel, "User Defined (Custom)", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSkipSelection(string sel)
        {
            sel = (sel ?? "").Trim();

            // Legacy exact matches
            if (string.Equals(sel, "Project.Project", StringComparison.OrdinalIgnoreCase))
                return "Project";
            if (string.Equals(sel, "User Defined (Custom)", StringComparison.OrdinalIgnoreCase))
                return "Custom…";

            // Strip old "SetName.PropertyName" prefixes → just the property name
            string[] knownPrefixes = { "Design Tracking Properties.", "Summary Information.", "Document Summary Information." };
            foreach (var prefix in knownPrefixes)
            {
                if (sel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return sel.Substring(prefix.Length).Trim();
            }

            return sel;
        }

        private static void SelectComboString(KryptonComboBox combo, string value)
        {
            value ??= "";

            // Map legacy persisted values to their modern equivalents so DropDownList doesn't look blank.
            var normalized = NormalizeSkipSelection(value);

            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i]?.ToString(), normalized, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            // If still not found, pick a safe default so the UI doesn't appear to lose the user's settings.
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i]?.ToString(), "Project", StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        }

        private void EditTemplate(string title, KryptonTextBox target)
        {
            // Reuse the exporter naming dialog so token lists + scoping stay consistent.
            // allowBlank: true — Prefix/Suffix are intentionally blank for "no prefix/suffix".
            var seed = target.Text ?? "";
            using var dlg = new RMAC_Efficiency_Addin.Settings.UI.ExporterNamingTemplateDialog(title, seed, allowBlank: true);

            if (dlg.ShowDialog(Host.Owner) == DialogResult.OK)
            {
                target.Text = (dlg.Template ?? "").Trim(); // allow blank
                Host.MarkChanged();
            }
        }
    }
}
