using System.Drawing;
using System.Windows.Forms;
using RMAC_Efficiency_Addin.Settings;

namespace RMAC_Efficiency_Addin.UI.Wizard.Steps
{
    internal sealed class FinishStep : WizardStepBase
    {
        public override string StepTitle => "Finish";
        public override string StepDescription => "Setup is complete";

        private Panel? _panel;
        private Label? _lblSummary;

        public override Control BuildPanel()
        {
            if (_panel != null) return _panel;

            _panel = new Panel { Dock = DockStyle.Fill };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0, 10, 0, 0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _lblSummary = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ForeColor = LabelTextColor
            };

            layout.Controls.Add(_lblSummary, 0, 0);

            _panel.Controls.Add(layout);
            return _panel;
        }

        public override void LoadFromSettings()
        {
            if (_lblSummary == null) return;

            var path = AddinSettings.LoadedPath;
            if (string.IsNullOrWhiteSpace(path))
                path = AddinSettings.UserSettingsPath;

            _lblSummary.Text =
                "Your RMAC Efficiency Addin is now configured.\r\n\r\n" +
                $"Settings will be saved to:\r\n{path}\r\n\r\n" +
                "You can change any setting later from the RMAC Settings\r\n" +
                "button in the Inventor ribbon, or re-run this wizard\r\n" +
                "from the General settings tab.\r\n\r\n" +
                "Click Finish to save your settings.";
        }

        public override void ApplyToSettings() { }
    }
}
