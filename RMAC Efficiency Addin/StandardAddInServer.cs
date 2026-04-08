using Inventor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using RMAC_Efficiency_Addin.Infrastructure;
using RMAC_Efficiency_Addin.UI;
using RMAC_Efficiency_Addin.DrawingDock;
using RMAC_Efficiency_Addin.Settings;
using RMAC_Efficiency_Addin.Updates;
using RMAC_Efficiency_Addin.UI.Dialogs;
using LicenseMgr = RMAC_Efficiency_Addin.Licensing.LicenseManager;
using RMAC_Efficiency_Addin.Licensing;


namespace RMAC_Efficiency_Addin
{
    [Guid("9889ec7f-aa24-4569-bd57-c5b0038466f5")]
    [ProgId("RMAC_Efficiency_Addin.StandardAddInServer")]
    [ComVisible(true)]
    public class StandardAddInServer : Inventor.ApplicationAddInServer, IRmacCommandCallbacks
    {
        private Inventor.Application? m_inventorApplication;

        private RmacUi? _ui;
        private ControllerManager? _controllerManager;
        // Sheet metal tool button
        // Sketch visibility buttons
        // Sketch dimension visibility tools

        // Sketch color override controls
        private bool _suppressDimModeSelect = false;

        private RecentPartsDockController? _recentPartsDock;
        private DrawingDockController? _drawingDock;

        private string? _clientId;
        private Inventor.ApplicationEvents? _appEvents;
        private string? _pendingUpdatePath;

        // IMPORTANT: store delegate instances so unsubscribe works reliably
        private ApplicationEventsSink_OnActivateDocumentEventHandler? _onActivateDocHandler;


        // --- Visible debug logging (iLogic Log) ---
        private bool _debugUiLog = false;          // reads from AddinSettings.Current.Debug at runtime
        private dynamic? _ilogicLogControl = null; // cached iLogic LogControl
        private bool _ilogicLogInitTried = false;  // avoid retry spam


        // Sketch styling / dim policy helpers
        private SketchStyler? _styler;
        private SketchDimensionPolicy? _dims;

        // Sketch Show Format auto-toggle (enter/exit sketch)
        private SketchShowFormatController? _sketchShowFormat;

        // controller for the Layout Pins / Derive feature
        private LayoutsController? _layoutsController;




        public void Activate(Inventor.ApplicationAddInSite addInSiteObject, bool firstTime)
        {
            m_inventorApplication = addInSiteObject.Application;
            _clientId = "{" + typeof(StandardAddInServer).GUID.ToString() + "}";

            // Load global settings (Dim mode, Debug, etc.)
            AddinSettings.Load();

            // --- License gate (DISABLED for development) ---
            // var licResult = LicenseMgr.CheckLicense();
            //
            // if (licResult.Status == LicenseStatus.NeedsActivation)
            // {
            //     using var dlg = new LicenseActivationDialog();
            //     if (dlg.ShowDialog() != DialogResult.OK) return;
            //     licResult = LicenseMgr.CheckLicense();
            // }
            //
            // if (licResult.Status == LicenseStatus.Expired)
            // {
            //     using var dlg = new LicenseActivationDialog(expiredMessage: licResult.Message);
            //     if (dlg.ShowDialog() != DialogResult.OK) return;
            //     licResult = LicenseMgr.CheckLicense();
            // }
            //
            // // If still not licensed after dialog, bail out
            // if (licResult.Status != LicenseStatus.Licensed)
            //     return;

            UiLog("RMAC addin activated (debug UI log enabled).", "Info");

            _styler = new SketchStyler(m_inventorApplication);
            _dims = new SketchDimensionPolicy();

            // Build ribbon/UI first
            _ui = RmacUi.Create(m_inventorApplication, _clientId!, this);

            // Apply persisted Dim Mode selection without firing OnSelect
            ComSafe.Try("Init DimMode combo", () =>
            {
                var cmb = _ui.Commands.CmbDimMode;
                int sel = 1 + (int)AddinSettings.Current.DimMode; // enum 0..2, listindex 1..3
                if (sel < 1) sel = 1;
                if (sel > 3) sel = 3;
                _suppressDimModeSelect = true;
                cmb.ListIndex = sel;
                _suppressDimModeSelect = false;
            });

            // Apply persisted sketch dimension mode to the current part document (if any)
            try
            {
                if (m_inventorApplication.ActiveDocument is Inventor.PartDocument pd)
                {
                    RunFast("RMAC Apply Dimension Mode", () =>
                    {
                        DimModeCore.ApplyToPart(pd, AddinSettings.Current.DimMode, _dims!);
                    });
                }
            }
            catch { }

            // Hook application events (store delegates!)
            _appEvents = m_inventorApplication.ApplicationEvents;

            _onActivateDocHandler = AppEvents_OnActivateDocument;

            _appEvents.OnActivateDocument += _onActivateDocHandler;


            // Start sketch Show Format controller (event-driven, no OnIdle polling)
            _sketchShowFormat = new SketchShowFormatController(
                m_inventorApplication,
                _dims!,
                _styler!,
                (m, lvl) => UiLog(m, lvl));
            _sketchShowFormat.Start();



            // Start controllers after events are wired
            _controllerManager = new ControllerManager();

            if (_sketchShowFormat != null)
                _controllerManager.Add(_sketchShowFormat);

            // Start controllers after events are wired
            _layoutsController = new LayoutsController(m_inventorApplication, _clientId);
            _recentPartsDock = new RecentPartsDockController(m_inventorApplication, _clientId);
            _drawingDock = new DrawingDockController(m_inventorApplication, _clientId);

            _controllerManager.Add(_layoutsController);
            _controllerManager.Add(_recentPartsDock);
            _controllerManager.Add(_drawingDock);

            _controllerManager.StartAll();

            // Ensure initial visibility is correct
            ComSafe.Try("RecentParts OnActivateDocument", () => _recentPartsDock.OnActivateDocument());

            // First-run setup wizard
            if (!AddinSettings.Current.WizardCompleted)
            {
                try
                {
                    using var wizard = new UI.Wizard.SetupWizardForm();
                    wizard.ShowDialog();
                }
                catch { }
            }

#if TEST_HARNESS
            // Self-test harness trigger. Only compiled in the Debug-Test build configuration.
            // Fires when FABFLOW_SELFTEST=1 is set in the environment (see Tests\RunOneVersion.bat).
            // Deferred via Task.Delay so Inventor's UI has time to stabilise before tests run.
            if (System.Environment.GetEnvironmentVariable("FABFLOW_SELFTEST") == "1")
            {
                var harnessSyncCtx = SynchronizationContext.Current;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(3000);
                        harnessSyncCtx?.Post(_ =>
                        {
                            try { TestHarness.SelfTestRunner.Run(m_inventorApplication!); }
                            catch { /* harness must never crash the add-in */ }
                        }, null);
                    }
                    catch { }
                });
            }
