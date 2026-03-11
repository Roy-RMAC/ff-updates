using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.UI.Wizard.Steps
{
    internal sealed class WelcomeStep : WizardStepBase
    {
        public override string StepTitle => "Welcome";
        public override string StepDescription => "Welcome to the RMAC Efficiency Addin";

        private Panel? _panel;

        public override Control BuildPanel()
        {
            if (_panel != null) return _panel;

            _panel = new Panel { Dock = DockStyle.Fill };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0, 10, 0, 0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // intro text
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // heading
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // steps list

            var intro = MakeLabel(
                "This wizard will guide you through the initial configuration " +
                "of the RMAC Efficiency Addin.\r\n\r\n" +
                "You can change any of these settings later from the " +
                "RMAC Settings button in the Inventor ribbon.");
            intro.Margin = new Padding(0, 0, 0, 16);
            layout.Controls.Add(intro, 0, 0);

            var stepsHeading = MakeHeading("Steps:");
            stepsHeading.Margin = new Padding(0, 0, 0, 8);
            layout.Controls.Add(stepsHeading, 0, 1);

            var steps = new KryptonTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                TabStop = false,
                Text =
                    "  1.  Export Categories\r\n" +
                    "       Define your manufacturing process categories and\r\n" +
                    "       which export actions apply to each.\r\n\r\n" +
                    "  2.  Comment Lists\r\n" +
                    "       Set the allowed Part and Assembly comment values\r\n" +
                    "       used by Check Comments and the exporter.\r\n\r\n" +
                    "  3.  Part Numbering\r\n" +
                    "       Choose a numbering scheme for parts in assemblies.\r\n\r\n" +
                    "  4.  Settings Location\r\n" +
                    "       Choose where to store your settings file."
            };
            layout.Controls.Add(steps, 0, 2);

            _panel.Controls.Add(layout);
            return _panel;
        }

        public override void LoadFromSettings() { }
        public override void ApplyToSettings() { }
    }
}
