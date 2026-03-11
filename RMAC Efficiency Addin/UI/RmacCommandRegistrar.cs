using Inventor;
using System;

namespace RMAC_Efficiency_Addin.UI
{
    internal sealed class RmacCommandRegistrar
    {
        private readonly Inventor.Application _app;
        private readonly string _clientId;
        private readonly IRmacCommandCallbacks _cb;

        // Keep delegate instances so we can unhook on Dispose
        private ButtonDefinitionSink_OnExecuteEventHandler? _hSettings;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hCheckComments;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hEditComments;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hFindSketchCmds;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hLegacySweep;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hSmThicknessFromLine;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hHideAllSketches;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hShowAllSketches;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hShowSketchDims;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hHideSketchDims;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hClearOneSketchEntOverride;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hFindShowFormatCmds;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hClearSketchColorOverrides;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hReleaseSketchColorHold;
        private ComboBoxDefinitionSink_OnSelectEventHandler? _hDimModeSelect;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hHighlight;
        private ButtonDefinitionSink_OnExecuteEventHandler? _hClearHighlight;

        public RmacCommandRegistrar(Inventor.Application app, string clientId, IRmacCommandCallbacks callbacks)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
            _cb = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        }

        public RmacCommands Register()
        {
            var defs = _app.CommandManager.ControlDefinitions;

            var btnCheckComments = defs.AddButtonDefinition(
                "Check Comments",
                "RMAC_CheckCommentsBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Checks BOM items and forces valid Comments values.",
                "Check Comments",
                null,
                null
            );

            var btnSettings = defs.AddButtonDefinition(
                "RMAC Settings",
                "RMAC_SettingsBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Configure RMAC Efficiency Addin settings.",
                "Settings",
                null,
                null
            );

            var btnEditComments = defs.AddButtonDefinition(
                "Edit Comment Lists",
                "RMAC_EditCommentListsBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Edit allowable Comments values used by the RMAC checker.",
                "Edit Comment Lists",
                null,
                null
            );

            var btnFindSketchCmds = defs.AddButtonDefinition(
                "Find Sick Sketch Constraints",
                "RMAC_FindSketchCmdsBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Placeholder: list sick constraints / future solver.",
                "Find Sick Sketch Constraints",
                null,
                null
            );

            // Legacy: sweep all sketches and re-apply RMAC colour overrides.
            // This is intentionally kept on the existing internal name so the UI survives updates.
            var btnLegacySweep = defs.AddButtonDefinition(
                "Format All Sketches",
                "RMAC_LegacySweepBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Apply RMAC sketch colour overrides to all sketches in the active part.",
                "Format All Sketches",
                null,
                null
            );

            var btnSmThicknessFromLine = defs.AddButtonDefinition(
                "Thickness From Line",
                "RMAC_SmThicknessFromLineBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Set sheet metal thickness from a selected line.",
                "Thickness From Line",
                null,
                null
            );

            // Renamed (display only)
            var btnHideAllSketches = defs.AddButtonDefinition(
                "Hide Sketches",
                "RMAC_HideAllSketchesBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Hide all sketches in the active part.",
                "Hide Sketches",
                null,
                null
            );

            var btnShowAllSketches = defs.AddButtonDefinition(
                "Show Sketches",
                "RMAC_ShowAllSketchesBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Show all sketches in the active part.",
                "Show Sketches",
                null,
                null
            );

            // ComboBox: sketch dimension visibility policy
            var cmbDimMode = defs.AddComboBoxDefinition(
                "Dimension Mode",
                "RMAC_DimModeCombo_v2",   // bumped to force UI refresh
                CommandTypesEnum.kQueryOnlyCmdType,
                90,                       // smaller width
                _clientId,
                "Controls sketch dimension visibility mode (Named Only / Hide All / Show All).",
                "Dimension Mode",
                null,
                null,
                ButtonDisplayEnum.kDisplayTextInLearningMode
            );

            // Populate items (selection is applied by StandardAddInServer to avoid triggering OnSelect during init)
            try
            {
                cmbDimMode.Clear();
                cmbDimMode.AddItem("Named Only", 1);
                cmbDimMode.AddItem("Hide All", 2);
                cmbDimMode.AddItem("Show All", 3);
            }
            catch { }

            var btnShowSketchDims = defs.AddButtonDefinition(
                "Show Dims",
                "RMAC_ShowSketchDimsBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Show all dimensions in the selected sketch.",
                "Show Dims",
                null,
                null
            );

            var btnHideSketchDims = defs.AddButtonDefinition(
                "Hide Dims",
                "RMAC_HideSketchDimsBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Revert sketch dimension visibility to the global policy.",
                "Hide Dims",
                null,
                null
            );

            var btnClearOneSketchEntOverride = defs.AddButtonDefinition(
                "Clear One Sketch Override",
                "RMAC_ClearOneSketchEntOverrideBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Clear entity colour overrides for one sketch.",
                "Clear One Sketch Override",
                null,
                null
            );

            var btnFindShowFormatCmds = defs.AddButtonDefinition(
                "Find 'Show Format' Command",
                "RMAC_FindShowFormatCmdsBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Debug: find the Show Format command definition.",
                "Find Show Format",
                null,
                null
            );

            var btnClearSketchColorOverrides = defs.AddButtonDefinition(
                "Enable Show Format",
                "RMAC_ClearSketchColorOverridesBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Turn ON Inventor 'Show Format' (AppLinePropertiesToggleCmd). This is a session/global toggle.",
                "Enable Show Format",
                null,
                null
            );

            var btnReleaseSketchColorHold = defs.AddButtonDefinition(
                "Disable Show Format",
                "RMAC_ReleaseSketchColorHoldBtn",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Turn OFF Inventor 'Show Format' (AppLinePropertiesToggleCmd). This is a session/global toggle.",
                "Disable Show Format",
                null,
                null
            );

            var btnHighlight = defs.AddButtonDefinition(
                "Highlight",
                "RMAC_BtnHighlight",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Enable sketch highlight formatting.",
                "Highlight",
                null,
                null
            );

            var btnClearHighlight = defs.AddButtonDefinition(
                "Clear H-Light",
                "RMAC_BtnClearHighlight",
                CommandTypesEnum.kQueryOnlyCmdType,
                _clientId,
                "Clear sketch highlight formatting.",
                "Clear H-Light",
                null,
                null
            );

            // Wire events (store delegate instances!)
            _hSettings = _cb.BtnSettings_OnExecute;
            btnSettings.OnExecute += _hSettings;

            _hCheckComments = _cb.BtnCheckComments_OnExecute;
            btnCheckComments.OnExecute += _hCheckComments;

            _hEditComments = _cb.BtnEditComments_OnExecute;
            btnEditComments.OnExecute += _hEditComments;

            _hFindSketchCmds = _cb.BtnFindSketchCmds_OnExecute;
            btnFindSketchCmds.OnExecute += _hFindSketchCmds;

            _hLegacySweep = _cb.BtnLegacySweep_OnExecute;
            btnLegacySweep.OnExecute += _hLegacySweep;

            _hSmThicknessFromLine = _cb.BtnSmThicknessFromLine_OnExecute;
            btnSmThicknessFromLine.OnExecute += _hSmThicknessFromLine;

            _hHideAllSketches = _cb.BtnHideAllSketches_OnExecute;
            btnHideAllSketches.OnExecute += _hHideAllSketches;

            _hShowAllSketches = _cb.BtnShowAllSketches_OnExecute;
            btnShowAllSketches.OnExecute += _hShowAllSketches;

            _hShowSketchDims = _cb.BtnShowSketchDims_OnExecute;
            btnShowSketchDims.OnExecute += _hShowSketchDims;

            _hHideSketchDims = _cb.BtnHideSketchDims_OnExecute;
            btnHideSketchDims.OnExecute += _hHideSketchDims;

            _hClearOneSketchEntOverride = _cb.BtnClearOneSketchEntOverride_OnExecute;
            btnClearOneSketchEntOverride.OnExecute += _hClearOneSketchEntOverride;

            _hFindShowFormatCmds = _cb.BtnFindShowFormatCmds_OnExecute;
            btnFindShowFormatCmds.OnExecute += _hFindShowFormatCmds;

            _hClearSketchColorOverrides = _cb.BtnClearSketchColorOverrides_OnExecute;
            btnClearSketchColorOverrides.OnExecute += _hClearSketchColorOverrides;

            _hReleaseSketchColorHold = _cb.BtnReleaseSketchColorHold_OnExecute;
            btnReleaseSketchColorHold.OnExecute += _hReleaseSketchColorHold;

            _hHighlight = new ButtonDefinitionSink_OnExecuteEventHandler(_cb.BtnHighlight_OnExecute);
            btnHighlight.OnExecute += _hHighlight;

            _hClearHighlight = new ButtonDefinitionSink_OnExecuteEventHandler(_cb.BtnClearHighlight_OnExecute);
            btnClearHighlight.OnExecute += _hClearHighlight;

            _hDimModeSelect = _cb.CmbDimMode_OnSelect;
            cmbDimMode.OnSelect += _hDimModeSelect;

            return new RmacCommands
            {
                BtnSettings = btnSettings,
                BtnCheckComments = btnCheckComments,
                BtnEditComments = btnEditComments,
                BtnFindSketchCmds = btnFindSketchCmds,
                BtnLegacySweep = btnLegacySweep,
                BtnSmThicknessFromLine = btnSmThicknessFromLine,
                BtnHideAllSketches = btnHideAllSketches,
                BtnShowAllSketches = btnShowAllSketches,
                CmbDimMode = cmbDimMode,
                BtnShowSketchDims = btnShowSketchDims,
                BtnHideSketchDims = btnHideSketchDims,
                BtnClearOneSketchEntOverride = btnClearOneSketchEntOverride,
                BtnFindShowFormatCmds = btnFindShowFormatCmds,
                BtnClearSketchColorOverrides = btnClearSketchColorOverrides,
                BtnReleaseSketchColorHold = btnReleaseSketchColorHold,
                BtnHighlight = btnHighlight,
                BtnClearHighlight = btnClearHighlight,
            };
        }

