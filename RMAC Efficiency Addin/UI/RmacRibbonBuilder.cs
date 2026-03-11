using Inventor;
using System;

namespace RMAC_Efficiency_Addin.UI
{
    internal sealed class RmacRibbonBuilder
    {
        private readonly Inventor.Application _app;
        private readonly _3DModelRibbon _modelRibbon;
        private readonly _SheetMetalRibbon _sheetMetalRibbon;
        private readonly _ToolsRibbon _toolsRibbon;

        public RmacRibbonBuilder(Inventor.Application app, string clientId)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentNullException(nameof(clientId));

            _modelRibbon = new _3DModelRibbon(clientId);
            _sheetMetalRibbon = new _SheetMetalRibbon(clientId);
            _toolsRibbon = new _ToolsRibbon(clientId);
        }

        public void Build(RmacCommands cmd)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));

            var uiMan = _app.UserInterfaceManager;
            if (uiMan == null) return;

            try { _modelRibbon.Build(uiMan, cmd); } catch { }
            try { _sheetMetalRibbon.Build(uiMan, cmd); } catch { }
            try { _toolsRibbon.Build(uiMan, cmd); } catch { }
        }
    }
}
