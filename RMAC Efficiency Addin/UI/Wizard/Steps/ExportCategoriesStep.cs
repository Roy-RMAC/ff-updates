using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Krypton.Toolkit;
using RMAC_Efficiency_Addin.Settings;

namespace RMAC_Efficiency_Addin.UI.Wizard.Steps
{
    internal sealed class ExportCategoriesStep : WizardStepBase
    {
        public override string StepTitle => "Export Categories";
        public override string StepDescription => "Define manufacturing process categories and their export actions";

        private Panel? _panel;
        private KryptonListBox? _list;
        private KryptonTextBox? _txtCategory;
        private KryptonTextBox? _txtFolder;
        private KryptonCheckBox? _chkDrawings;
        private KryptonCheckBox? _chkDXF;
        private KryptonCheckBox? _chkDXFPost;
        private KryptonCheckBox? _chkSTEP;
        private KryptonCheckBox? _chkBOM;
        private bool _suppressEvents;

        private sealed class CategoryRow
        {
            public string Category { get; set; } = "";
            public string FolderName { get; set; } = "";
            public bool PackageDrawings { get; set; }
            public bool ExportDXF { get; set; }
            public bool DxfPostProcessing { get; set; }
            public bool ExportSTEP { get; set; }
            public bool ExportBOM { get; set; }
            // Preserve naming templates from existing settings
            public ExportNamingSettings? Naming { get; set; }
        }

        private readonly List<CategoryRow> _rows = new();

        public override Control BuildPanel()
        {
            if (_panel != null) return _panel;

            _panel = new Panel { Dock = DockStyle.Fill };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(0, 4, 0, 0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var info = MakeLabel(
                "Define export categories. Each category maps to a Comments field value " +
                "and controls which export actions run for those components.");
            info.Margin = new Padding(0, 0, 0, 8);
            layout.Controls.Add(info, 0, 0);
            layout.SetColumnSpan(info, 2);

            // Left: category list
            var leftPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0)
            };
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _list = new KryptonListBox { Dock = DockStyle.Fill };
            leftPanel.Controls.Add(_list, 0, 0);

