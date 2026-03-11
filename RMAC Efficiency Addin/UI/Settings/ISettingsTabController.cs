using System.Collections.Generic;
using Krypton.Navigator;

namespace RMAC_Efficiency_Addin.UI.Settings
{
    internal interface ISettingsTabController
    {
        string TabTitle { get; }

        KryptonPage BuildTab();
        void WireEvents();

        void LoadFromSettings();
        void ApplyToSettings();

        void Validate(List<string> warnings, List<string> errors);
    }
}
