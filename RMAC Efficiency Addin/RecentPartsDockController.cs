using Inventor;
using System;
using RMAC_Efficiency_Addin.Infrastructure;

namespace RMAC_Efficiency_Addin
{
    internal sealed class RecentPartsDockController : IAddinController
    {
        private readonly Inventor.Application _app;
        private readonly string _clientId;

        private DockableWindow? _dockWin;
        private RecentPartsPane? _pane;

        private RecentPartsService? _service;

        private const string DockInternalName = "RMAC_RecentPartsDockWin";

                private bool _started;

        public bool IsStarted => _started;

        private bool _lastVisible;

        public RecentPartsDockController(Inventor.Application app, string clientId)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        }

        public void Start()
        {
            if (_started) return;

            EnsureDockableWindow();

            _service = new RecentPartsService(_app, capacity: 5);
            _service.ListChanged += Service_ListChanged;
            _service.Start();

            _pane?.SetItems(_service.Items);

            ApplyVisibility();

            _started = true;
}
        public void Stop()
        {
            if (!_started)
            {
                // still attempt best-effort cleanup
            }
// Unhook pane events
            if (_pane != null)
            {
                try
                {
                    _pane.PlaceRequested -= Pane_PlaceRequested;
                    _pane.PlaceGroundedRequested -= Pane_PlaceGroundedRequested;
                }
                catch { /* ignore */ }
            }

            // Stop MRU tracking
            try
            {
                if (_service != null)
                {
                    _service.ListChanged -= Service_ListChanged;
                    _service.Stop();
                }
            }
            catch { /* ignore */ }

            _service = null;

            // Hide dock window
            try { if (_dockWin != null) _dockWin.Visible = false; } catch { }

            try { _pane?.Dispose(); } catch { }
            _pane = null;
            _dockWin = null;

            _started = false;
}
        public void OnActivateDocument()
        {
            ApplyVisibility();
        }

        private void Service_ListChanged()
        {
            if (_pane == null || _service == null) return;
            _pane.SetItems(_service.Items);
        }

        private void EnsureDockableWindow()
        {
            if (_dockWin != null) return;

            var ui = _app.UserInterfaceManager;
            var docks = ui.DockableWindows;

            try { _dockWin = docks[DockInternalName]; }
            catch { _dockWin = docks.Add(_clientId, DockInternalName, "Recent Parts"); }

            if (_pane == null)
            {
                _pane = new RecentPartsPane();
                _pane.PlaceRequested += Pane_PlaceRequested;
                _pane.PlaceGroundedRequested += Pane_PlaceGroundedRequested;
            }

            try { _dockWin.AddChild(_pane.Handle); }
            catch { /* already attached */ }

            try
            {
                _dockWin.DockingState = DockingStateEnum.kDockRight;
                _dockWin.Visible = false;
            }
            catch { /* ignore */ }
        }

        private void ApplyVisibility()
        {
            if (_dockWin == null) return;

            bool isAssembly = _app.ActiveDocument is AssemblyDocument;

            if (isAssembly != _lastVisible)
            {
                _lastVisible = isAssembly;
                RMAC_Efficiency_Addin.Infrastructure.DockUiRefresh
                    .ForceToggleVisible(_dockWin, _pane, isAssembly);
            }
            else
            {
                try { _dockWin.Visible = isAssembly; } catch { /* ignore */ }
            }
        }


        // --------------------------------------------------------------------
        // Button actions
        // --------------------------------------------------------------------

