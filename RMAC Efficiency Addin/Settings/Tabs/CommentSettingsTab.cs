// UI/Settings/CommentSettingsTab.cs
using RMAC_Efficiency_Addin.Settings;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using Krypton.Navigator;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.UI.Settings
{
    /// <summary>
    /// Comments configuration editor.
    /// Unified: reads/writes AddinSettings.Current.CommentsOptions (stored inside settings.json).
    /// </summary>
    internal sealed class CommentSettingsTab : SettingsTabBase
    {
        public CommentSettingsTab(ISettingsTabHost host) : base(host) { }
        public override string TabTitle => "Comments";

        private KryptonPage? _tab;

        private sealed class ListRow { public string Value { get; set; } = ""; }

        private readonly BindingList<ListRow> _partRows = new();
        private readonly BindingList<ListRow> _assemblyRows = new();
        private readonly BindingList<ListRow> _inseparableRows = new();

        private readonly KryptonDataGridView _gridPart = NewListGrid();
        private readonly KryptonDataGridView _gridAssembly = NewListGrid();
        private readonly KryptonDataGridView _gridTreatInseparable = NewListGrid();

        private readonly KryptonButton _btnAddPart = new() { Text = "Add" };
        private readonly KryptonButton _btnRemovePart = new() { Text = "Remove" };

        private readonly KryptonButton _btnAddAssembly = new() { Text = "Add" };
        private readonly KryptonButton _btnRemoveAssembly = new() { Text = "Remove" };

        private readonly KryptonButton _btnAddTreat = new() { Text = "Add" };
        private readonly KryptonButton _btnRemoveTreat = new() { Text = "Remove" };

        private readonly KryptonLabel _lblInfo = new()
        {
            AutoSize = true,
            Text =
                "Edits the shared allowed-lists used by Check Comments / exporter classification.\r\n" +
                "\"No Drawing Required\" lists comment values that skip the Check Views Coverage check.\r\n" +
                "Changes are saved into settings.json when you click Save in RMAC Settings."
        };

        public override KryptonPage BuildTab()
        {
            if (_tab != null) return _tab;

            _tab = new KryptonPage { Text = TabTitle, Padding = new Padding(10) };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(_lblInfo, 0, 0);

            var cols = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 10, 0, 0),
                Padding = new Padding(0),
                AutoSize = false
            };

            cols.ColumnStyles.Clear();
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            cols.RowStyles.Clear();
            // CRITICAL: force the only row to take all available height
            cols.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            cols.Controls.Add(BuildListPanel("Part categories", _gridPart, _btnAddPart, _btnRemovePart), 0, 0);
            cols.Controls.Add(BuildListPanel("Assembly categories", _gridAssembly, _btnAddAssembly, _btnRemoveAssembly), 1, 0);

            cols.Controls.Add(BuildListPanel("No Drawing Required", _gridTreatInseparable, _btnAddTreat, _btnRemoveTreat), 2, 0);

            root.Controls.Add(cols, 0, 1);
            _tab.Controls.Add(root);

            BindGrid(_gridPart, _partRows);
            BindGrid(_gridAssembly, _assemblyRows);
            BindGrid(_gridTreatInseparable, _inseparableRows);

            return _tab;
        }

        public override void WireEvents()
        {
            _btnAddPart.Click += (_, __) => AddRow(_gridPart, _partRows);
            _btnRemovePart.Click += (_, __) => RemoveSelected(_gridPart, _partRows);

            _btnAddAssembly.Click += (_, __) => AddRow(_gridAssembly, _assemblyRows);
            _btnRemoveAssembly.Click += (_, __) => RemoveSelected(_gridAssembly, _assemblyRows);

            _btnAddTreat.Click += (_, __) => AddRow(_gridTreatInseparable, _inseparableRows);
            _btnRemoveTreat.Click += (_, __) => RemoveSelected(_gridTreatInseparable, _inseparableRows);

            HookGridChanged(_gridPart);
            HookGridChanged(_gridAssembly);
            HookGridChanged(_gridTreatInseparable);
        }

        public override void LoadFromSettings()
        {
            var opts = AddinSettings.Current.CommentsOptions;
            opts.SanitizeInPlace();

            SetRows(_partRows, opts.Part);
            SetRows(_assemblyRows, opts.Assembly);
            SetRows(_inseparableRows, opts.SkipCommentsForViewCoverage);

            _lblInfo.Text =
                "Edits the shared allowed-lists used by Check Comments / exporter classification.\r\n" +
                "\"No Drawing Required\" lists comment values that skip the Check Views Coverage check.\r\n" +
                "Changes are saved into settings.json when you click Save in RMAC Settings.";
        }

        public override void ApplyToSettings()
        {
            var opts = AddinSettings.Current.CommentsOptions;
            opts.Part = ReadRows(_partRows);
            opts.Assembly = ReadRows(_assemblyRows);
            opts.SkipCommentsForViewCoverage = ReadRows(_inseparableRows);
            opts.SanitizeInPlace();
        }

        public override void Validate(List<string> warnings, List<string> errors)
        {
            if (ReadRows(_partRows).Count == 0) errors.Add("Comments: Part categories list is empty.");
            if (ReadRows(_assemblyRows).Count == 0) errors.Add("Comments: Assembly categories list is empty.");
        }

        // ---- UI helpers ----

        private static Control BuildListPanel(string title, KryptonDataGridView grid, KryptonButton btnAdd, KryptonButton btnRemove)
        {
            var box = new KryptonGroupBox
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            box.Values.Heading = title;

            btnAdd.AutoSize = true;
            btnRemove.AutoSize = true;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
                AutoSize = false
            };

            layout.RowStyles.Clear();
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // grid
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));  // button row (will be corrected)

            layout.Controls.Add(grid, 0, 0);

            var buttonHost = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 8),
                Padding = new Padding(0)
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            buttons.Controls.Add(btnAdd);
            buttons.Controls.Add(btnRemove);

            buttonHost.Controls.Add(buttons);
            layout.Controls.Add(buttonHost, 0, 1);

            int _lastRowH = -1;

            void SyncButtonRowHeight()
            {
                // Use the container's PreferredSize (more reliable than individual button PreferredSize)
                int contentH = buttons.PreferredSize.Height;

                int marginV = buttonHost.Margin.Top + buttonHost.Margin.Bottom;
                int paddingV = buttonHost.Padding.Top + buttonHost.Padding.Bottom;

                // Slightly larger buffer to account for TLPanel/GroupBox internal borders + DPI rounding
                int buffer = 22;

                int rowH = contentH + marginV + paddingV + buffer;
                rowH = Math.Max(rowH, 44);

                if (rowH == _lastRowH) return; // prevent layout thrash
                _lastRowH = rowH;

                layout.RowStyles[1] = new RowStyle(SizeType.Absolute, rowH);
                buttonHost.MinimumSize = new Size(0, rowH);
            }

            // Critical: run AFTER WinForms finishes layout so PreferredSize is correct
            void SyncLater()
            {
                if (!box.IsHandleCreated) return;
                box.BeginInvoke((Action)(() => SyncButtonRowHeight()));
            }

            box.HandleCreated += (_, __) => SyncLater();
            box.FontChanged += (_, __) => SyncLater();
            box.Layout += (_, __) => SyncLater();

            // initial kick
            SyncLater();

            // KryptonGroupBox hosts children inside its inner Panel
            box.Panel.Controls.Add(layout);
            return box;
        }

        private static KryptonDataGridView NewListGrid()
        {
            var grid = new KryptonDataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
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

        private static void BindGrid(KryptonDataGridView grid, BindingList<ListRow> rows) => grid.DataSource = rows;

        private void HookGridChanged(KryptonDataGridView grid)
        {
            grid.CellValueChanged += (_, __) => OnChanged();
            grid.UserAddedRow += (_, __) => OnChanged();
            grid.UserDeletedRow += (_, __) => OnChanged();
            grid.CurrentCellDirtyStateChanged += (_, __) =>
            {
                if (grid.IsCurrentCellDirty)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
        }

        private static void AddRow(KryptonDataGridView grid, BindingList<ListRow> rows)
        {
            rows.Add(new ListRow());
            if (rows.Count > 0)
            {
                grid.ClearSelection();
                grid.CurrentCell = grid.Rows[rows.Count - 1].Cells[0];
                grid.BeginEdit(true);
            }
        }

        private static void RemoveSelected(KryptonDataGridView grid, BindingList<ListRow> rows)
        {
            if (grid.CurrentRow == null) return;
            int idx = grid.CurrentRow.Index;
            if (idx < 0 || idx >= rows.Count) return;
            rows.RemoveAt(idx);
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
            return (rows ?? new BindingList<ListRow>())
                .Select(r => (r?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
