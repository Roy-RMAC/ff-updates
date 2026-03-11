using System.Drawing;
using System.Windows.Forms;

namespace RMAC_Efficiency_Addin.UI.Wizard
{
    internal abstract class WizardStepBase : IWizardStepPanel
    {
        protected static readonly Color LabelTextColor = Color.FromArgb(220, 220, 220);

        public abstract string StepTitle { get; }
        public abstract string StepDescription { get; }
        public abstract Control BuildPanel();
        public abstract void LoadFromSettings();
        public abstract void ApplyToSettings();
        public virtual bool Validate(out string? error) { error = null; return true; }

        protected static Label MakeLabel(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = LabelTextColor,
            Margin = new Padding(0, 0, 8, 0)
        };

        protected static Label MakeHeading(string text)
        {
            var baseFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font(baseFont.FontFamily, 11f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 4)
            };
        }
    }
}
