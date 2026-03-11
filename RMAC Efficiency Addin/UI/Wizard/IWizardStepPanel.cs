using System.Windows.Forms;

namespace RMAC_Efficiency_Addin.UI.Wizard
{
    internal interface IWizardStepPanel
    {
        string StepTitle { get; }
        string StepDescription { get; }
        Control BuildPanel();
        void LoadFromSettings();
        void ApplyToSettings();
        bool Validate(out string? error);
    }
}