            var listButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 6, 0, 0)
            };
            var btnAdd = new KryptonButton { Text = "Add", AutoSize = true };
            var btnRemove = new KryptonButton { Text = "Remove", AutoSize = true };
            listButtons.Controls.Add(btnAdd);
            listButtons.Controls.Add(btnRemove);
            leftPanel.Controls.Add(listButtons, 0, 1);

            layout.Controls.Add(leftPanel, 0, 1);

            // Right: detail panel
            var detail = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 9,
                Padding = new Padding(12, 0, 0, 0)
            };
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 9; i++)
                detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            int row = 0;
            detail.Controls.Add(MakeLabel("Category name:"), 0, row);
            _txtCategory = new KryptonTextBox { Dock = DockStyle.Fill };
            detail.Controls.Add(_txtCategory, 1, row++);

            detail.Controls.Add(MakeLabel("Folder name:"), 0, row);
            _txtFolder = new KryptonTextBox { Dock = DockStyle.Fill };
            detail.Controls.Add(_txtFolder, 1, row++);

            // Spacer
            detail.Controls.Add(new Panel { Height = 8 }, 0, row++);

            var actionsLabel = MakeHeading("Export Actions");
            detail.Controls.Add(actionsLabel, 0, row);
            detail.SetColumnSpan(actionsLabel, 2);
            row++;

            _chkDrawings = new KryptonCheckBox { Text = "Package Drawings (PDF)", AutoSize = true };
            detail.Controls.Add(_chkDrawings, 0, row);
            detail.SetColumnSpan(_chkDrawings, 2);
            row++;

            _chkDXF = new KryptonCheckBox { Text = "Export DXF (flat patterns)", AutoSize = true };
            detail.Controls.Add(_chkDXF, 0, row);
            detail.SetColumnSpan(_chkDXF, 2);
            row++;

            _chkDXFPost = new KryptonCheckBox { Text = "DXF Post Processing", AutoSize = true };
            detail.Controls.Add(_chkDXFPost, 0, row);
            detail.SetColumnSpan(_chkDXFPost, 2);
            row++;

            _chkSTEP = new KryptonCheckBox { Text = "Export STEP (3D models)", AutoSize = true };
            detail.Controls.Add(_chkSTEP, 0, row);
            detail.SetColumnSpan(_chkSTEP, 2);
            row++;

            _chkBOM = new KryptonCheckBox { Text = "Export BOM (Excel)", AutoSize = true };
            detail.Controls.Add(_chkBOM, 0, row);
            detail.SetColumnSpan(_chkBOM, 2);

            layout.Controls.Add(detail, 1, 1);

            // Wire events
            _list.SelectedIndexChanged += (_, _) => ShowSelectedCategory();
            btnAdd.Click += (_, _) => AddCategory();
            btnRemove.Click += (_, _) => RemoveCategory();

            _txtCategory.TextChanged += (_, _) => SaveDetailToRow();
            _txtFolder.TextChanged += (_, _) => SaveDetailToRow();
            _chkDrawings.CheckedChanged += (_, _) => SaveDetailToRow();
            _chkDXF.CheckedChanged += (_, _) => SaveDetailToRow();
            _chkDXFPost.CheckedChanged += (_, _) => SaveDetailToRow();
            _chkSTEP.CheckedChanged += (_, _) => SaveDetailToRow();
            _chkBOM.CheckedChanged += (_, _) => SaveDetailToRow();

            _panel.Controls.Add(layout);
            return _panel;
        }

        public override void LoadFromSettings()
        {
            _rows.Clear();
            var profiles = AddinSettings.Current.Exporting.ProfilesByComment;
            if (profiles != null)
            {
                foreach (var kvp in profiles)
                {
                    var p = kvp.Value;
                    _rows.Add(new CategoryRow
                    {
                        Category = kvp.Key,
                        FolderName = p.FolderName,
                        PackageDrawings = p.PackageDrawings,
                        ExportDXF = p.ExportDXF,
                        DxfPostProcessing = p.DxfPostProcessing,
                        ExportSTEP = p.ExportSTEP,
                        ExportBOM = p.ExportBOM,
                        Naming = p.Naming
                    });
                }
            }

            RefreshList();
            if (_list != null && _list.Items.Count > 0)
                _list.SelectedIndex = 0;
        }

        public override void ApplyToSettings()
        {
            var profiles = new Dictionary<string, ExportProfileSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in _rows)
            {
                var key = r.Category.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (profiles.ContainsKey(key)) continue;

                profiles[key] = new ExportProfileSettings
                {
                    FolderName = string.IsNullOrWhiteSpace(r.FolderName) ? key : r.FolderName.Trim(),
                    PackageDrawings = r.PackageDrawings,
                    ExportDXF = r.ExportDXF,
                    DxfPostProcessing = r.DxfPostProcessing,
                    ExportSTEP = r.ExportSTEP,
                    ExportBOM = r.ExportBOM,
                    Naming = r.Naming ?? ExportNamingSettings.CreateDefaults()
                };
            }
            AddinSettings.Current.Exporting.ProfilesByComment = profiles;
        }

        public override bool Validate(out string? error)
        {
            if (_rows.Count == 0 || _rows.All(r => string.IsNullOrWhiteSpace(r.Category)))
            {
                error = "At least one export category is required.";
                return false;
            }
            error = null;
            return true;
        }

        private void RefreshList()
        {
            if (_list == null) return;
            _suppressEvents = true;
            int sel = _list.SelectedIndex;
            _list.Items.Clear();
            foreach (var r in _rows)
                _list.Items.Add(r.Category);
            if (sel >= 0 && sel < _list.Items.Count)
                _list.SelectedIndex = sel;
            else if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
            _suppressEvents = false;
            ShowSelectedCategory();
        }

        private void ShowSelectedCategory()
        {
            if (_suppressEvents || _list == null) return;
            int idx = _list.SelectedIndex;
            bool valid = idx >= 0 && idx < _rows.Count;

            _suppressEvents = true;
            _txtCategory!.Text = valid ? _rows[idx].Category : "";
            _txtFolder!.Text = valid ? _rows[idx].FolderName : "";
            _chkDrawings!.Checked = valid && _rows[idx].PackageDrawings;
            _chkDXF!.Checked = valid && _rows[idx].ExportDXF;
            _chkDXFPost!.Checked = valid && _rows[idx].DxfPostProcessing;
            _chkSTEP!.Checked = valid && _rows[idx].ExportSTEP;
            _chkBOM!.Checked = valid && _rows[idx].ExportBOM;

            _txtCategory.Enabled = valid;
            _txtFolder.Enabled = valid;
            _chkDrawings.Enabled = valid;
            _chkDXF.Enabled = valid;
            _chkDXFPost.Enabled = valid;
            _chkSTEP.Enabled = valid;
            _chkBOM.Enabled = valid;
            _suppressEvents = false;
        }

        private void SaveDetailToRow()
        {
            if (_suppressEvents || _list == null) return;
            int idx = _list.SelectedIndex;
            if (idx < 0 || idx >= _rows.Count) return;

            var r = _rows[idx];
            r.Category = _txtCategory!.Text.Trim();
            r.FolderName = _txtFolder!.Text.Trim();
            r.PackageDrawings = _chkDrawings!.Checked;
            r.ExportDXF = _chkDXF!.Checked;
            r.DxfPostProcessing = _chkDXFPost!.Checked;
            r.ExportSTEP = _chkSTEP!.Checked;
            r.ExportBOM = _chkBOM!.Checked;

            // Update list display
            _suppressEvents = true;
            _list.Items[idx] = r.Category;
            _suppressEvents = false;
        }

        private void AddCategory()
        {
            var newRow = new CategoryRow
            {
                Category = "NEW CATEGORY",
                FolderName = "NEW CATEGORY",
                PackageDrawings = true,
                ExportBOM = true
            };
            _rows.Add(newRow);
            RefreshList();
            if (_list != null)
                _list.SelectedIndex = _rows.Count - 1;
        }

        private void RemoveCategory()
        {
            if (_list == null) return;
            int idx = _list.SelectedIndex;
            if (idx < 0 || idx >= _rows.Count) return;
            _rows.RemoveAt(idx);
            RefreshList();
        }
    }
}
