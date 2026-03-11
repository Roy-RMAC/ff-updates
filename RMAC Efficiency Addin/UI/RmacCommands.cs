using Inventor;

namespace RMAC_Efficiency_Addin.UI
{
    internal sealed class RmacCommands
    {
        public ButtonDefinition BtnSettings { get; init; } = null!;
        public ButtonDefinition BtnCheckComments { get; init; } = null!;
        public ButtonDefinition BtnEditComments { get; init; } = null!;
        public ButtonDefinition BtnFindSketchCmds { get; init; } = null!;
        public ButtonDefinition BtnLegacySweep { get; init; } = null!;
        public ButtonDefinition BtnClearOneSketchEntOverride { get; init; } = null!;
        public ButtonDefinition BtnFindShowFormatCmds { get; init; } = null!;

        public ButtonDefinition BtnSmThicknessFromLine { get; init; } = null!;

        public ButtonDefinition BtnHideAllSketches { get; init; } = null!;
        public ButtonDefinition BtnShowAllSketches { get; init; } = null!;

        public ComboBoxDefinition CmbDimMode { get; init; } = null!;
        public ButtonDefinition BtnShowSketchDims { get; init; } = null!;
        public ButtonDefinition BtnHideSketchDims { get; init; } = null!;

        public ButtonDefinition BtnClearSketchColorOverrides { get; init; } = null!;
        public ButtonDefinition BtnReleaseSketchColorHold { get; init; } = null!;

        public ButtonDefinition BtnHighlight { get; init; } = null!;
        public ButtonDefinition BtnClearHighlight { get; init; } = null!;
    }
}
