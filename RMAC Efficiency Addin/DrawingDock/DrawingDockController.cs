using Inventor;
using System;
using RMAC_Efficiency_Addin.Infrastructure;
using System.Runtime.InteropServices;

namespace RMAC_Efficiency_Addin.DrawingDock
{
    internal sealed class DrawingDockController : IAddinController
    {
        private readonly Inventor.Application _app;
        private readonly string _clientId;

        private DockableWindow? _dockWin;
        private DrawingDockPane? _pane;

        private const string DockInternalName = "RMAC_DrawingDock";
        private const string DockTitle = "RMAC Drawing Tools";

        private bool _started;
        public bool IsStarted => _started;

        public DrawingDockController(Inventor.Application app, string clientId)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        }

        public void Start()
        {
            if (_started) return;

            EnsureDockCreated();
            OnActivateDocument();

            _started = true;
        }

        public void Stop()
        {
            try
            {
                if (_dockWin != null)
                {
                    try { _dockWin.Visible = false; } catch { }
                }
            }
            finally
            {
                if (_dockWin != null)
                {
                    try { Marshal.FinalReleaseComObject(_dockWin); } catch { }
                    _dockWin = null;
                }

                try { _pane?.Dispose(); } catch { }
                _pane = null;
            }

            _started = false;
        }

        /// <summary>
        /// Call this from AppEvents_OnActivateDocument (kAfter) to show/hide based on environment.
        /// </summary>
        public void OnActivateDocument()
        {
            bool isDrawing = _app.ActiveDocument is DrawingDocument;

            if (_dockWin == null)
            {
                if (!isDrawing) return;
                EnsureDockCreated();
            }

            try { _dockWin!.Visible = isDrawing; } catch { }

            if (isDrawing)
            {
                try { _pane?.RefreshSelectedAssemblyFromCurrentContext(); } catch { }
            }
        }

        private void EnsureDockCreated()
        {
            if (_dockWin != null) return;

            var uiMgr = _app.UserInterfaceManager;

            try
            {
                _dockWin = uiMgr.DockableWindows[DockInternalName];
            }
            catch
            {
                _dockWin = uiMgr.DockableWindows.Add(_clientId, DockInternalName, DockTitle);
            }

            _pane ??= new DrawingDockPane(_app);

            _pane.CreateControl();
            nint hwnd = _pane.Handle;

            try
            {
                _dockWin.AddChild(hwnd);
            }
            catch
            {
                // Swallow intentionally: persisted dock may already have a child.
            }

            try { _dockWin.Visible = false; } catch { }
        }

        public void RefreshPaneContext()
        {
            try { _pane?.RefreshSelectedAssemblyFromCurrentContext(); } catch { }
        }

        public void Dispose() => Stop();
    }
}
