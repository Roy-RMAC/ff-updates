using Inventor;
using System;
using RMAC_Efficiency_Addin.Infrastructure;
using RMAC_Efficiency_Addin.Layouts;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using IODir = System.IO.Directory;
// Avoid Inventor.File / Inventor.Path collisions
using IOFile = System.IO.File;
using IOPath = System.IO.Path;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// RMAC Layouts controller:
    /// - DockableWindow on RHS
    /// - DERIVE PARTS list stored in per-project JSON
    /// - Add/Remove/Derive actions
    /// - Custom Derive: select Sketch2D / Sketch3D / WorkPlane and derive only those
    /// - "Derived in Active Part" shows Part Numbers of derived sources
    /// </summary>
    public sealed class LayoutsController : IAddinController
    {
        // Test-accessibility hook. Set in Start(), cleared in Stop()/Dispose().
        // The self-test harness uses this to call internal command methods directly,
        // bypassing the dock UI state. Production code should never depend on this.
        public static LayoutsController? Instance { get; private set; }

        private bool _started;

        public bool IsStarted => _started;

        private readonly global::Inventor.Application _app;
        private readonly string _clientId;

        private DockableWindow? _dock;
        private Control? _root;

        private ListBox? _deriveList;
        private ListBox? _derivedInActiveList;

        private Button? _btnAdd;
        private Button? _btnRemove;
        private Button? _btnDerive;
        private Button? _btnCustomDerive;
        private Button? _btnEditDerive;
        private volatile bool _mtFinishRequested;
        private volatile bool _mtCancelRequested;

        // Cache to avoid IO on every activation
        private string? _activeIpjPath;

        // Dev: unique name avoids stale child HWND blank docks. Later you can make this constant.
        private readonly string _dockInternalName = "RMAC_LayoutsDock_" + Guid.NewGuid().ToString("N");

        public LayoutsController(global::Inventor.Application app, string clientId)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        }

        public void Start()
        {
            if (_started) return;
EnsureDockCreated();
            UpdateOnActivation();

            _started = true;
            Instance = this;
}
        public void Stop()
        {
            if (!_started)
            {
                // still attempt best-effort cleanup
            }
try { if (_dock != null) _dock.Visible = false; } catch { }

            try { _root?.Dispose(); } catch { }
            _root = null;
            _dock = null;

            _deriveList = null;
            _derivedInActiveList = null;

            _btnAdd = null;
            _btnRemove = null;
            _btnDerive = null;
            _btnCustomDerive = null;
            _btnEditDerive = null;

            _activeIpjPath = null;

            _started = false;
            if (ReferenceEquals(Instance, this)) Instance = null;
}

        public void OnActivateDocument() => UpdateOnActivation();

        private void EnsureDockCreated()
        {
            if (_dock != null && _root != null) return;

            var uiMgr = _app.UserInterfaceManager;

            if (_dock == null)
            {
                _dock = uiMgr.DockableWindows.Add(_clientId, _dockInternalName, "RMAC Layouts");
                try { _dock.DockingState = DockingStateEnum.kDockRight; } catch { /* ignore */ }
            }

            if (_root == null)
            {
                _root = BuildUi();

                // Force handle creation before AddChild
                _root.CreateControl();
                _dock.AddChild(_root.Handle.ToInt64());

                _dock.Visible = true;
            }
        }

        private Control BuildUi()
        {
            // Dark theme colors
            var bg = System.Drawing.Color.FromArgb(45, 45, 48);
            var panelBg = System.Drawing.Color.FromArgb(37, 37, 38);
            var border = System.Drawing.Color.FromArgb(62, 62, 64);
            var text = System.Drawing.Color.Gainsboro;

            var root = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                BackColor = bg
            };

            // New layout:
            // 0: Add/Remove (above derive list)
            // 1: Derive parts list
            // 2: Full Derive + Custom Derive (below derive list)
            // 3: Derived-in-active (fixed)
            // 4: filler
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = bg
            };

            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // top buttons
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 300)); // derive parts
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // bottom buttons
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 200)); // derived-in-active FIXED
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // edit derive button
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // filler absorbs remainder

            // --- TOP Buttons (Add / Remove) ---
            var topButtonStrip = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = bg
            };

            _btnAdd = MakeButton("Add...", 120, border, text);
            _btnRemove = MakeButton("Remove", 120, border, text);

            _btnAdd.Left = 0;
            _btnRemove.Left = _btnAdd.Right + 8;
            _btnAdd.Top = _btnRemove.Top = 6;

            _btnAdd.Click += (s, e) =>
            {
                try { AddDerivePartViaDialog(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "RMAC Layouts - Add"); }
            };

            _btnRemove.Click += (s, e) =>
            {
                try { RemoveSelectedDerivePart(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "RMAC Layouts - Remove"); }
            };

            _btnRemove.Enabled = false;

            topButtonStrip.Controls.Add(_btnAdd);
            topButtonStrip.Controls.Add(_btnRemove);

            // --- DERIVE PARTS group ---
            var grpDerive = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "DERIVE PARTS",
                ForeColor = text,
                BackColor = bg,
                Padding = new Padding(10, 12, 10, 10)
            };

            var listFrame = MakeFramedPanel(panelBg, border);
            _deriveList = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                BackColor = System.Drawing.Color.FromArgb(30, 30, 30),
                ForeColor = text,
                BorderStyle = BorderStyle.None,
                SelectionMode = SelectionMode.One
            };
            _deriveList.Items.Add("(No derive parts yet)");
            listFrame.Controls.Add(_deriveList);
            grpDerive.Controls.Add(listFrame);

            // --- BOTTOM Buttons (FULL DERIVE / CUSTOM DERIVE) ---
            var bottomButtonStrip = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = bg
            };

            _btnDerive = MakeButton("FULL DERIVE", 190, border, text);
            _btnCustomDerive = MakeButton("CUSTOM DERIVE", 190, border, text);

            _btnDerive.Left = 0;
            _btnCustomDerive.Left = _btnDerive.Right + 8;
            _btnDerive.Top = _btnCustomDerive.Top = 6;

            _btnDerive.Click += (s, e) =>
            {
                try { DeriveSelectedIntoActivePart(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "RMAC Layouts - Full Derive"); }
            };

            _btnCustomDerive.Click += (s, e) =>
            {
                try { CustomDeriveSelectedIntoActivePart(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "RMAC Layouts - Custom Derive"); }
            };

            _btnDerive.Enabled = false;
            _btnCustomDerive.Enabled = false;

            bottomButtonStrip.Controls.Add(_btnDerive);
            bottomButtonStrip.Controls.Add(_btnCustomDerive);

            // Selection enables/disables Remove/Derive/Custom Derive
            _deriveList.SelectedIndexChanged += (s, e) =>
            {
                bool has = _deriveList.SelectedItem is DerivePartEntry;
                _btnRemove!.Enabled = has;
                _btnDerive!.Enabled = has;
                _btnCustomDerive!.Enabled = has;
            };

            // --- Derived in Active Part group (unchanged) ---
            var derivedArea = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = bg,
                Padding = new Padding(0, 8, 0, 0)
            };

            var grpDerivedNow = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "DERIVED IN ACTIVE PART",
                ForeColor = text,
                BackColor = bg,
                Padding = new Padding(10, 12, 10, 10)
            };

            var derivedFrame = MakeFramedPanel(panelBg, border);
            _derivedInActiveList = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                BackColor = System.Drawing.Color.FromArgb(30, 30, 30),
                ForeColor = text,
                BorderStyle = BorderStyle.None,
                SelectionMode = SelectionMode.One
            };
            _derivedInActiveList.Items.Add("(No derived sources yet)");
            derivedFrame.Controls.Add(_derivedInActiveList);
            grpDerivedNow.Controls.Add(derivedFrame);
            derivedArea.Controls.Add(grpDerivedNow);

            // --- Edit Derive button strip ---
            var editDeriveStrip = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = bg
            };

            _btnEditDerive = MakeButton("EDIT DERIVE", 190, border, text);
            _btnEditDerive.Left = 0;
            _btnEditDerive.Top = 6;
            _btnEditDerive.Enabled = false;

            _btnEditDerive.Click += (s, e) =>
            {
                try { EditDeriveSelected(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "RMAC Layouts - Edit Derive"); }
            };

            editDeriveStrip.Controls.Add(_btnEditDerive);

            _derivedInActiveList.SelectedIndexChanged += (s, e) => UpdateEditDeriveEnabled();

            // --- Assemble layout ---
            layout.Controls.Add(topButtonStrip, 0, 0);
            layout.Controls.Add(grpDerive, 0, 1);
            layout.Controls.Add(bottomButtonStrip, 0, 2);
            layout.Controls.Add(derivedArea, 0, 3);
            layout.Controls.Add(editDeriveStrip, 0, 4);

            // filler row (row 5) intentionally left empty

            root.Controls.Add(layout);
            return root;
        }


        private static Panel MakeFramedPanel(System.Drawing.Color fill, System.Drawing.Color border)
        {
            var p = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(6),
                BackColor = fill
            };

            p.Paint += (s, e) =>
            {
                using var pen = new System.Drawing.Pen(border);
                var r = p.ClientRectangle;
                r.Width -= 1;
                r.Height -= 1;
                e.Graphics.DrawRectangle(pen, r);
            };

            return p;
        }

        private static Button MakeButton(string text, int width, System.Drawing.Color border, System.Drawing.Color fore)
        {
            var btn = new Button
            {
                Text = text,
                Width = width,
                Height = 28,
                BackColor = System.Drawing.Color.FromArgb(63, 63, 70),
                ForeColor = fore,
                FlatStyle = FlatStyle.Flat
            };
            btn.FlatAppearance.BorderColor = border;
            btn.FlatAppearance.BorderSize = 1;
            return btn;
        }

        private bool _lastVisible;

        private void UpdateOnActivation()
        {
            try
            {
                EnsureDockCreated();

                bool isPart = _app.ActiveDocument is PartDocument;

                if (_dock != null)
                {
                    // Only do the heavy refresh when the visibility state changes
                    bool willShow = isPart;
                    if (willShow != _lastVisible)
                    {
                        _lastVisible = willShow;
                        RMAC_Efficiency_Addin.Infrastructure.DockUiRefresh
    .ForceToggleVisible(_dock, _root, willShow);

                        // Optional “bring forward” attempt via Win32/WinForms (safe no-op if it can’t)
                        RMAC_Efficiency_Addin.Infrastructure.DockUiRefresh.BringToFront(_root);
                    }
                    else
                    {
                        try { _dock.Visible = willShow; } catch { }
                    }
                }

                RefreshProjectContextIfChanged();
                RefreshDerivedInActivePartList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "RMAC Layouts - Error");
            }
        }

        private void RefreshProjectContextIfChanged()
        {
            var proj = _app.DesignProjectManager?.ActiveDesignProject;
            if (proj == null) return;

            string ipjFullPath = proj.FullFileName;
            string workspaceRoot = proj.WorkspacePath;

            if (string.IsNullOrWhiteSpace(ipjFullPath) || string.IsNullOrWhiteSpace(workspaceRoot))
                return;

            if (string.Equals(_activeIpjPath, ipjFullPath, StringComparison.OrdinalIgnoreCase))
                return;

            _activeIpjPath = ipjFullPath;

            var jsonPath = EnsureProjectEfficiencyJsonExists(ipjFullPath, workspaceRoot);

            var model = LoadEfficiencyData(jsonPath);
            PopulateDeriveList(model);
        }

        private void PopulateDeriveList(EfficiencyDataFile model)
        {
            if (_deriveList == null) return;

            _deriveList.BeginUpdate();
            try
            {
                _deriveList.Items.Clear();

                if (model.DeriveParts == null || model.DeriveParts.Count == 0)
                {
                    _deriveList.Items.Add("(No derive parts yet)");
                    return;
                }

                foreach (var p in model.DeriveParts)
                    _deriveList.Items.Add(p);
            }
            finally
            {
                _deriveList.EndUpdate();
            }
        }

        private void AddDerivePartViaDialog()
        {
            var proj = _app.DesignProjectManager?.ActiveDesignProject
                       ?? throw new InvalidOperationException("No active Inventor project.");

            string ipjFullPath = proj.FullFileName;
            string workspaceRoot = proj.WorkspacePath;

            if (string.IsNullOrWhiteSpace(ipjFullPath) || string.IsNullOrWhiteSpace(workspaceRoot))
                throw new InvalidOperationException("Active project does not provide a valid IPJ/workspace.");

            string jsonPath = EnsureProjectEfficiencyJsonExists(ipjFullPath, workspaceRoot);

            using var dlg = new OpenFileDialog
            {
                Title = "Select layout parts to add to DERIVE PARTS",
                Filter = "Inventor Part (*.ipt)|*.ipt",
                Multiselect = true,
                CheckFileExists = true,
                CheckPathExists = true,
                RestoreDirectory = true,
                InitialDirectory = workspaceRoot
            };

            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            var selectedPaths = dlg.FileNames;
            if (selectedPaths == null || selectedPaths.Length == 0)
                return;

            var model = LoadEfficiencyData(jsonPath);
            model.DeriveParts ??= new List<DerivePartEntry>();

            bool changed = false;

            foreach (var selectedPath in selectedPaths)
            {
                if (string.IsNullOrWhiteSpace(selectedPath)) continue;

                bool exists = model.DeriveParts.Any(x =>
                    string.Equals(x.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));

                if (exists) continue;

                string partNumber = ReadPartNumber(selectedPath);

                model.DeriveParts.Add(new DerivePartEntry
                {
                    PartNumber = partNumber,
                    FilePath = selectedPath
                });

                changed = true;
            }

            if (changed)
            {
                model.UpdatedUtc = DateTime.UtcNow;
                SaveEfficiencyDataAtomic(jsonPath, model);
            }

            PopulateDeriveList(model);
        }

        private void RemoveSelectedDerivePart()
        {
            if (_deriveList == null) return;

            if (_deriveList.SelectedItem is not DerivePartEntry selected)
            {
                MessageBox.Show("Select a derive part to remove.");
                return;
            }

            var proj = _app.DesignProjectManager?.ActiveDesignProject
                       ?? throw new InvalidOperationException("No active Inventor project.");

            string ipjFullPath = proj.FullFileName;
            string workspaceRoot = proj.WorkspacePath;

            if (string.IsNullOrWhiteSpace(ipjFullPath) || string.IsNullOrWhiteSpace(workspaceRoot))
                throw new InvalidOperationException("Active project does not provide a valid IPJ/workspace.");

            string jsonPath = EnsureProjectEfficiencyJsonExists(ipjFullPath, workspaceRoot);

            var model = LoadEfficiencyData(jsonPath);
            model.DeriveParts ??= new List<DerivePartEntry>();

            int removed = model.DeriveParts.RemoveAll(x =>
                string.Equals(x.FilePath, selected.FilePath, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                model.UpdatedUtc = DateTime.UtcNow;
                SaveEfficiencyDataAtomic(jsonPath, model);
            }

            PopulateDeriveList(model);
        }

        private void DeriveSelectedIntoActivePart()
        {
            if (_deriveList == null) return;

            if (_deriveList.SelectedItem is not DerivePartEntry selected)
            {
                MessageBox.Show("Select a derive part first.");
                return;
            }

            if (_app.ActiveDocument is not PartDocument targetPart)
            {
                MessageBox.Show("Open a Part document to derive into.");
                return;
            }

            if (IsSketchEditActive())
                throw new InvalidOperationException("Please exit sketch edit mode before deriving.");

            var sourcePath = selected.FilePath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !IOFile.Exists(sourcePath))
            {
                MessageBox.Show("Source file does not exist:\n" + sourcePath);
                return;
            }

            if (CustomDeriveCore.IsAlreadyDerived(targetPart, sourcePath))
            {
                MessageBox.Show("That part is already derived into the active part.");
                return;
            }

            // Snapshot BEFORE derive so we can style only newly created sketches (no polling)
            var before = SnapshotSketchIUnknowns(targetPart);

            Transaction? tx = null;
            bool oldSU = _app.ScreenUpdating;

            try
            {
                _app.ScreenUpdating = false;
                tx = _app.TransactionManager.StartTransaction((Inventor._Document)_app.ActiveDocument, "RMAC Derive Part");

                CustomDeriveCore.AddFullDerive(targetPart, sourcePath);

                // Ensure derived objects are instantiated before we enumerate sketches
                try { targetPart.Update(); } catch { }
                try { ((Inventor._Document)targetPart).Update(); } catch { }

                // Apply inactive overrides to newly derived sketches (visibility doesn't matter)
                ApplyInactiveToNewSketches(targetPart, before);

                tx.End();
            }
            catch (Exception ex)
            {
                try { tx?.Abort(); } catch { }
                throw new InvalidOperationException("Derive failed: " + ex.Message, ex);
            }
            finally
            {
                _app.ScreenUpdating = oldSU;
                try { _app.ActiveView.Update(); } catch { }
            }

            RefreshDerivedInActivePartList();
        }

        // Thin UI wrapper — what the ribbon button still calls. Reads UI state, validates,
        // delegates the real work to RunCustomDerive (which is internal so tests can call it).
        private void CustomDeriveSelectedIntoActivePart()
        {
            if (_deriveList == null) return;

            if (_deriveList.SelectedItem is not DerivePartEntry selected)
            {
                MessageBox.Show("Select a derive part first.");
                return;
            }

            if (_app.ActiveDocument is not PartDocument targetPart)
            {
                MessageBox.Show("Open a Part document to derive into.");
                return;
            }

            try
            {
                RunCustomDerive(targetPart, selected.FilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "RMAC Layouts - Custom Derive");
            }
        }

        /// <summary>
        /// Run the custom derive command against an explicit target and source. Opens (or
        /// activates) the source part, runs the interactive selection session, and adds the
        /// resulting derived component to the target. Throws on validation failures and on
        /// "no items selected" so callers (production wrapper or tests) can decide how to
        /// present the error.
        /// </summary>
        internal void RunCustomDerive(PartDocument targetPart, string sourceFullPath)
        {
            if (targetPart == null) throw new ArgumentNullException(nameof(targetPart));
            if (string.IsNullOrWhiteSpace(sourceFullPath))
                throw new ArgumentException("Source path is required.", nameof(sourceFullPath));

            if (IsSketchEditActive())
                throw new InvalidOperationException("Please exit sketch edit mode before deriving.");

            if (!IOFile.Exists(sourceFullPath))
                throw new InvalidOperationException("Source file does not exist:\n" + sourceFullPath);

            if (CustomDeriveCore.IsAlreadyDerived(targetPart, sourceFullPath))
                throw new InvalidOperationException("That part is already derived into the active part.");

            bool openedHere = false;
            Inventor._Document? openedDoc = null;
            PartDocument? sourcePart = null;

            Transaction? tx = null;
            bool oldSU = _app.ScreenUpdating;

            try
            {
                try { ((Inventor._Document)_app.ActiveDocument).SelectSet.Clear(); } catch { }
                try { _app.CommandManager.StopActiveCommand(); } catch { }

                openedDoc = FindOpenDocumentByPath(sourceFullPath);
                if (openedDoc != null)
                {
                    sourcePart = openedDoc as PartDocument;

                    // If it's open but activation fails, force a close/reopen (fixes "won't switch window")
                    if (sourcePart != null)
                    {
                        if (!ForceActivateDocument((Inventor._Document)sourcePart))
                        {
                            var reopened = ForceReopenVisiblePart(sourceFullPath);
                            if (reopened == null)
                                throw new InvalidOperationException("Failed to reopen the source layout part.");

                            openedDoc = reopened;
                            sourcePart = reopened as PartDocument;
                            openedHere = true;

                            if (sourcePart == null)
                                throw new InvalidOperationException("Reopened source is not a Part document.");
                        }
                    }
                }
                else
                {
                    // Not open -> open visible
                    openedDoc = _app.Documents.Open(sourceFullPath, true);
                    openedHere = true;
                    sourcePart = openedDoc as PartDocument;
                }

                if (sourcePart == null)
                    throw new InvalidOperationException("Selected source is not a Part document.");

                // Activate source for selection (now robust)
                BeginBrowserSafeSourceSession(sourcePart);

                var selectedEntityIds = RunSelectionSession(sourcePart);

                if (selectedEntityIds.Count == 0)
                    throw new InvalidOperationException("No items were selected. Nothing was derived.");

                // Return to target and close the source doc if we opened it here.
                EndBrowserSafeSourceSession_NoWindowHandle(targetPart, openedHere, openedDoc);
                openedHere = false; // EndBrowserSafeSourceSession may have closed it; prevent double-close in finally

                // Snapshot BEFORE derive so we can style only newly created sketches (no polling)
                var before = SnapshotSketchIUnknowns(targetPart);

                _app.ScreenUpdating = false;
                tx = _app.TransactionManager.StartTransaction((Inventor._Document)targetPart, "RMAC Custom Derive");

                AddDerivedComponentAndRefresh(targetPart, sourcePart, sourceFullPath, selectedEntityIds);

                // Ensure derived objects are instantiated before we enumerate sketches
                try { targetPart.Update(); } catch { }
                try { ((Inventor._Document)targetPart).Update(); } catch { }

                // Apply inactive overrides to newly derived sketches (visibility doesn't matter)
                ApplyInactiveToNewSketches(targetPart, before);

                tx.End();
                tx = null;
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
                try { targetPart.Activate(); } catch { }

                // Safety: if something went wrong, close source if we opened it.
                if (openedHere && openedDoc != null)
                {
                    try { openedDoc.Close(false); } catch { }
                }
            }
        }

        // ============================================================
        // EDIT DERIVE
        // ============================================================

        // Thin UI wrapper — what the ribbon button still calls.
        private void EditDeriveSelected()
        {
            if (_derivedInActiveList?.SelectedItem is not DerivedSourceItem selected)
            {
                MessageBox.Show("Select a derived source in the list first.");
                return;
            }

            if (_app.ActiveDocument is not PartDocument targetPart)
            {
                MessageBox.Show("Open a Part document first.");
                return;
            }

            try
            {
                RunEditDerive(targetPart, selected.SourceFullPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "RMAC Layouts - Edit Derive");
            }
        }

        /// <summary>
        /// Run the edit-derive command against an explicit target and source. Finds the
        /// existing DerivedPartComponent for that source, opens (or activates) the source,
        /// runs the interactive selection session pre-populated with the currently-included
        /// entities, then deletes the old DPC and recreates it with the new selection.
        /// Throws on validation failures and on "no items selected".
        /// </summary>
        internal void RunEditDerive(PartDocument targetPart, string sourceFullPath)
        {
            if (targetPart == null) throw new ArgumentNullException(nameof(targetPart));
            if (string.IsNullOrWhiteSpace(sourceFullPath))
                throw new ArgumentException("Source path is required.", nameof(sourceFullPath));

            if (IsSketchEditActive())
                throw new InvalidOperationException("Please exit sketch edit mode before editing a derive.");

            if (!IOFile.Exists(sourceFullPath))
                throw new InvalidOperationException("Source file does not exist:\n" + sourceFullPath);

            // Find the existing DerivedPartComponent for this source
            dynamic? existingDpc = CustomDeriveCore.FindDerivedPartComponent(targetPart, sourceFullPath);
            if (existingDpc == null)
                throw new InvalidOperationException("Could not find the derived component for this source.");

            bool openedHere = false;
            Inventor._Document? openedDoc = null;
            PartDocument? sourcePart = null;

            Transaction? tx = null;
            bool oldSU = _app.ScreenUpdating;

            try
            {
                try { ((Inventor._Document)_app.ActiveDocument).SelectSet.Clear(); } catch { }
                try { _app.CommandManager.StopActiveCommand(); } catch { }

                // Open / activate source part (same pattern as Custom Derive)
                openedDoc = FindOpenDocumentByPath(sourceFullPath);
                if (openedDoc != null)
                {
                    sourcePart = openedDoc as PartDocument;
                    if (sourcePart != null)
                    {
                        if (!ForceActivateDocument((Inventor._Document)sourcePart))
                        {
                            var reopened = ForceReopenVisiblePart(sourceFullPath);
                            if (reopened == null)
                                throw new InvalidOperationException("Failed to reopen the source layout part.");
                            openedDoc = reopened;
                            sourcePart = reopened as PartDocument;
                            openedHere = true;
                            if (sourcePart == null)
                                throw new InvalidOperationException("Reopened source is not a Part document.");
                        }
                    }
                }
                else
                {
                    openedDoc = _app.Documents.Open(sourceFullPath, true);
                    openedHere = true;
                    sourcePart = openedDoc as PartDocument;
                }

                if (sourcePart == null)
                    throw new InvalidOperationException("Selected source is not a Part document.");

                // Read which entities are currently included in the derive definition
                var currentlyIncluded = CustomDeriveCore.ReadIncludedEntityIds(existingDpc, (Inventor._Document)sourcePart);

                // Activate source for selection
                BeginBrowserSafeSourceSession(sourcePart);

                // Run selection session with current entities pre-highlighted
                var selectedEntityIds = RunSelectionSession(sourcePart, currentlyIncluded);

                if (selectedEntityIds.Count == 0)
                    throw new InvalidOperationException("No items were selected. Derive was not modified.");

                // Return to target
                EndBrowserSafeSourceSession_NoWindowHandle(targetPart, openedHere, openedDoc);
                openedHere = false; // prevent double-close in finally

                // Snapshot BEFORE edit so we can style only newly created sketches
                var before = SnapshotSketchIUnknowns(targetPart);

                _app.ScreenUpdating = false;
                tx = _app.TransactionManager.StartTransaction((Inventor._Document)targetPart, "RMAC Edit Derive");

                // Inventor's API doesn't reliably propagate in-place changes to an existing
                // DerivedPartComponent definition. ReplaceCustomDerive deletes and recreates.
                CustomDeriveCore.ReplaceCustomDerive(targetPart, sourceFullPath, (Inventor._Document)sourcePart, selectedEntityIds);

                // Force update to propagate changes
                try { targetPart.Update(); } catch { }
                try { ((Inventor._Document)targetPart).Update(); } catch { }

                // Apply inactive overrides to any newly derived sketches
                ApplyInactiveToNewSketches(targetPart, before);

                tx.End();
                tx = null;
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
                try { targetPart.Activate(); } catch { }

                if (openedHere && openedDoc != null)
                {
                    try { openedDoc.Close(false); } catch { }
                }
            }

            RefreshDerivedInActivePartList();
        }

        /// <summary>
        /// Give Inventor some UI time and update view.
        /// </summary>
        private void BeginBrowserSafeSourceSession(PartDocument sourcePart)
        {
            try
            {
                if (!ForceActivateDocument((Inventor._Document)sourcePart))
                    throw new InvalidOperationException("Failed to activate source layout part for selection.");

                // Optional: helps a lot when the window activates but isn't visually updated yet
                try { _app.ActiveView.Fit(); } catch { }
                try { _app.ActiveView.Update(); } catch { }
            }
            catch { }
        }

        private void EndBrowserSafeSourceSession_NoWindowHandle(
            PartDocument targetPart,
            bool closeSourceDoc,
            Document? sourceDocToClose)
        {
            try { targetPart.Activate(); } catch { }

            // Let Inventor process activation/messages
            try { System.Windows.Forms.Application.DoEvents(); } catch { }
            try { _app.ActiveView.Update(); } catch { }

            // Close the source doc if we opened it (this also closes its window)
            if (closeSourceDoc && sourceDocToClose != null)
            {
                try { sourceDocToClose.Close(false); } catch { }
            }

            // UI nudge: update + fit helps the browser repaint correctly
            try { System.Windows.Forms.Application.DoEvents(); } catch { }
            try { targetPart.Update(); } catch { }
            try { _app.ActiveView.Update(); } catch { }
            try { _app.ActiveView.Fit(); } catch { }
            try { _app.ActiveView.Update(); } catch { }
        }

        private void AddDerivedComponentAndRefresh(
            PartDocument targetPart,
            PartDocument sourcePart,
            string sourceFullPath,
            HashSet<string> selectedEntityIds)
        {
            CustomDeriveCore.AddCustomDerive(targetPart, sourceFullPath, (Inventor._Document)sourcePart, selectedEntityIds);

            // Force rebuild so derived descriptors populate
            try { targetPart.Update(); } catch { }
            try { ((Inventor._Document)targetPart).Update(); } catch { }
            try { _app.ActiveView.Update(); } catch { }

            RefreshDerivedInActivePartList();
        }

        private HashSet<string> RunSelectionSession(PartDocument sourcePart,
            HashSet<string>? preSelectedIds = null)
        {
            var selectedEntityIds = new HashSet<string>(StringComparer.Ordinal);
            var idToObj = new Dictionary<string, object>(StringComparer.Ordinal);

            // Pre-populate with already-included entities (for Edit Derive)
            if (preSelectedIds != null && preSelectedIds.Count > 0)
            {
                var srcDoc = (Inventor._Document)sourcePart;
                var cd = sourcePart.ComponentDefinition;

                foreach (object o in cd.Sketches)
                {
                    if (o is PlanarSketch ps)
                    {
                        string id = CustomDeriveCore.GetEntityId(srcDoc, ps);
                        if (preSelectedIds.Contains(id))
                        {
                            selectedEntityIds.Add(id);
                            idToObj[id] = ps;
                        }
                    }
                }

                foreach (object o in cd.Sketches3D)
                {
                    if (o is Sketch3D s3)
                    {
                        string id = CustomDeriveCore.GetEntityId(srcDoc, s3);
                        if (preSelectedIds.Contains(id))
                        {
                            selectedEntityIds.Add(id);
                            idToObj[id] = s3;
                        }
                    }
                }

                foreach (object o in cd.WorkPlanes)
                {
                    if (o is WorkPlane wp)
                    {
                        string id = CustomDeriveCore.GetEntityId(srcDoc, wp);
                        if (preSelectedIds.Contains(id))
                        {
                            selectedEntityIds.Add(id);
                            idToObj[id] = wp;
                        }
                    }
                }
            }

            bool done = false;
            bool cancelled = false;

            HighlightSet? hs = null;
            _mtFinishRequested = false;
            _mtCancelRequested = false;

            MiniToolbar? mt = null;
            dynamic? mtCountLabel = null;

            var doc = (Inventor._Document)sourcePart;

            if (!ReferenceEquals(_app.ActiveDocument, sourcePart))
            {
                if (!ForceActivateDocument((Inventor._Document)sourcePart))
                {
                    MessageBox.Show(
                        "Inventor did not switch to the source layout part window.\n\n" +
                        "Try again. If it persists, save all docs and restart Inventor.",
                        "RMAC Custom Derive");
                    return new HashSet<string>(StringComparer.Ordinal);
                }
            }

            try
            {
                // Activate once
                try { sourcePart.Activate(); } catch { }

                // --- MiniToolbar ---
                // --- MiniToolbar (use built-in green tick / red cross) ---
                try
                {
                    mt = _app.CommandManager.CreateMiniToolbar();

                    // Position near top-left
                    try
                    {
                        var tg = _app.TransientGeometry;
                        mt.Position = tg.CreatePoint2d(40, 40);
                    }
                    catch { }

                    var ctrls = mt.Controls;

                    // Counter label (your interop signature requires 5 args)
                    try
                    {
                        mtCountLabel = ctrls.AddLabel(
                            "RMAC_MT_COUNT_LBL",
                            "Selected: 0",
                            "Selection count",
                            null,
                            null);
                    }
                    catch { mtCountLabel = null; }

                    // Enable built-in OK/Cancel (green tick / red cross) - property names vary by Inventor version
                    // Enable + hook built-in OK/Cancel (green tick / red cross)
                    try
                    {
                        dynamic dmt = mt;

                        object? okObj = null;
                        object? cancelObj = null;

                        // Try common property names for the actual button objects
                        try { okObj = dmt.OkButton; } catch { }
                        try { cancelObj = dmt.CancelButton; } catch { }

                        // Alternate names (seen in some interops)
                        if (okObj == null) { try { okObj = dmt.OKButton; } catch { } }
                        if (okObj == null) { try { okObj = dmt.OkBtn; } catch { } }
                        if (cancelObj == null) { try { cancelObj = dmt.CancelBtn; } catch { } }

                        // Make them visible if possible
                        try { if (okObj != null) ((dynamic)okObj).Visible = true; } catch { }
                        try { if (cancelObj != null) ((dynamic)cancelObj).Visible = true; } catch { }

                        // Hook button click/execute events directly (most reliable)
                        if (okObj != null)
                        {
                            TryHookComEvent(okObj, "OnClick", this, nameof(MtFinish_OnClick), nameof(MtFinish_OnAny));
                            TryHookComEvent(okObj, "OnExecute", this, nameof(MtFinish_OnExecute), nameof(MtFinish_OnAny));
                        }

                        if (cancelObj != null)
                        {
                            TryHookComEvent(cancelObj, "OnClick", this, nameof(MtCancel_OnClick), nameof(MtCancel_OnAny));
                            TryHookComEvent(cancelObj, "OnExecute", this, nameof(MtCancel_OnExecute), nameof(MtCancel_OnAny));
                        }
                    }
                    catch { }

                    // Also hook MiniToolbar-level events (if your version exposes them)
                    TryHookComEvent(mt, "OnOK", this, nameof(MtFinish_OnExecute), nameof(MtFinish_OnClick), nameof(MtFinish_OnAny));
                    TryHookComEvent(mt, "OnOk", this, nameof(MtFinish_OnExecute), nameof(MtFinish_OnClick), nameof(MtFinish_OnAny));
                    TryHookComEvent(mt, "OnOKExecute", this, nameof(MtFinish_OnExecute), nameof(MtFinish_OnClick), nameof(MtFinish_OnAny));

                    TryHookComEvent(mt, "OnCancel", this, nameof(MtCancel_OnExecute), nameof(MtCancel_OnClick), nameof(MtCancel_OnAny));
                    TryHookComEvent(mt, "OnCancelExecute", this, nameof(MtCancel_OnExecute), nameof(MtCancel_OnClick), nameof(MtCancel_OnAny));



                    mt.Visible = true;
                }
                catch
                {
                    mt = null;
                }


                try
                {
                    _app.StatusBarText =
                        "Select SK2D / SK3D / WorkPlanes. Ctrl-click to de-select. OK/Enter = Finish. Esc = Cancel.";
                }
                catch { }

                // Pre-populate highlight set for Edit Derive (show already-included entities)
                if (idToObj.Count > 0)
                {
                    try
                    {
                        hs = doc.CreateHighlightSet();
                        hs.Clear();
                        try { hs.Color = _app.TransientObjects.CreateColor(255, 0, 255); } catch { }
                        foreach (var obj in idToObj.Values)
                        {
                            try { hs.AddItem(obj); } catch { }
                        }
                    }
                    catch { hs = null; }

                    if (mtCountLabel != null)
                    {
                        try { mtCountLabel.DisplayName = $"Selected: {selectedEntityIds.Count}"; }
                        catch { try { mtCountLabel.Text = $"Selected: {selectedEntityIds.Count}"; } catch { } }
                    }

                    try { _app.StatusBarText = $"Selected: {selectedEntityIds.Count} (Ctrl-click to de-select)"; } catch { }
                }

                bool highlightDirty = false;

                var deadline = DateTime.UtcNow.AddMinutes(5);
                while (!done && DateTime.UtcNow < deadline)
                {
                    System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(15);

                    if (mt != null && !_mtFinishRequested && !_mtCancelRequested)
                    {
                        bool toolbarGone = false;

                        try
                        {
                            // In some Inventor interops, Visible never flips, so we treat exceptions as "gone"
                            bool v = mt.Visible;
                            toolbarGone = !v;
                        }
                        catch
                        {
                            // If the green tick closes/destroys the toolbar COM object, Visible read can throw
                            toolbarGone = true;
                        }

                        if (toolbarGone)
                        {
                            _mtFinishRequested = true;
                        }
                    }

                    // Hard escape hatch (always works)
                    if (IsEscDown())
                    {
                        cancelled = true;
                        done = true;
                        break;
                    }

                    if (IsEnterDown())
                    {
                        done = true;
                        break;
                    }

                    // Finish/Cancel via handlers (if events fire)
                    if (_mtFinishRequested)
                    {
                        done = true;
                        break;
                    }

                    if (_mtCancelRequested)
                    {
                        cancelled = true;
                        done = true;
                        break;
                    }

                    try
                    {
                        if (!ReferenceEquals(_app.ActiveDocument, sourcePart))
                        {
                            int ssCount = 0;
                            try { ssCount = doc.SelectSet.Count; } catch { ssCount = 0; }

                            if (ssCount == 0)
                                ForceActivateDocument((Inventor._Document)sourcePart);
                        }
                        var ss = doc.SelectSet;

                        if (hs == null)
                        {
                            try
                            {
                                hs = doc.CreateHighlightSet();
                                hs.Clear();
                                try { hs.Color = _app.TransientObjects.CreateColor(255, 0, 255); } catch { }
                            }
                            catch { hs = null; }
                        }

                        int n;
                        try { n = ss.Count; } catch { n = 0; }
                        if (n <= 0) continue;

                        bool ctrlDown = (Control.ModifierKeys & Keys.Control) == Keys.Control;

                        for (int i = 1; i <= n; i++)
                        {
                            object raw;
                            try { raw = ss[i]; } catch { continue; }
                            if (raw == null) continue;

                            if (!TryResolveSelectableEntity(raw, out object entityObj))
                                continue;

                            string id = CustomDeriveCore.GetEntityId(doc, entityObj);

                            if (ctrlDown && selectedEntityIds.Contains(id))
                            {
                                selectedEntityIds.Remove(id);
                                idToObj.Remove(id);
                                highlightDirty = true;
                            }
                            else
                            {
                                if (selectedEntityIds.Add(id))
                                {
                                    idToObj[id] = entityObj;
                                    highlightDirty = true;
                                }
                            }
                        }

                        if (highlightDirty)
                        {
                            try { _app.StatusBarText = $"Selected: {selectedEntityIds.Count} (Ctrl-click to de-select)"; } catch { }

                            if (mtCountLabel != null)
                            {
                                try
                                {
                                    try { mtCountLabel.DisplayName = $"Selected: {selectedEntityIds.Count}"; }
                                    catch
                                    {
                                        try { mtCountLabel.Text = $"Selected: {selectedEntityIds.Count}"; } catch { }
                                    }
                                }
                                catch { }
                            }

                            try { hs?.Clear(); } catch { }
                            if (hs != null)
                            {
                                foreach (var obj in idToObj.Values)
                                {
                                    try { hs.AddItem(obj); } catch { }
                                }
                            }

                            highlightDirty = false;
                        }

                        // Clear transient selection so next click behaves as a toggle event
                        try { ss.Clear(); } catch { }
                    }
                    catch
                    {
                        // ignore transient COM issues
                    }
                }
                if (!done) cancelled = true;
            }
            finally
            {
                // Cleanup no matter what
                try { hs?.Clear(); } catch { }
                try { doc.SelectSet?.Clear(); } catch { }

                try
                {
                    if (mt != null)
                    {
                        try { mt.Visible = false; } catch { }
                        try { mt.Delete(); } catch { }
                    }
                }
                catch { }

                _mtFinishRequested = false;
                _mtCancelRequested = false;

                // Safety: ensure we’re not stuck in an active command/selection mode
                try { _app.CommandManager.StopActiveCommand(); } catch { }
                try { _app.ActiveView.Update(); } catch { }
            }

            return cancelled ? new HashSet<string>(StringComparer.Ordinal) : selectedEntityIds;
        }




        private static bool TryResolveSelectableEntity(object selected, out object entityObj)
        {
            entityObj = null!;

            if (selected == null) return false;

            if (selected is PlanarSketch || selected is Sketch3D || selected is WorkPlane)
            {
                entityObj = selected;
                return true;
            }

            // Proxy -> native
            try
            {
                dynamic d = selected;
                try
                {
                    object native = d.NativeObject;
                    if (native != null && !ReferenceEquals(native, selected))
                        return TryResolveSelectableEntity(native, out entityObj);
                }
                catch { }
            }
            catch { }

            // ContainingSketch / Parent walk
            try
            {
                dynamic o = selected;

                try
                {
                    object cs = o.ContainingSketch;
                    if (cs is PlanarSketch || cs is Sketch3D)
                    {
                        entityObj = cs;
                        return true;
                    }
                }
                catch { }

                object cur = selected;
                for (int depth = 0; depth < 4; depth++)
                {
                    try
                    {
                        dynamic cd = cur;
                        object p = cd.Parent;

                        if (p is PlanarSketch || p is Sketch3D || p is WorkPlane)
                        {
                            entityObj = p;
                            return true;
                        }

                        cur = p;
                    }
                    catch { break; }
                }
            }
            catch { }

            // Geometry created by feature -> feature sketch
            try
            {
                dynamic d = selected;
                try
                {
                    object f = d.CreatedByFeature;
                    if (f != null)
                    {
                        try
                        {
                            dynamic df = f;
                            object sk = df.Sketch;
                            if (sk is PlanarSketch || sk is Sketch3D)
                            {
                                entityObj = sk;
                                return true;
                            }
                        }
                        catch { }

                        try
                        {
                            dynamic df = f;
                            dynamic prof = df.Profile;
                            if (prof != null)
                            {
                                object parent = prof.Parent;
                                if (parent is PlanarSketch)
                                {
                                    entityObj = parent;
                                    return true;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
            catch { }

            return false;
        }

        

        private void RefreshDerivedInActivePartList()
        {
            if (_derivedInActiveList == null) return;

            if (_app.ActiveDocument is not PartDocument partDoc)
            {
                _derivedInActiveList.Items.Clear();
                _derivedInActiveList.Items.Add("(None)");
                UpdateEditDeriveEnabled();
                return;
            }

            var items = new List<DerivedSourceItem>();

            try
            {
                dynamic dpcs = partDoc.ComponentDefinition.ReferenceComponents.DerivedPartComponents;

                foreach (object obj in dpcs)
                {
                    try
                    {
                        dynamic dpc = obj;

                        string? full = null;
                        try { full = (string?)dpc.ReferencedDocumentDescriptor?.FullDocumentName; } catch { }

                        if (string.IsNullOrWhiteSpace(full))
                            continue;

                        string pn = ReadPartNumber(full);
                        string display = !string.IsNullOrWhiteSpace(pn)
                            ? pn.Trim()
                            : IOPath.GetFileNameWithoutExtension(full);

                        // Avoid duplicate display names (same source derived multiple times)
                        if (items.Any(i => string.Equals(i.SourceFullPath, full, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        items.Add(new DerivedSourceItem
                        {
                            DisplayName = display,
                            SourceFullPath = full!
                        });
                    }
                    catch { }
                }
            }
            catch { }

            _derivedInActiveList.BeginUpdate();
            try
            {
                _derivedInActiveList.Items.Clear();

                if (items.Count == 0)
                    _derivedInActiveList.Items.Add("(None)");
                else
                    _derivedInActiveList.Items.AddRange(items.ToArray());
            }
            finally
            {
                _derivedInActiveList.EndUpdate();
            }

            UpdateEditDeriveEnabled();
        }

        private void UpdateEditDeriveEnabled()
        {
            if (_btnEditDerive == null) return;
            _btnEditDerive.Enabled = _derivedInActiveList?.SelectedItem is DerivedSourceItem;
        }

        private string ReadPartNumber(string fullPath)
        {
            Document? doc = null;
            bool openedHere = false;

            try
            {
                foreach (Document d in _app.Documents)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(d.FullFileName) &&
                            string.Equals(d.FullFileName, fullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            doc = d;
                            break;
                        }
                    }
                    catch { }
                }

                if (doc == null)
                {
                    doc = _app.Documents.Open(fullPath, false);
                    openedHere = true;
                }

                try
                {
                    var ps = doc.PropertySets["Design Tracking Properties"];
                    var pn = ps["Part Number"].Value;
                    if (pn != null) return pn.ToString() ?? "";
                }
                catch { }

                return "";
            }
            finally
            {
                if (openedHere && doc != null)
                {
                    try { doc.Close(false); } catch { }
                }
            }
        }

        private Inventor._Document? FindOpenDocumentByPath(string fullPath)
        {
            foreach (Document d in _app.Documents)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(d.FullFileName) &&
                        string.Equals(d.FullFileName, fullPath, StringComparison.OrdinalIgnoreCase))
                        return (Inventor._Document)d;
                }
                catch { }
            }
            return null;
        }


        // --- JSON persistence ---

        private string EnsureProjectEfficiencyJsonExists(string ipjFullPath, string workspaceRoot)
        {
            string jsonPath = GetEfficiencyJsonPath(ipjFullPath, workspaceRoot);

            if (IOFile.Exists(jsonPath))
            {
                try { _ = LoadEfficiencyData(jsonPath); return jsonPath; }
                catch { /* recreate */ }
            }

            string projectName = IOPath.GetFileNameWithoutExtension(ipjFullPath);
            if (string.IsNullOrWhiteSpace(projectName))
                projectName = "Project";

            var model = new EfficiencyDataFile
            {
                SchemaVersion = 1,
                ProjectName = projectName,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                DeriveParts = new List<DerivePartEntry>()
            };

            IODir.CreateDirectory(workspaceRoot);
            SaveEfficiencyDataAtomic(jsonPath, model);
            return jsonPath;
        }

        private static string GetEfficiencyJsonPath(string ipjFullPath, string workspaceRoot)
        {
            string projectName = IOPath.GetFileNameWithoutExtension(ipjFullPath);
            if (string.IsNullOrWhiteSpace(projectName))
                projectName = "Project";

            string fileName = SanitizeFileName(projectName) + " Efficiency Data.json";
            return IOPath.Combine(workspaceRoot, fileName);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in IOPath.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static EfficiencyDataFile LoadEfficiencyData(string jsonPath)
        {
            var json = IOFile.ReadAllText(jsonPath);
            var model = JsonSerializer.Deserialize<EfficiencyDataFile>(json, JsonOpts);
            if (model == null) throw new InvalidOperationException("Failed to load efficiency data JSON.");
            model.DeriveParts ??= new List<DerivePartEntry>();
            return model;
        }

        private static void SaveEfficiencyDataAtomic(string jsonPath, EfficiencyDataFile model)
        {
            var tmp = jsonPath + ".tmp";
            IOFile.WriteAllText(tmp, JsonSerializer.Serialize(model, JsonOpts));
            IOFile.Move(tmp, jsonPath, overwrite: true);
        }

        private sealed class EfficiencyDataFile
        {
            public int SchemaVersion { get; set; } = 1;
            public string ProjectName { get; set; } = "";
            public DateTime CreatedUtc { get; set; }
            public DateTime UpdatedUtc { get; set; }

            public List<DerivePartEntry>? DeriveParts { get; set; }
        }

        private sealed class DerivePartEntry
        {
            public string PartNumber { get; set; } = "";
            public string FilePath { get; set; } = "";

            public override string ToString()
            {
                return string.IsNullOrWhiteSpace(PartNumber) ? "(no part number)" : PartNumber;
            }
        }

        /// <summary>Display item for the "Derived in Active Part" list, carrying the source file path.</summary>
        private sealed class DerivedSourceItem
        {
            public string DisplayName { get; set; } = "";
            public string SourceFullPath { get; set; } = "";

            public override string ToString() => DisplayName;
        }
       private void MtFinish_OnExecute(NameValueMap Context)
        {
            _mtFinishRequested = true;
        }

        private void MtCancel_OnExecute(NameValueMap Context)
        {
            _mtCancelRequested = true;
        }

        private void MtFinish_OnClick()
        {
            _mtFinishRequested = true;
        }

        private void MtCancel_OnClick()
        {
            _mtCancelRequested = true;
        }

        private static bool TryHookComEvent(object comObj, string eventName, object handlerTarget, params string[] handlerMethodNames)
        {
            if (comObj == null) return false;

            try
            {
                // COM interop often exposes events as non-public, and names can vary in casing.
                var events = comObj.GetType().GetEvents(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var ei = events.FirstOrDefault(e => string.Equals(e.Name, eventName, StringComparison.OrdinalIgnoreCase));
                if (ei == null) return false;

                foreach (var methodName in handlerMethodNames)
                {
                    var mi = handlerTarget.GetType().GetMethod(
                        methodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (mi == null) continue;

                    var del = Delegate.CreateDelegate(ei.EventHandlerType!, handlerTarget, mi, throwOnBindFailure: false);
                    if (del == null) continue;

                    ei.AddEventHandler(comObj, del);
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static bool IsEnterDown() => (Control.ModifierKeys & Keys.Alt) == Keys.None && (GetKeyState(Keys.Enter) < 0);
        private static bool IsEscDown() => (GetKeyState(Keys.Escape) < 0);

        // Win32 key state (keeps this independent of Inventor focus quirks)
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private static short GetKeyState(Keys key) => GetKeyState((int)key);

        private bool ForceActivateDocument(Inventor._Document doc, int retries = 30, int sleepMs = 20)
        {
            if (doc == null) return false;

            // Stop any command that might be holding the UI/selection state
            try { _app.CommandManager.StopActiveCommand(); } catch { }

            for (int i = 0; i < retries; i++)
            {
                try
                {
                    // If the doc is open but not visible, try to show it (some interop builds expose Visible)
                    try
                    {
                        dynamic d = doc;
                        try { d.Visible = true; } catch { }
                    }
                    catch { }

                    doc.Activate();

                    try { System.Windows.Forms.Application.DoEvents(); } catch { }
                    try { _app.ActiveView.Update(); } catch { }

                    if (ReferenceEquals(_app.ActiveDocument, doc))
                        return true;
                }
                catch { }

                System.Threading.Thread.Sleep(sleepMs);
            }

            return ReferenceEquals(_app.ActiveDocument, doc);
        }

        private Inventor._Document? ForceReopenVisiblePart(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !IOFile.Exists(fullPath))
                return null;

            // Close if already open
            try
            {
                foreach (Document d in _app.Documents)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(d.FullFileName) &&
                            string.Equals(d.FullFileName, fullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            try { d.Close(false); } catch { }
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Open visible
            try
            {
                var opened = _app.Documents.Open(fullPath, true);
                return (Inventor._Document)opened;
            }
            catch
            {
                return null;
            }
        }

        private void MtFinish_OnAny(object _)
        {
            _mtFinishRequested = true;
        }

        private void MtCancel_OnAny(object _)
        {
            _mtCancelRequested = true;
        }
        public void Dispose()
        {
            Stop();
        }

        // ============================================================
        // Post-derive sketch styling (no polling)
        // ============================================================

        private bool IsSketchEditActive()
        {
            try
            {
                var editObj = _app.ActiveEditObject;
                return editObj is Sketch || editObj is Sketch3D || editObj is PlanarSketch;
            }
            catch
            {
                return false;
            }
        }

        private static HashSet<long> SnapshotSketchIUnknowns(PartDocument partDoc)
        {
            var set = new HashSet<long>();
            var cd = partDoc.ComponentDefinition;

            void addObj(object o)
            {
                if (o == null) return;

                IntPtr p = IntPtr.Zero;
                try
                {
                    p = Marshal.GetIUnknownForObject(o);
                    set.Add(p.ToInt64());
                }
                finally
                {
                    if (p != IntPtr.Zero) Marshal.Release(p);
                }
            }

            foreach (object s in cd.Sketches) addObj(s);
            foreach (object s in cd.Sketches3D) addObj(s);

            return set;
        }

        private int ApplyInactiveToNewSketches(PartDocument partDoc, HashSet<long> before)
        {
            var styler = new SketchStyler(_app);
            var cd = partDoc.ComponentDefinition;

            bool isNew(object o)
            {
                IntPtr p = IntPtr.Zero;
                try
                {
                    p = Marshal.GetIUnknownForObject(o);
                    return !before.Contains(p.ToInt64());
                }
                finally
                {
                    if (p != IntPtr.Zero) Marshal.Release(p);
                }
            }

            int styled = 0;

            foreach (object s in cd.Sketches)
            {
                if (s is Sketch sk && isNew(sk))
                {
                    styler.ApplyInactive(sk);
                    styled++;
                }
            }

            foreach (object s in cd.Sketches3D)
            {
                if (s is Sketch3D sk3 && isNew(sk3))
                {
                    styler.ApplyInactive(sk3);
                    styled++;
                }
            }

            return styled;
        }




    }
}
