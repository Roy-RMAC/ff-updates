using Inventor;

namespace RMAC_Efficiency_Addin.UI
{
    // StandardAddInServer implements this so UI wiring stays out of the server.
    internal interface IRmacCommandCallbacks
    {
        void BtnSettings_OnExecute(NameValueMap Context);
        void BtnCheckComments_OnExecute(NameValueMap Context);
        void BtnEditComments_OnExecute(NameValueMap Context);
        void BtnFindSketchCmds_OnExecute(NameValueMap Context);
        void BtnLegacySweep_OnExecute(NameValueMap Context);
        void BtnSmThicknessFromLine_OnExecute(NameValueMap Context);
        void BtnHideAllSketches_OnExecute(NameValueMap Context);
        void BtnShowAllSketches_OnExecute(NameValueMap Context);
        void BtnShowSketchDims_OnExecute(NameValueMap Context);
        void BtnHideSketchDims_OnExecute(NameValueMap Context);
        void BtnClearOneSketchEntOverride_OnExecute(NameValueMap Context);
        void BtnFindShowFormatCmds_OnExecute(NameValueMap Context);
        void BtnClearSketchColorOverrides_OnExecute(NameValueMap Context);
        void BtnReleaseSketchColorHold_OnExecute(NameValueMap Context);
        void CmbDimMode_OnSelect(NameValueMap Context);
        void BtnHighlight_OnExecute(NameValueMap Context);
        void BtnClearHighlight_OnExecute(NameValueMap Context);

    }
}
