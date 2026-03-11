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
    internal sealed class CommentListsStep : WizardStepBase
    {
        public override string StepTitle => "Comment Lists";
        public override string StepDescription => "Define the allowed Part and Assembly comment values";

        private Panel? _panel;

        private sealed class ListRow { public string Value { get; set; } = ""; }

        private readonly BindingList<ListRow> _partRows = new();
        private readonly BindingList<ListRow> _assemblyRows = new();

        private readonly KryptonDataGridView _gridPart = NewListGrid();
        private readonly KryptonDataGridView _gridAssembly = NewListGrid();

        public override Control BuildPanel()
        {
            if (_panel != null) return _panel;

            _panel = new Panel { Dock = DockStyle.Fill };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(0, 4, 0, 0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // info
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // column headers
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // grids + buttons

            var info = MakeLabel(
                "Define valid comment values for parts and assemblies. " +
                "These are used by Check Comments and the exporter to classify components.");
            info.Margin = new Padding(0, 0, 0, 8);
            layout.Controls.Add(info, 0, 0);
            layout.SetColumnSpan(info, 2);

            // Column headers
            var lblPart = MakeHeading("Part Comments");
            lblPart.Margin = new Padding(0, 0, 4, 4);
            layout.Controls.Add(lblPart, 0, 1);

            var lblAsm = MakeHeading("Assembly Comments");
            lblAsm.Margin = new Padding(4, 0, 0, 4);
            layout.Controls.Add(lblAsm, 1, 1);

            // Grid panels (grid + buttons, no group box)
            layout.Controls.Add(BuildListPanel(_gridPart, _partRows, new Padding(0, 0, 4, 0)), 0, 2);
            layout.Controls.Add(BuildListPanel(_gridAssembly, _assemblyRows, new Padding(4, 0, 0, 0)), 1, 2);

            _panel.Controls.Add(layout);
            return _panel;
        }

        public override void LoadFromSettings()
        {
            var opts = AddinSettings.Current.CommentsOptions;
            opts.SanitizeInPlace();
            SetRows(_partRows, opts.Part);
            SetRows(_assemblyRows, opts.Assembly);
        }

        public override void ApplyToSettings()
        {
            var opts = AddinSettings.Current.CommentsOptions;
            opts.Part = ReadRows(_partRows);
            opts.Assembly = ReadRows(_assemblyRows);
            opts.SanitizeInPlace();
        }

        public override bool Validate(out string? error)
        {
            if (ReadRows(_partRows).Count == 0)
            {
                error = "Part comments list must have at least one entry.";
                return false;
            }
            if (ReadRows(_assemblyRows).Count == 0)
            {
                error = "Assembly comments list must have at least one entry.";
                return false;
            }
            error = null;
            return true;
        }

        private static Control BuildListPanel(KryptonDataGridView grid, BindingList<ListRow> rows, Padding margin)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = margin
            };

            var btnAdd = new KryptonButton { Text = "Add", AutoSize = true };
            var btnRemove = new KryptonButton { Text = "Remove", AutoSize = true };

            btnAdd.Click += (_, _) =>
            {
                rows.Add(new ListRow());
                if (rows.Count > 0)
                {
                    grid.ClearSelection();
                    grid.CurrentCell = grid.Rows[rows.Count - 1].Cells[0];
                    grid.BeginEdit(true);
                }
            };

            btnRemove.Click += (_, _) =>
            {
                if (grid.CurrentRow == null) return;
                int idx = grid.CurrentRow.Index;
                if (idx >= 0 && idx < rows.Count) rows.RemoveAt(idx);
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 4, 0, 0)
            };
            buttons.Controls.Add(btnAdd);
            buttons.Controls.Add(btnRemove);

            grid.Dock = DockStyle.Fill;
            grid.DataSource = rows;

            panel.Controls.Add(grid);
            panel.Controls.Add(buttons);
            return panel;
        }

        private static KryptonDataGridView NewListGrid()
        {
            var grid = new KryptonDataGridView
            {
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Value",
                DataPropertyName = nameof(ListRow.Value),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
            return grid;
        }

        private static void SetRows(BindingList<ListRow> target, IEnumerable<string>? values)
        {
            target.RaiseListChangedEvents = false;
            target.Clear();
            foreach (var v in values ?? Array.Empty<string>())
                target.Add(new ListRow { Value = v });
            target.RaiseListChangedEvents = true;
            target.ResetBindings();
        }

        private static List<string> ReadRows(BindingList<ListRow> rows)
        {
            return rows
                .Select(r => (r?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