        private void Pane_PlaceRequested(string fullPath)
        {
            if (!EnsureActiveAssembly(out _))
                return;

            if (!ValidatePath(fullPath))
                return;

            // Native "Place Component" with file pre-seeded so Inventor enters
            // interactive placement mode (cursor attached; user can click multiple).
            try
            {
                _app.CommandManager.PostPrivateEvent(
                    PrivateEventTypeEnum.kFileNameEvent,
                    fullPath);

                var def = _app.CommandManager.ControlDefinitions["AssemblyPlaceComponentCmd"];
                def.Execute();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.Message, "Place - Error");
            }
        }

        private void Pane_PlaceGroundedRequested(string fullPath)
        {
            if (!EnsureActiveAssembly(out var asmDoc))
                return;

            if (!ValidatePath(fullPath))
                return;

            try
            {
                RunFast("RMAC Place - Grounded", () =>
                {
                    PlaceAtAssemblyOriginConstrained(asmDoc, fullPath);
                });
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.Message, "Place - Grounded - Error");
            }
        }

        // --------------------------------------------------------------------
        // Implementation details
        // --------------------------------------------------------------------

        private static bool ValidatePath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                System.Windows.Forms.MessageBox.Show("No file path was provided.");
                return false;
            }

            if (!global::System.IO.File.Exists(fullPath))
            {
                System.Windows.Forms.MessageBox.Show("File does not exist:\n" + fullPath);
                return false;
            }

            return true;
        }

        private bool EnsureActiveAssembly(out AssemblyDocument asmDoc)
        {
            asmDoc = null!;
            if (_app.ActiveDocument is not AssemblyDocument a)
            {
                System.Windows.Forms.MessageBox.Show("Open an Assembly document to use this command.");
                return false;
            }
            asmDoc = a;
            return true;
        }

        private void PlaceAtAssemblyOriginConstrained(AssemblyDocument asmDoc, string fullPath)
        {
            var asmDef = (AssemblyComponentDefinition)asmDoc.ComponentDefinition;
            var tg = _app.TransientGeometry;

            // Add occurrence at identity transform (assembly origin)
            var occ = asmDef.Occurrences.Add(fullPath, tg.CreateMatrix());

            try
            {
                // Assembly origin planes (Inventor default: 1=YZ, 2=XZ, 3=XY)
                var asmYZ = asmDef.WorkPlanes[1];
                var asmXZ = asmDef.WorkPlanes[2];
                var asmXY = asmDef.WorkPlanes[3];

                // Occurrence definition planes (part or sub-assembly)
                WorkPlanes occPlanes;

                if (occ.Definition is PartComponentDefinition pDef)
                    occPlanes = pDef.WorkPlanes;
                else if (occ.Definition is AssemblyComponentDefinition aDef)
                    occPlanes = aDef.WorkPlanes;
                else
                    throw new InvalidOperationException("Unsupported component definition type: " + occ.Definition.Type);

                var occYZ = occPlanes[1];
                var occXZ = occPlanes[2];
                var occXY = occPlanes[3];

                // Proxies in assembly context
                occ.CreateGeometryProxy(occYZ, out object o1);
                occ.CreateGeometryProxy(occXZ, out object o2);
                occ.CreateGeometryProxy(occXY, out object o3);

                var pYZ = (WorkPlaneProxy)o1;
                var pXZ = (WorkPlaneProxy)o2;
                var pXY = (WorkPlaneProxy)o3;

                // 0-offset flush constraints to lock occurrence origin to assembly origin
                asmDef.Constraints.AddFlushConstraint(asmYZ, pYZ, 0);
                asmDef.Constraints.AddFlushConstraint(asmXZ, pXZ, 0);
                asmDef.Constraints.AddFlushConstraint(asmXY, pXY, 0);

                // Optional: ensure it's not grounded (some workflows auto-ground)
                try { occ.Grounded = false; } catch { /* ignore */ }
            }
            catch
            {
                // Fallback to previous behavior if constraints fail for any reason
                try { occ.Grounded = true; } catch { }
            }
        }

        private void RunFast(string name, Action action)
        {
            bool oldSU = _app.ScreenUpdating;
            Transaction? tx = null;

            try
            {
                _app.ScreenUpdating = false;

                if (_app.ActiveDocument != null)
                    tx = _app.TransactionManager.StartTransaction(_app.ActiveDocument, name);

                action();

                tx?.End();
            }
            catch
            {
                try { tx?.Abort(); } catch { }
                throw;
            }
            finally
            {
                _app.ScreenUpdating = oldSU;
                try { _app.ActiveView.Update(); } catch { }
            }
        }
        public void Dispose()
        {
            Stop();
        }

    }
}
