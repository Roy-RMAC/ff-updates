using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Krypton.Toolkit;
using RMAC_Efficiency_Addin.Settings;

namespace RMAC_Efficiency_Addin.UI.Wizard.Steps
{
    internal sealed class BomColumnsStep : WizardStepBase
    {
        public override string StepTitle => "BOM Columns";
        public override string StepDescription => "Configure which columns appear in exported Excel BOM files";

        private Panel? _panel;
        private KryptonDataGridView? _grid;

        private sealed class ColumnRow
        {
            public string TokenName { get; set; } = "";
            public string Header { get; set; } = "";
            // Preserve formatting from existing settings (not shown in wizard)
            public bool FormattingEnabled { get; set; }
            public string NumberFormat { get; set; } = "0.0";
            public bool StripUnit { get; set; }
            // Fixed rows (like Comments) cannot be edited or removed
            public bool IsFixed { get; set; }
        }

        private readonly BindingList<ColumnRow> _rows = new();

        public override Control BuildPanel()
        {
            if (_panel != null) return _panel;

            _panel = new Panel { Dock = DockStyle.Fill };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0, 4, 0, 0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var info = MakeLabel(
                "Configure the columns for BOM Excel output. " +
                "The Comments column is always included as the last column. " +
                "You can add formatting options in full Settings later.");
            info.Margin = new Padding(0, 0, 0, 8);
            layout.Controls.Add(info, 0, 0);

            _grid = new KryptonDataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Property / Token",
                DataPropertyName = nameof(ColumnRow.TokenName),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50
            });

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Column Header",
                DataPropertyName = nameof(ColumnRow.Header),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50
            });

            _grid.DataSource = _rows;

            // Prevent editing fixed rows
            _grid.CellBeginEdit += (_, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < _rows.Count && _rows[e.RowIndex].IsFixed)
                    e.Cancel = true;
            };

            // Grey out fixed rows
            _grid.CellFormatting += (_, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < _rows.Count && _rows[e.RowIndex].IsFixed)
                {
                    e.CellStyle.ForeColor = Color.FromArgb(110, 110, 110);
                    e.CellStyle.SelectionForeColor = Color.FromArgb(110, 110, 110);
                }
            };

            layout.Controls.Add(_grid, 0, 1);

            // Buttons
            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 6, 0, 0)
            };

            var btnAdd = new KryptonButton { Text = "Add Column", AutoSize = true };
            var btnRemove = new KryptonButton { Text = "Remove", AutoSize = true };
            var btnUp = new KryptonButton { Text = "Move Up", AutoSize = true };
            var btnDown = new KryptonButton { Text = "Move Down", AutoSize = true };

            btnAdd.Click += (_, _) =>
            {
                // Insert before the fixed Comments row
                int insertAt = _rows.Count;
                for (int i = _rows.Count - 1; i >= 0; i--)
                {
                    if (_rows[i].IsFixed) insertAt = i;
                    else break;
                }
                var newRow = new ColumnRow { TokenName = "Part Number", Header = "PART NUMBER" };
                _rows.Insert(insertAt, newRow);
                _grid.ClearSelection();
                _grid.CurrentCell = _grid.Rows[insertAt].Cells[0];
                _grid.BeginEdit(true);
            };

            btnRemove.Click += (_, _) =>
            {
                if (_grid.CurrentRow == null) return;
                int idx = _grid.CurrentRow.Index;
                if (idx >= 0 && idx < _rows.Count && !_rows[idx].IsFixed)
                    _rows.RemoveAt(idx);
            };

            btnUp.Click += (_, _) => MoveRow(-1);
            btnDown.Click += (_, _) => MoveRow(1);

            buttons.Controls.Add(btnAdd);
            buttons.Controls.Add(btnRemove);
            buttons.Controls.Add(btnUp);
            buttons.Controls.Add(btnDown);
            layout.Controls.Add(buttons, 0, 2);

            _panel.Controls.Add(layout);
            return _panel;
        }

        public override void LoadFromSettings()
        {
            _rows.RaiseListChangedEvents = false;
            _rows.Clear();

            var cols = AddinSettings.Current.Exporting.BomColumns;
            if (cols != null)
            {
                foreach (var c in cols)
                {
                    _rows.Add(new ColumnRow
                    {
                        TokenName = c.TokenName,
                        Header = c.Header,
                        FormattingEnabled = c.FormattingEnabled,
                        NumberFormat = c.NumberFormat,
                        StripUnit = c.StripUnit
                    });
                }
            }

            // Always show Comments as the final fixed row
            _rows.Add(new ColumnRow
            {
                TokenName = "Comments",
                Header = "COMMENTS",
                IsFixed = true
            });

            _rows.RaiseListChangedEvents = true;
            _rows.ResetBindings();
        }

        public override void ApplyToSettings()
        {
            var result = new List<BomColumnDefinition>();
            foreach (var r in _rows)
            {
                if (r.IsFixed) continue; // Comments row is managed by the exporter
                if (string.IsNullOrWhiteSpace(r.TokenName)) continue;
                result.Add(new BomColumnDefinition
                {
                    TokenName = r.TokenName.Trim(),
                    Header = string.IsNullOrWhiteSpace(r.Header)
                        ? r.TokenName.Trim().ToUpperInvariant()
                        : r.Header.Trim(),
                    FormattingEnabled = r.FormattingEnabled,
                    NumberFormat = r.NumberFormat,
                    StripUnit = r.StripUnit
                });
            }
            AddinSettings.Current.Exporting.BomColumns = result;
        }

        public override bool Validate(out string? error)
        {
            var editableRows = _rows.Where(r => !r.IsFixed).ToList();
            if (editableRows.Count == 0 || editableRows.All(r => string.IsNullOrWhiteSpace(r.TokenName)))
            {
                error = "At least one BOM column is required.";
                return false;
            }
            error = null;
            return true;
        }

        private void MoveRow(int direction)
        {
            if (_grid == null || _grid.CurrentRow == null) return;
            int idx = _grid.CurrentRow.Index;
            if (idx >= 0 && idx < _rows.Count && _rows[idx].IsFixed) return; // can't move fixed rows

            int newIdx = idx + direction;
            if (newIdx < 0 || newIdx >= _rows.Count) return;
            if (_rows[newIdx].IsFixed) return; // can't swap with fixed row

            var item = _rows[idx];
            _rows.RemoveAt(idx);
            _rows.Insert(newIdx, item);
            _grid.ClearSelection();
            _grid.CurrentCell = _grid.Rows[newIdx].Cells[0];
        }
    }
}
