// Settings/Tabs/SketchColoursSettingsTab.cs
using RMAC_Efficiency_Addin;
using RMAC_Efficiency_Addin.Settings;
using RMAC_Efficiency_Addin.UI.Settings;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Navigator;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.UI.Settings
{
    /// <summary>
    /// Sketch colour overrides (inactive sketch palette).
    /// </summary>
    internal sealed class SketchColoursSettingsTab : SettingsTabBase
    {
        public SketchColoursSettingsTab(ISettingsTabHost host) : base(host) { }

        public override string TabTitle => "Sketch Colours";

        private KryptonPage? _tab;

        // Headline mode selector (includes "off")
        private readonly KryptonComboBox _cmbColorMode = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220
        };

        // Color swatches stay as standard Panels (they display colors, not themed chrome)
        private readonly Panel _swProjected = NewSwatch();
        private readonly Panel _swUnderCurve = NewSwatch();
        private readonly Panel _swFullCurve = NewSwatch();
        private readonly Panel _swOverCurve = NewSwatch();
        private readonly Panel _swUnderConstruction = NewSwatch();
        private readonly Panel _swFullConstruction = NewSwatch();
        private readonly Panel _swOverConstruction = NewSwatch();

        private readonly KryptonButton _btnResetPalette = new() { Text = "Reset defaults" };

        // Track palette-specific controls so we can enable/disable them
        private readonly List<Control> _paletteControls = new();

        // ---- Visual tuning ----
        private const int RowHeight = 36;
        private const int HeaderRowHeight = 28;
        private const int ButtonColumnWidth = 140;

        // Combo indices
        private const int IdxDisabled = 0;
        private const int IdxPalette = 1;
        private const int IdxRandom = 2;

        public override KryptonPage BuildTab()
        {
            if (_tab != null) return _tab;

            _tab = new KryptonPage { Text = TabTitle, Padding = new Padding(10) };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));        // label
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));        // swatch
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ButtonColumnWidth)); // button

            int row = 0;

            // Headline: sketch coloring mode
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, RowHeight));
            var modeLabel = new KryptonLabel
            {
                Text = "Sketch coloring mode:",
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            modeLabel.StateCommon.ShortText.Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);
            table.Controls.Add(modeLabel, 0, row);
            _cmbColorMode.Dock = DockStyle.Fill;
            table.Controls.Add(_cmbColorMode, 1, row);
            table.SetColumnSpan(_cmbColorMode, 2);
            row++;

            // Spacer
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
            table.Controls.Add(new Label { AutoSize = true }, 0, row++);

            AddHeaderRow(table, row++, "Inactive Sketch Colour Palette");

            // 1) Projected
            AddColourRow(table, row++, "Projected", _swProjected);

            // 2) Standard Geometry header + Under/Full/Over
            AddSectionRow(table, row++, "Standard Geometry");
            AddColourRow(table, row++, "Under", _swUnderCurve);
            AddColourRow(table, row++, "Full", _swFullCurve);
            AddColourRow(table, row++, "Over", _swOverCurve);

            // 3) Construction header + Under/Full/Over
            AddSectionRow(table, row++, "Construction");
            AddColourRow(table, row++, "Under", _swUnderConstruction);
            AddColourRow(table, row++, "Full", _swFullConstruction);
            AddColourRow(table, row++, "Over", _swOverConstruction);

            // Bottom row: Reset + note
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var bottom = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 16, 0, 0)
            };

            _btnResetPalette.AutoSize = false;
            _btnResetPalette.Size = new Size(ButtonColumnWidth, RowHeight);
            _btnResetPalette.Margin = new Padding(0);
            bottom.Controls.Add(_btnResetPalette);
            _paletteControls.Add(_btnResetPalette);

            var note = new KryptonLabel
            {
                AutoSize = true,
                Text = "Applies when sketches are inactive.",
                Margin = new Padding(10, 7, 0, 0)
            };
            bottom.Controls.Add(note);
            _paletteControls.Add(note);

            table.Controls.Add(bottom, 0, row);
            table.SetColumnSpan(bottom, 3);

            // Populate combo (order must match IdxDisabled/IdxPalette/IdxRandom)
            _cmbColorMode.Items.Clear();
            _cmbColorMode.Items.Add("No formatting");
            _cmbColorMode.Items.Add("Muted palette");
            _cmbColorMode.Items.Add("Random colour per sketch");

            _tab.Controls.Add(table);
            return _tab;
        }

        public override void WireEvents()
        {
            _btnResetPalette.Click += (_, __) =>
            {
                if (Host.IsLoading) return;
                SetPaletteUi(InactiveSketchPaletteSettings.CreateDefaults());
                Host.MarkChanged();
            };

            _cmbColorMode.SelectedIndexChanged += (_, __) =>
            {
                if (Host.IsLoading) return;
                UpdateControlStates();
                Host.MarkChanged();
            };
        }

        public override void LoadFromSettings()
        {
            _cmbColorMode.SelectedIndex = AddinSettings.Current.InactiveSketchColorMode switch
            {
                InactiveSketchColorMode.Palette => IdxPalette,
                InactiveSketchColorMode.RandomPerSketch => IdxRandom,
                _ => IdxDisabled
            };

            var pal = AddinSettings.Current.InactiveSketchPalette ?? InactiveSketchPaletteSettings.CreateDefaults();
            pal.SanitizeInPlace();
            SetPaletteUi(pal);

            UpdateControlStates();
        }

        public override void ApplyToSettings()
        {
            AddinSettings.Current.InactiveSketchColorMode = _cmbColorMode.SelectedIndex switch
            {
                IdxPalette => InactiveSketchColorMode.Palette,
                IdxRandom => InactiveSketchColorMode.RandomPerSketch,
                _ => InactiveSketchColorMode.Disabled
            };

            AddinSettings.Current.InactiveSketchPalette = ReadPaletteUi();
            AddinSettings.Current.InactiveSketchPalette.SanitizeInPlace();
        }

        public override void Validate(List<string> warnings, List<string> errors)
        {
            // No validation required.
        }

        private void UpdateControlStates()
        {
            bool isPaletteMode = _cmbColorMode.SelectedIndex == IdxPalette;

            foreach (var c in _paletteControls)
                c.Enabled = isPaletteMode;
        }

        private static Panel NewSwatch()
        {
            return new Panel
            {
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(4),
                BackColor = Color.LightGray
            };
        }

        private void AddHeaderRow(TableLayoutPanel table, int row, string header)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderRowHeight));
            var lbl = new KryptonLabel
            {
                Text = header,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(0, 4, 0, 0)
            };
            lbl.StateCommon.ShortText.Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);
            table.Controls.Add(lbl, 0, row);
            table.SetColumnSpan(lbl, 3);
            _paletteControls.Add(lbl);
        }

        private void AddSectionRow(TableLayoutPanel table, int row, string section)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderRowHeight));

            var lbl = new KryptonLabel
            {
                Text = section,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(0, 6, 0, 0),
                Margin = new Padding(0, 4, 0, 0)
            };
            lbl.StateCommon.ShortText.Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);

            table.Controls.Add(lbl, 0, row);
            table.SetColumnSpan(lbl, 3);
            _paletteControls.Add(lbl);
        }

        private void AddColourRow(TableLayoutPanel table, int row, string label, Panel swatch)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, RowHeight));

            var lbl = new KryptonLabel
            {
                Text = label,
                AutoSize = true,
                Dock = DockStyle.Fill
            };

            swatch.Dock = DockStyle.Fill;

            var btn = new KryptonButton
            {
                Text = "Change\u2026",
                Dock = DockStyle.Fill,
                Tag = swatch
            };

            btn.AutoSize = false;
            btn.MinimumSize = new Size(0, RowHeight);
            btn.Padding = new Padding(10, 0, 10, 0);

            btn.Click += (_, __) =>
            {
                if (Host.IsLoading) return;
                ChangeSwatchColor((Panel)btn.Tag);
            };

            table.Controls.Add(lbl, 0, row);
            table.Controls.Add(swatch, 1, row);
            table.Controls.Add(btn, 2, row);

            _paletteControls.Add(lbl);
            _paletteControls.Add(swatch);
            _paletteControls.Add(btn);
        }

        private void ChangeSwatchColor(Panel swatch)
        {
            using var dlg = new ColorDialog
            {
                FullOpen = true,
                Color = swatch.BackColor
            };

            if (dlg.ShowDialog(Host.Owner) != DialogResult.OK)
                return;

            swatch.BackColor = dlg.Color;
            Host.MarkChanged();
        }

        private void SetPaletteUi(InactiveSketchPaletteSettings pal)
        {
            _swProjected.BackColor = ToDrawingColor(pal.Projected);
            _swUnderCurve.BackColor = ToDrawingColor(pal.UnderCurve);
            _swFullCurve.BackColor = ToDrawingColor(pal.FullCurve);
            _swOverCurve.BackColor = ToDrawingColor(pal.OverCurve);
            _swUnderConstruction.BackColor = ToDrawingColor(pal.UnderConstruction);
            _swFullConstruction.BackColor = ToDrawingColor(pal.FullConstruction);
            _swOverConstruction.BackColor = ToDrawingColor(pal.OverConstruction);
        }

        private InactiveSketchPaletteSettings ReadPaletteUi()
        {
            return new InactiveSketchPaletteSettings
            {
                Projected = ToRgb24(_swProjected.BackColor),
                UnderCurve = ToRgb24(_swUnderCurve.BackColor),
                FullCurve = ToRgb24(_swFullCurve.BackColor),
                OverCurve = ToRgb24(_swOverCurve.BackColor),
                UnderConstruction = ToRgb24(_swUnderConstruction.BackColor),
                FullConstruction = ToRgb24(_swFullConstruction.BackColor),
                OverConstruction = ToRgb24(_swOverConstruction.BackColor),
            };
        }

        private static Rgb24 ToRgb24(Color c) => new(c.R, c.G, c.B);
        private static Color ToDrawingColor(Rgb24 c) => Color.FromArgb(c.R, c.G, c.B);
    }
}