#endif

            // Background update check (fire-and-forget, never crashes the addin)
            var syncCtx = SynchronizationContext.Current;
            _ = Task.Run(async () =>
            {
                try
                {
                    var url = AddinSettings.Current.UpdateUrl;
                    if (string.IsNullOrWhiteSpace(url)) return;

                    if ((DateTime.UtcNow - AddinSettings.Current.LastUpdateCheck).TotalHours < 24)
                        return;

                    var update = await UpdateChecker.CheckForUpdateAsync(url);

                    AddinSettings.Current.LastUpdateCheck = DateTime.UtcNow;
                    AddinSettings.SaveWithResult();

                    if (update == null) return;
                    if (update.Version == AddinSettings.Current.SkippedVersion) return;

                    var localPath = await UpdateChecker.DownloadInstallerAsync(update.DownloadUrl);
                    if (localPath == null) return;

                    syncCtx?.Post(_ =>
                    {
                        try
                        {
                            using var dlg = new UpdateNotificationForm(update);
                            var result = dlg.ShowDialog();

                            if (result == DialogResult.OK)
                            {
                                _pendingUpdatePath = localPath;
                            }
                            else if (result == DialogResult.Ignore)
                            {
                                AddinSettings.Current.SkippedVersion = update.Version;
                                AddinSettings.SaveWithResult();
                            }
                            // Cancel = Not Now → do nothing
                        }
                        catch { }
                    }, null);
                }
                catch { /* updates are best-effort */ }
            });
        }


        public void Deactivate()
        {
            // 1. Unhook events first
            try
            {
                if (_appEvents != null)
                {
                    if (_onActivateDocHandler != null) _appEvents.OnActivateDocument -= _onActivateDocHandler;
                }
            }
            catch { }
            _onActivateDocHandler = null;
            _appEvents = null;

            // 2. Save settings
            try { AddinSettings.Save(); } catch { }

            // 2b. Launch pending update installer (runs after Inventor exits)
            try
            {
                if (!string.IsNullOrEmpty(_pendingUpdatePath) && System.IO.File.Exists(_pendingUpdatePath))
                {
                    UpdateChecker.LaunchInstallerOnExit(_pendingUpdatePath!);
                    _pendingUpdatePath = null;
                }
            }
            catch { }

            // 3. Dispose/stop UI controllers
            try { _controllerManager?.Dispose(); } catch { }
            _controllerManager = null;
            _layoutsController = null;
            _recentPartsDock = null;
            _drawingDock = null;
            _sketchShowFormat = null;

            // 4. Dispose UI
            try { _ui?.Dispose(); } catch { }
            _ui = null;

            // 5. Null out remaining references
            _dims = null;
            _styler = null;
            _ilogicLogControl = null;
            _ilogicLogInitTried = false;

            m_inventorApplication = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public void ExecuteCommand(int commandID) { /* obsolete */ }
        public object? Automation => null;



        private static bool PanelHasControl(RibbonPanel panel, string internalName)
        {
            foreach (CommandControl c in panel.CommandControls)
                if (c.InternalName == internalName) return true;
            return false;
        }

        private void BtnCheckComments_OnExecute(NameValueMap Context)
        {
            try
            {
                CommentsChecker.Run(m_inventorApplication!);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Add-in Error");
            }
        }

        private void BtnEditComments_OnExecute(NameValueMap Context)
        {
            try
            {
                using var f = new RMAC_Efficiency_Addin.UI.RmacSettingsForm(m_inventorApplication);
                f.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "RMAC Settings - Error");
            }
        }

        private void BtnFindSketchCmds_OnExecute(NameValueMap Context)
        {
            FindSketchPropertyCommands();
        }

        private void BtnSmThicknessFromLine_OnExecute(NameValueMap Context)
        {
            try
            {
                var app = m_inventorApplication ?? throw new InvalidOperationException("Inventor application not set.");

                // Calls your helper class (SheetMetalThicknessTool.cs)
                SheetMetalThicknessTools.SetSheetMetalThicknessFromSelectedLine(app);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "SM Thickness from Line - Error");
            }
        }

        private void BtnHideAllSketches_OnExecute(NameValueMap Context)
        {
            try
            {
                var app = m_inventorApplication ?? throw new InvalidOperationException("Inventor application not set.");

                if (app.ActiveDocument is not Inventor.PartDocument partDoc)
                {
                    MessageBox.Show("Open a Part document to run this command.");
                    return;
                }

                object? editObj = null;
                try { editObj = app.ActiveEditObject; } catch { }

                bool isSketchEditActive =
                    editObj != null && (Is2DSketchEditObject(editObj) || Is3DSketchEditObject(editObj));

                if (isSketchEditActive)
                {
                    MessageBox.Show("Exit sketch edit mode before running 'Hide All Sketches'.");
                    return;
                }

                // If our internal flag ever got stuck true, reset it here (safe).

                RunFast("RMAC Hide All Sketches", () =>
                {
                    SketchVisibilityCore.SetAll(partDoc, makeVisible: false);
                });

                // No completion message (per request).
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Hide All Sketches - Error");
            }
        }

        private void BtnShowAllSketches_OnExecute(NameValueMap Context)
        {
            try
            {
                var app = m_inventorApplication ?? throw new InvalidOperationException("Inventor application not set.");

                if (app.ActiveDocument is not Inventor.PartDocument partDoc)
                {
                    MessageBox.Show("Open a Part document to run this command.");
                    return;
                }

                object? editObj = null;
                try { editObj = app.ActiveEditObject; } catch { }

                bool isSketchEditActive =
                    editObj != null && (Is2DSketchEditObject(editObj) || Is3DSketchEditObject(editObj));

                if (isSketchEditActive)
                {
                    MessageBox.Show("Exit sketch edit mode before running 'Show All Sketches'.");
                    return;
                }

                // If our internal flag ever got stuck true, reset it here (safe).

                RunFast("RMAC Show All Sketches", () =>
                {
                    SketchVisibilityCore.SetAll(partDoc, makeVisible: true);
                });

                // No completion message (per request).
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Show All Sketches - Error");
            }
        }


        // =========================
        // Sketch Dimension Commands
        // =========================

        private void CmbDimMode_OnSelect(NameValueMap Context)
        {
            try
            {
                if (_suppressDimModeSelect) return;
                if (_ui!.Commands.CmbDimMode == null) return;

                var mode = ListIndexToDimMode(_ui!.Commands.CmbDimMode.ListIndex);
                SetDimMode(mode, applyNow: true, updateCombo: false);
            }
            catch
            {
                // silent
            }
        }


        private void BtnShowSketchDims_OnExecute(NameValueMap Context)
        {
            try
            {
                var app = m_inventorApplication ?? throw new InvalidOperationException("Inventor application not set.");

                if (app.ActiveDocument is not Inventor.PartDocument partDoc)
                {
                    MessageBox.Show("Open a Part document to run this command.");
                    return;
                }

                if (IsSketchEditActive(app))
                {
                    MessageBox.Show("Exit sketch edit mode before running 'Show Sketch Dimensions'.");
                    return;
                }

                var sk = PickSketch(partDoc);
                if (sk == null) return;

                RunFast("RMAC Pin Sketch Dimensions", () =>
                {
                    SketchDimCore.ShowAndPin(sk, _dims!);
                });
            }
            catch (COMException)
            {
                // user cancelled pick
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Show Sketch Dimensions - Error");
            }
        }

        private void BtnHideSketchDims_OnExecute(NameValueMap Context)
        {
            try
            {
                var app = m_inventorApplication ?? throw new InvalidOperationException("Inventor application not set.");

                if (app.ActiveDocument is not Inventor.PartDocument partDoc)
                {
                    MessageBox.Show("Open a Part document to run this command.");
                    return;
                }

                if (IsSketchEditActive(app))
                {
                    MessageBox.Show("Exit sketch edit mode before running 'Hide Sketch Dimensions'.");
                    return;
                }

                var sk = PickSketch(partDoc);
                if (sk == null) return;

                RunFast("RMAC Unpin Sketch Dimensions", () =>
                {
                    SketchDimCore.UnpinAndApply(sk, AddinSettings.Current.DimMode, _dims!);
                });
            }
            catch (COMException)
            {
                // user cancelled pick
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Hide Sketch Dimensions - Error");
            }
        }

        private void BtnClearSketchColorOverrides_OnExecute(NameValueMap Context)
        {
            try
            {
                var app = m_inventorApplication ?? throw new InvalidOperationException("Inventor application not set.");

                if (app.ActiveDocument is not Inventor.PartDocument partDoc)
                {
                    MessageBox.Show("Open a Part document to run this command.");
                    return;
                }

                if (IsSketchEditActive(app))
                {
                    MessageBox.Show("Exit sketch edit mode before running 'Clear Sketch Color Overrides'.");
                    return;
                }

                var sketches = GetSelectedSketches(partDoc);

                if (sketches.Count == 0)
                {
                    var sk = PickSketch(partDoc);
                    if (sk != null) sketches.Add(sk);
                }

                if (sketches.Count == 0) return;

                RunFast("RMAC Enable Show Format", () =>
                {
                    // This command only executes reliably while a sketch is active.
                    // Use the first selected/picked sketch as our entry point.
                    SketchColorCore.ClearOverrides(app, sketches[0], _sketchShowFormat);
                });

                try { app.ActiveView.Update(); } catch { }
            }
            catch (COMException)
            {
                // user cancelled pick
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Clear Sketch Color Overrides - Error");
            }
        }

        private void BtnReleaseSketchColorHold_OnExecute(NameValueMap Context)
        {
            try
            {
                var app = m_inventorApplication ?? throw new InvalidOperationException("Inventor application not set.");

                if (app.ActiveDocument is not Inventor.PartDocument partDoc)
                {
                    MessageBox.Show("Open a Part document to run this command.");
                    return;
                }

                if (IsSketchEditActive(app))
                {
                    MessageBox.Show("Exit sketch edit mode before running 'Deselect Sketch Color Hold'.");
                    return;
                }

                var sketches = GetSelectedSketches(partDoc);

                if (sketches.Count == 0)
                {
                    var sk = PickSketch(partDoc);
                    if (sk != null) sketches.Add(sk);
                }

                if (sketches.Count == 0) return;

                RunFast("RMAC Disable Show Format", () =>
                {
                    SketchColorCore.ReleaseHold(app, sketches, _sketchShowFormat, _styler!);
                });

                try { app.ActiveView.Update(); } catch { }
            }
            catch (COMException)
            {
                // user cancelled pick
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Deselect Sketch Color Hold - Error");
            }
        }

        private List<object> GetSelectedSketches(Inventor.PartDocument partDoc)
        {
            var res = new List<object>();
            try
            {
                var byKey = new Dictionary<string, object>(StringComparer.Ordinal);

                foreach (object o in partDoc.SelectSet)
                {
                    var sk = ResolveSketch(o, partDoc);
                    if (sk == null) continue;

                    string nm = SketchHelpers.TryGetName(sk) ?? sk.GetType().Name;
                    string key = (sk is Inventor.Sketch3D ? "3D:" : "2D:") + nm;

                    if (!byKey.ContainsKey(key))
                        byKey[key] = sk;
                }

                res.AddRange(byKey.Values);
            }
            catch { }

            return res;
        }


        private void SetDimMode(DimPolicyMode mode, bool applyNow, bool updateCombo)
        {
            try
            {
                AddinSettings.Current.DimMode = mode;
                AddinSettings.Save();

                if (updateCombo && _ui!.Commands.CmbDimMode != null)
                {
                    try
                    {
                        _suppressDimModeSelect = true;
                        _ui!.Commands.CmbDimMode.ListIndex = 1 + (int)mode;
                    }
                    finally
                    {
                        _suppressDimModeSelect = false;
                    }
                }

                if (applyNow && m_inventorApplication?.ActiveDocument is Inventor.PartDocument partDoc)
                {
                    RunFast("RMAC Set Dimension Mode", () =>
                    {
                        DimModeCore.ApplyToPart(partDoc, mode, _dims!);
                    });
                }
            }
            catch
            {
                _suppressDimModeSelect = false;
            }
        }

        private static DimPolicyMode ListIndexToDimMode(int listIndex)
        {
            return listIndex switch
            {
                3 => DimPolicyMode.AllVisible,
                2 => DimPolicyMode.AllHidden,
                _ => DimPolicyMode.NamedOnly
            };
        }

        private void ApplyDimExitPolicy(object sketchObj)
        {
            // Pinned sketches are local overrides: never auto-hide on exit.
            if (SketchDimPin.IsPinned(sketchObj))
                return;

            switch (AddinSettings.Current.DimMode)
            {
                case DimPolicyMode.AllHidden:
                    _dims!.ApplyHideAll(sketchObj);
                    break;

                case DimPolicyMode.NamedOnly:
                    _dims!.ApplyNamedOnly(sketchObj);
                    break;

                case DimPolicyMode.AllVisible:
                default:
                    // Do nothing: leave current visibility as-is.
                    break;
            }
        }

        private static bool IsSketchEditActive(Inventor.Application app)
        {
            object? editObj = null;
            try { editObj = app.ActiveEditObject; } catch { }

            if (editObj == null) return false;

            // 1) Sometimes Inventor returns the actual sketch object
            if (editObj is Inventor.Sketch || editObj is Inventor.Sketch3D)
                return true;

            // 2) Sometimes you get a wrapper with a NativeObject that is the sketch
            try
            {
                dynamic d = editObj;
                object native = d.NativeObject;
                if (native is Inventor.Sketch || native is Inventor.Sketch3D)
                    return true;
            }
            catch { }

            // 3) Fallback: your existing heuristic for edit wrappers
            return Is2DSketchEditObject(editObj) || Is3DSketchEditObject(editObj);
        }


        private object? PickSketch(Inventor.PartDocument partDoc)
        {
            // 1) Use preselection if present (browser OR canvas preselect).
            try
            {
                foreach (object o in partDoc.SelectSet)
                {
                    var sk = ResolveSketch(o, partDoc);
                    if (sk != null) return sk;
                }
            }
            catch { }

            var app = m_inventorApplication ?? throw new InvalidOperationException("Inventor application not set.");

            // 2) Best UX: pick sketch GEOMETRY in the canvas (line/arc/point/etc.) and resolve to owning sketch.
            // This mirrors the pick something that belongs to the sketch behaviour you already use elsewhere.
            try
            {
                object picked2d = app.CommandManager.Pick(
                    SelectionFilterEnum.kSketchCurveFilter,
                    "Select any 2D sketch geometry in the canvas (or preselect a sketch in the browser)."
                );

                var sk2 = ResolveSketch(picked2d, partDoc);
                if (sk2 != null) return sk2;
            }
            catch (COMException)
            {
                // user cancelled pick
                throw;
            }
            catch
            {
                // ignore and try other filters
            }

            try
            {
                object picked3d = app.CommandManager.Pick(
                    SelectionFilterEnum.kSketch3DCurveFilter,
                    "Select any 3D sketch geometry in the canvas (or preselect a sketch in the browser)."
                );

                var sk3 = ResolveSketch(picked3d, partDoc);
                if (sk3 != null) return sk3;
            }
            catch (COMException)
            {
                // user cancelled pick
                throw;
            }
            catch
            {
                // ignore and try object filters
            }

            // 3) Fallback: pick sketch object (reliable from browser).
            try
            {
                object picked = app.CommandManager.Pick(
                    SelectionFilterEnum.kSketchObjectFilter,
                    "Select a 2D sketch in the browser."
                );

                var sk = ResolveSketch(picked, partDoc);
                if (sk != null) return sk;
            }
            catch (COMException)
            {
                throw;
            }
            catch
            {
                // ignore and try 3D
            }

            try
            {
                object picked = app.CommandManager.Pick(
                    SelectionFilterEnum.kSketch3DObjectFilter,
                    "Select a 3D sketch in the browser."
                );

                return ResolveSketch(picked, partDoc);
            }
            catch (COMException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private object? ResolveSketch(object picked, Inventor.PartDocument partDoc)
        {
            if (picked == null) return null;

            if (picked is Inventor.Sketch || picked is Inventor.Sketch3D)
                return picked;

            // Try common proxy/native patterns.
            try
            {
                dynamic d = picked;
                var native = d.NativeObject;
                if (native is Inventor.Sketch || native is Inventor.Sketch3D) return native;
            }
            catch { }

            // Try Parent.
            try
            {
                dynamic d = picked;
                var parent = d.Parent;
                if (parent is Inventor.Sketch || parent is Inventor.Sketch3D) return parent;
            }
            catch { }

            // Try ContainingSketch (often on sketch entities).
            try
            {
                dynamic d = picked;
                var cs = d.ContainingSketch;
                if (cs is Inventor.Sketch) return cs;
            }
            catch { }

            // Try Sketch property.
            try
            {
                dynamic d = picked;
                var sk = d.Sketch;
                if (sk is Inventor.Sketch || sk is Inventor.Sketch3D) return sk;
            }
            catch { }

            // Fallback: resolve by name to native object.
            try
            {
                var nm = SketchHelpers.TryGetName(picked);
                if (!string.IsNullOrWhiteSpace(nm))
                {
                    var s2 = SketchHelpers.Find2DSketchByName(partDoc, nm);
                    if (s2 != null) return s2;

                    var s3 = SketchHelpers.Find3DSketchByName(partDoc, nm);
                    if (s3 != null) return s3;
                }
            }
            catch { }

            return null;
        }

        private void FindSketchPropertyCommands()
        {
            var hits = SketchCommandInspector.FindMatches(m_inventorApplication!);
            MessageBox.Show(string.Join("\n", hits), "Possible Sketch Commands");
        }

        private void AppEvents_OnActivateDocument(
            Inventor._Document DocumentObject,
            Inventor.EventTimingEnum BeforeOrAfter,
            Inventor.NameValueMap Context,
            out Inventor.HandlingCodeEnum HandlingCode)
        {
            HandlingCode = HandlingCodeEnum.kEventNotHandled;
            if (BeforeOrAfter != EventTimingEnum.kAfter) return;

            // During batch export, suppress dock panel visibility updates.
            // ScreenUpdating restoration fires queued activation events for every
            // document that was opened behind the scenes — we don't want those
            // events toggling dock panels on and off.
            if (Exporting.GlobalExporter.SuppressDockUpdates) return;

            // Refresh pinned layouts / dock context when user changes documents/projects
            try { _layoutsController?.OnActivateDocument(); } catch { }
            try { _recentPartsDock?.OnActivateDocument(); } catch { }
            try { _drawingDock?.OnActivateDocument(); } catch { }
            try { _ui?.RebuildRibbons(); } catch { }

            // Prime the sketch environment so the first user-initiated sketch edit works correctly
            try
            {
                if (DocumentObject is Inventor.PartDocument pd2)
                    _sketchShowFormat?.PrimeIfNeeded(pd2);
            }
            catch { }

            // No per-sketch hold state: nothing to clear when switching documents

            // Apply persisted dimension mode when switching to a part document
            try
            {
                if (m_inventorApplication?.ActiveDocument is Inventor.PartDocument pd)
                {
                    // Never sweep while anything is being edited (sketch, feature, etc.)
                    object? editObj = null;
                    try { editObj = m_inventorApplication.ActiveEditObject; } catch { }

                    if (editObj == null && !IsSketchEditActive(m_inventorApplication))
                    {
                        RunFast("RMAC Apply Dimension Mode", () =>
                        {
                            DimModeCore.ApplyToPart(pd, AddinSettings.Current.DimMode, _dims!);
                        });
                    }
                }

            }
            catch { }
        }

        private void BtnLegacySweep_OnExecute(NameValueMap Context)
        {
            try
            {
                var app = m_inventorApplication ?? throw new InvalidOperationException("Inventor application not set.");

                if (app.ActiveDocument is not Inventor.PartDocument partDoc)
                {
                    MessageBox.Show("Open a Part document to run the legacy sweep.");
                    return;
                }

                if (!AddinSettings.Current.SketchColoringEnabled)
                {
                    try { app.StatusBarText = "FabFlow: Sketch coloring is set to 'No formatting'."; } catch { }
                    return;
                }

                RunFast("RMAC Legacy Sketch Sweep", () =>
                {
                    LegacySweepCore.Run(partDoc, _styler!);
                });

                // No modal popup on success (keep workflow fast / non-blocking)
                try { app.StatusBarText = "FabFlow: Format All Sketches complete."; } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Legacy Sweep - Error");
            }
        }

        private void RunFast(string name, Action action)
        {
            var app = m_inventorApplication!;
            RMAC_Efficiency_Addin.Infrastructure.InventorTransaction.RunFast(app, app.ActiveDocument, name, action);
        }

        private static bool Is2DSketchEditObject(object o)
        {
            if (o == null) return false;

            try
            {
                var t = o.GetType();

                // In actual sketch edit, the edit object supports ExitEdit()
                if (t.GetMethod("ExitEdit") == null)
                    return false;

                // 2D sketch edit objects expose these collections
                if (t.GetProperty("SketchEntities") == null)
                    return false;

                if (t.GetProperty("SketchPoints") == null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool Is3DSketchEditObject(object o)
        {
            if (o == null) return false;

            try
            {
                var t = o.GetType();

                if (t.GetMethod("ExitEdit") == null)
                    return false;

                // 3D sketch edit objects expose SketchEntities3D
                if (t.GetProperty("SketchEntities3D") == null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }



        private static void RemoveControlIfExists(RibbonPanel panel, string internalName)
        {
            try
            {
                // iterate backwards to be safe
                for (int i = panel.CommandControls.Count; i >= 1; i--)
                {
                    CommandControl c;
                    try { c = panel.CommandControls[i]; }
                    catch { continue; }

                    if (c != null && string.Equals(c.InternalName, internalName, StringComparison.OrdinalIgnoreCase))
                    {
                        try { c.Delete(); } catch { /* ignore */ }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private void BtnSettings_OnExecute(NameValueMap Context)
        {
            try
            {
                using var f = new RMAC_Efficiency_Addin.UI.RmacSettingsForm(m_inventorApplication);

                var result = f.ShowDialog();

                if (result != DialogResult.OK) return;

                // Reload to be safe (in case import/export/reset happened)
                AddinSettings.Load();

                // Apply current Dim Mode immediately and sync combo box
                try
                {
                    SetDimMode(AddinSettings.Current.DimMode, applyNow: true, updateCombo: true);
                }
                catch { }

                // Debug flag is live via AddinSettings.Current.Debug already
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "RMAC Settings - Error");
            }
        }

        private void BtnClearOneSketchEntOverride_OnExecute(NameValueMap Context)
        {
            try
            {
                var app = m_inventorApplication ?? throw new InvalidOperationException("Inventor application not set.");

                if (app.ActiveDocument is not Inventor.PartDocument partDoc)
                {
                    MessageBox.Show("Open a Part document to run this command.");
                    return;
                }

                object picked;
                try
                {
                    // Works for SketchLine / arcs / splines etc. inside a 2D sketch.
                    picked = app.CommandManager.Pick(
                        SelectionFilterEnum.kSketchCurveFilter,
                        "Select a single 2D sketch line/curve to clear OverrideColor."
                    );
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // user cancelled
                    return;
                }

                if (picked == null) return;

                string report = SketchColorCore.ClearOneOverride(picked);

                // Copy to clipboard (best effort)
                try { System.Windows.Forms.Clipboard.SetText(report); } catch { }

                // Show (truncate in UI if needed)
                var ui = report;
                if (ui.Length > 12000)
                    ui = ui.Substring(0, 12000) + "\n\n(truncated in message box - full text copied to clipboard if possible)";

                MessageBox.Show(ui, "Clear Line Override Diagnostics");

                // Redraw
                try { partDoc.Update(); } catch { }
                try { app.ActiveView.Update(); } catch { }
                try { app.UserInterfaceManager.DoEvents(); } catch { }
                try { app.ActiveView.Update(); } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Clear Line Override - Error");
            }
        }

        private void BtnFindShowFormatCmds_OnExecute(NameValueMap Context)
        {
            // Reserved for future diagnostic use
        }
        private bool? IsFormattingTogglePressed()
        {
            try
            {
                var app = m_inventorApplication;
                if (app == null) return null;

                var cd = app.CommandManager.ControlDefinitions["AppLinePropertiesToggleCmd"];
                if (cd == null) return null;

                return TryReadBoolProp(cd, "Pressed");
            }
            catch
            {
                return null;
            }
        }

        private static bool? TryReadBoolProp(object comObj, string propName)
        {
            try
            {
                var p = comObj.GetType().GetProperty(propName);
                if (p != null && p.PropertyType == typeof(bool))
                    return (bool)p.GetValue(comObj)!;
            }
            catch { }

            return null;
        }

        private void UiLog(string message, string levelName = "Info")
        {
            string msg = $"[RMAC] {message}";

            // Always show something immediately
            try
            {
                if (m_inventorApplication != null)
                    m_inventorApplication.StatusBarText = msg;
            }
            catch { }

            // Skip iLogic debug logging during export to prevent the iLogic panel stealing focus
            if (Exporting.GlobalExporter.SuppressDockUpdates) return;

            // Master switch — tied to the Debug checkbox in Settings > General
            try { _debugUiLog = AddinSettings.Current.Debug; } catch { }
            if (!_debugUiLog) return;

            try
            {
                EnsureILogicLogControl();
                if (_ilogicLogControl == null) return;

                // LogControl is: Autodesk.iLogic.Core.LogWrapper.LogControlImplementation
                // Method we want: Log(LogLevel, String, Object[])
                object lcObj = (object)_ilogicLogControl;
                Type t = lcObj.GetType();

                // Resolve the enum type for LogLevel via the Level property
                var levelProp = t.GetProperty("Level", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (levelProp == null) return;

                Type enumType = levelProp.PropertyType; // Autodesk.iLogic.Core.LogWrapper.LogLevel

                object desiredLevel;

                // Parse requested level if possible; fallback to Info; fallback to first enum value
                try
                {
                    desiredLevel = Enum.Parse(enumType, levelName, ignoreCase: true);
                }
                catch
                {
                    try { desiredLevel = Enum.Parse(enumType, "Info", ignoreCase: true); }
                    catch { desiredLevel = Enum.GetValues(enumType).GetValue(0)!; }
                }

                // If current Level is "None", bump it up so messages are actually recorded
                try
                {
                    object currentLevel = levelProp.GetValue(lcObj)!;
                    if (string.Equals(currentLevel?.ToString(), "None", StringComparison.OrdinalIgnoreCase))
                        levelProp.SetValue(lcObj, desiredLevel);
                }
                catch { }

                // Invoke Log(LogLevel, string, object[])
                var logMethod = t.GetMethod(
                    "Log",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    binder: null,
                    types: new[] { enumType, typeof(string), typeof(object[]) },
                    modifiers: null
                );

                if (logMethod == null) return;

                logMethod.Invoke(lcObj, new object[] { desiredLevel, msg, Array.Empty<object>() });
            }
            catch
            {
                // Ignore; status bar already updated. (If you want, we can add a one-time file fallback later.)
            }
        }

        private void EnsureILogicLogControl()
        {
            if (_ilogicLogControl != null) return;

            if (_ilogicLogInitTried) return;
            _ilogicLogInitTried = true;

            try
            {
                if (m_inventorApplication == null)
                {
                    _ilogicLogInitTried = false;
                    return;
                }

                Inventor.ApplicationAddIn? addin = null;

                // 1) Try known GUID first
                const string ILOGIC_ADDIN_GUID = "{3BDD8D79-2179-4B11-8A5A-257B1C0263AC}";
                try { addin = m_inventorApplication.ApplicationAddIns.ItemById[ILOGIC_ADDIN_GUID]; }
                catch { addin = null; }

                // 2) Fallback: search by DisplayName (more robust across installs)
                if (addin == null)
                {
                    foreach (Inventor.ApplicationAddIn a in m_inventorApplication.ApplicationAddIns)
                    {
                        try
                        {
                            string dn = a.DisplayName ?? "";
                            if (dn.IndexOf("ilogic", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                addin = a;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (addin == null)
                {
                    try { m_inventorApplication.StatusBarText = "[RMAC] iLogic add-in not found (cannot log)."; } catch { }
                    _ilogicLogInitTried = false;
                    return;
                }

                // Ensure iLogic add-in is activated
                try
                {
                    if (!addin.Activated)
                        addin.Activate();
                }
                catch { }

                // Try a couple of times in case iLogic Automation isn't ready immediately
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        dynamic ilogic = addin.Automation;
                        if (ilogic != null)
                        {
                            dynamic lc = ilogic.LogControl;
                            if (lc != null)
                            {
                                _ilogicLogControl = lc;
                                try { m_inventorApplication.StatusBarText = "[RMAC] iLogic LogControl acquired."; } catch { }
                                return;
                            }
                        }
                    }
                    catch { }

                    // Let Inventor pump messages
                    try { m_inventorApplication.UserInterfaceManager.DoEvents(); } catch { }
                }

                try { m_inventorApplication.StatusBarText = "[RMAC] iLogic add-in found but LogControl not available."; } catch { }
                _ilogicLogInitTried = false;
            }
            catch (Exception ex)
            {
                _ilogicLogControl = null;
                try { if (m_inventorApplication != null) m_inventorApplication.StatusBarText = "[RMAC] EnsureILogicLogControl failed: " + ex.Message; } catch { }
                _ilogicLogInitTried = false;
            }
        }

        // Explicit IRmacCommandCallbacks implementation (keeps handler methods private)
        void IRmacCommandCallbacks.BtnSettings_OnExecute(NameValueMap Context) => BtnSettings_OnExecute(Context);
        void IRmacCommandCallbacks.BtnCheckComments_OnExecute(NameValueMap Context) => BtnCheckComments_OnExecute(Context);
        void IRmacCommandCallbacks.BtnEditComments_OnExecute(NameValueMap Context) => BtnEditComments_OnExecute(Context);
        void IRmacCommandCallbacks.BtnFindSketchCmds_OnExecute(NameValueMap Context) => BtnFindSketchCmds_OnExecute(Context);
        void IRmacCommandCallbacks.BtnLegacySweep_OnExecute(NameValueMap Context) => BtnLegacySweep_OnExecute(Context);
        void IRmacCommandCallbacks.BtnSmThicknessFromLine_OnExecute(NameValueMap Context) => BtnSmThicknessFromLine_OnExecute(Context);
        void IRmacCommandCallbacks.BtnHideAllSketches_OnExecute(NameValueMap Context) => BtnHideAllSketches_OnExecute(Context);
        void IRmacCommandCallbacks.BtnShowAllSketches_OnExecute(NameValueMap Context) => BtnShowAllSketches_OnExecute(Context);
        void IRmacCommandCallbacks.BtnShowSketchDims_OnExecute(NameValueMap Context) => BtnShowSketchDims_OnExecute(Context);
        void IRmacCommandCallbacks.BtnHideSketchDims_OnExecute(NameValueMap Context) => BtnHideSketchDims_OnExecute(Context);
        void IRmacCommandCallbacks.BtnClearOneSketchEntOverride_OnExecute(NameValueMap Context) => BtnClearOneSketchEntOverride_OnExecute(Context);
        void IRmacCommandCallbacks.BtnFindShowFormatCmds_OnExecute(NameValueMap Context) => BtnFindShowFormatCmds_OnExecute(Context);
        void IRmacCommandCallbacks.BtnClearSketchColorOverrides_OnExecute(NameValueMap Context) => BtnClearSketchColorOverrides_OnExecute(Context);
        void IRmacCommandCallbacks.BtnReleaseSketchColorHold_OnExecute(NameValueMap Context) => BtnReleaseSketchColorHold_OnExecute(Context);
        void IRmacCommandCallbacks.CmbDimMode_OnSelect(NameValueMap Context) => CmbDimMode_OnSelect(Context);

        public void BtnHighlight_OnExecute(NameValueMap Context)
        {
            // Alias to your existing "Disable Show Format" / release-hold behaviour
            BtnClearSketchColorOverrides_OnExecute(Context);
        }

        public void BtnClearHighlight_OnExecute(NameValueMap Context)
        {
            // Alias to your existing "Enable Show Format" / clear overrides behaviour

            BtnReleaseSketchColorHold_OnExecute(Context);
        }
    }
}
