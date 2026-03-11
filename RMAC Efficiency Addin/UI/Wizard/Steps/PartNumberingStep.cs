using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;
using RMAC_Efficiency_Addin.Settings;
using RMAC_Efficiency_Addin.UI.Dialogs;

namespace RMAC_Efficiency_Addin.UI.Wizard.Steps
{
    internal sealed class PartNumberingStep : WizardStepBase
    {
        public override string StepTitle => "Part Numbering";
        public override string StepDescription => "Choose a numbering scheme for parts in assemblies";

        private Panel? _panel;
        private KryptonComboBox? _cmbMode;
        private KryptonTextBox? _txtPrefix;
        private KryptonTextBox? _txtSuffix;

        public override Control BuildPanel()
        {
            if (_panel != null) return _panel;

            _panel = new Panel { Dock = DockStyle.Fill };

            // 3-column layout: label | input | edit button
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 5,
                Padding = new Padding(0, 10, 0, 0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));    // labels
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // inputs
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));    // edit buttons
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // info
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // mode
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // prefix
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // suffix
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // explanation

            var info = MakeLabel(
                "Choose how parts are numbered when you run the Renumber command from the drawing dock.");
            info.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(info, 0, 0);
            layout.SetColumnSpan(info, 3);

            // Mode row (combo spans input + button columns)
            layout.Controls.Add(MakeLabel("Numbering mode:"), 0, 1);
            _cmbMode = new KryptonComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            _cmbMode.Items.AddRange(new object[] { "Structured", "Sequential", "Frames Only" });
            layout.Controls.Add(_cmbMode, 1, 1);
            layout.SetColumnSpan(_cmbMode, 2);

            // Prefix row
            layout.Controls.Add(MakeLabel("Prefix template:"), 0, 2);
            _txtPrefix = new KryptonTextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(_txtPrefix, 1, 2);
            var btnEditPrefix = new KryptonButton { Text = "Edit", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
            btnEditPrefix.Click += (_, _) =>
            {
                using var dlg = new TemplateTokenEditorDialog("Edit Prefix Template", _txtPrefix.Text);
                if (dlg.ShowDialog() == DialogResult.OK)
                    _txtPrefix.Text = dlg.Template;
            };
            layout.Controls.Add(btnEditPrefix, 2, 2);

            // Suffix row
            layout.Controls.Add(MakeLabel("Suffix template:"), 0, 3);
            _txtSuffix = new KryptonTextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(_txtSuffix, 1, 3);
            var btnEditSuffix = new KryptonButton { Text = "Edit", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
            btnEditSuffix.Click += (_, _) =>
            {
                using var dlg = new TemplateTokenEditorDialog("Edit Suffix Template", _txtSuffix.Text);
                if (dlg.ShowDialog() == DialogResult.OK)
                    _txtSuffix.Text = dlg.Template;
            };
            layout.Controls.Add(btnEditSuffix, 2, 3);

            // Explanation textbox
            var explanation = new KryptonTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                TabStop = false,
                Text =
                    "Structured:  numbers reflect the assembly hierarchy\r\n" +
                    "  Top assembly: A-01\r\n" +
                    "  Assemblies:   A-02, A-03, ...\r\n" +
                    "  Parts:        P-01-01, P-01-02, P-02-01, ...\r\n" +
                    "  Frames:       P-01-03, P-01-04 (continue part count)\r\n\r\n" +
                    "Sequential:  flat sequential numbering across all parts\r\n" +
                    "  Top assembly: A-01\r\n" +
                    "  Assemblies:   A-02, A-03, ...\r\n" +
                    "  Parts:        P-001, P-002, P-003, ...\r\n\r\n" +
                    "Frames Only:  only renumber frame/structural members\r\n" +
                    "  Frames:       A-01-01, A-01-02, ... (parent prefix + counter)\r\n" +
                    "  Other parts and assemblies are left untouched\r\n\r\n" +
                    "Prefix/suffix templates wrap around the auto-generated number.\r\n" +
                    "Click Edit to insert tokens. Leave blank for no prefix/suffix."
            };
            layout.Controls.Add(explanation, 0, 4);
            layout.SetColumnSpan(explanation, 3);

            _panel.Controls.Add(layout);
            return _panel;
        }

        public override void LoadFromSettings()
        {
            if (_cmbMode == null) return;

            _cmbMode.SelectedIndex = AddinSettings.Current.PartNumberingMode switch
            {
                PartNumberingMode.Sequential => 1,
                PartNumberingMode.FramesOnly => 2,
                _ => 0 // Structured
            };
            _txtPrefix!.Text = AddinSettings.Current.PartNumberPrefixTemplate;
            _txtSuffix!.Text = AddinSettings.Current.PartNumberSuffixTemplate;
        }

        public override void ApplyToSettings()
        {
            if (_cmbMode == null) return;

            AddinSettings.Current.PartNumberingMode = _cmbMode.SelectedIndex switch
            {
                1 => PartNumberingMode.Sequential,
                2 => PartNumberingMode.FramesOnly,
                _ => PartNumberingMode.Structured
            };
            AddinSettings.Current.PartNumberPrefixTemplate = _txtPrefix!.Text.Trim();
            AddinSettings.Current.PartNumberSuffixTemplate = _txtSuffix!.Text.Trim();
        }
    }
}
