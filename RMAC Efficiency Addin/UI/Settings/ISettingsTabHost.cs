using Inventor;
using System.Windows.Forms;

namespace RMAC_Efficiency_Addin.UI.Settings
{
    internal interface ISettingsTabHost
    {
        bool IsLoading { get; }
        void MarkChanged();

        IWin32Window Owner { get; }

        /// <summary>
        /// Optional Inventor application reference, for tabs that need to interrogate the active document.
        /// </summary>
        Inventor.Application? InventorApp { get; }
    }
}