        public void Unhook(RmacCommands commands)
        {
            if (commands == null) return;

            try { if (_hSettings != null) commands.BtnSettings.OnExecute -= _hSettings; } catch { }
            try { if (_hCheckComments != null) commands.BtnCheckComments.OnExecute -= _hCheckComments; } catch { }
            try { if (_hEditComments != null) commands.BtnEditComments.OnExecute -= _hEditComments; } catch { }
            try { if (_hFindSketchCmds != null) commands.BtnFindSketchCmds.OnExecute -= _hFindSketchCmds; } catch { }
            try { if (_hLegacySweep != null) commands.BtnLegacySweep.OnExecute -= _hLegacySweep; } catch { }
            try { if (_hSmThicknessFromLine != null) commands.BtnSmThicknessFromLine.OnExecute -= _hSmThicknessFromLine; } catch { }
            try { if (_hHideAllSketches != null) commands.BtnHideAllSketches.OnExecute -= _hHideAllSketches; } catch { }
            try { if (_hShowAllSketches != null) commands.BtnShowAllSketches.OnExecute -= _hShowAllSketches; } catch { }
            try { if (_hShowSketchDims != null) commands.BtnShowSketchDims.OnExecute -= _hShowSketchDims; } catch { }
            try { if (_hHideSketchDims != null) commands.BtnHideSketchDims.OnExecute -= _hHideSketchDims; } catch { }
            try { if (_hClearOneSketchEntOverride != null) commands.BtnClearOneSketchEntOverride.OnExecute -= _hClearOneSketchEntOverride; } catch { }
            try { if (_hFindShowFormatCmds != null) commands.BtnFindShowFormatCmds.OnExecute -= _hFindShowFormatCmds; } catch { }
            try { if (_hClearSketchColorOverrides != null) commands.BtnClearSketchColorOverrides.OnExecute -= _hClearSketchColorOverrides; } catch { }
            try { if (_hReleaseSketchColorHold != null) commands.BtnReleaseSketchColorHold.OnExecute -= _hReleaseSketchColorHold; } catch { }
            try { if (_hDimModeSelect != null) commands.CmbDimMode.OnSelect -= _hDimModeSelect; } catch { }
            try { if (_hHighlight != null) commands.BtnHighlight.OnExecute -= _hHighlight; } catch { }
            try { if (_hClearHighlight != null) commands.BtnClearHighlight.OnExecute -= _hClearHighlight; } catch { }
        }
    }
}
