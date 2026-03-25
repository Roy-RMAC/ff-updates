using Inventor;
using System;
using RMAC_Efficiency_Addin.Infrastructure;
using WF = System.Windows.Forms;

using InvEnvironment = Inventor.Environment;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Event-driven controller for sketch UX.
    ///
    /// Goal:
    /// - While editing a sketch, force Inventor's "Show Format" (AppLinePropertiesToggleCmd) ON so
    ///   sketches appear with Inventor defaults (i.e., not your muted palette).
    /// - On exit, restore whatever the user had before they entered the sketch:
    ///     - If Show Format was already ON, leave it ON.
    ///     - If Show Format was OFF, turn it OFF on exit.
    ///
    /// Notes:
    /// - No OnIdle polling.
    /// - Exit detection uses the Environment argument (ActiveEnvironment is unreliable during transition).
    /// - Heavy work (palette + dim exit policy) is deferred off Inventor's event stack via BeginInvoke.
    /// </summary>
    internal sealed class SketchShowFormatController : IAddinController
    {
        private readonly Inventor.Application _app;
        private readonly SketchDimensionPolicy _dims;
        private readonly SketchStyler _styler;
        private readonly Action<string, string> _log;

        private Inventor.ApplicationEvents? _appEvents;
        private Inventor.UserInterfaceEvents? _uiEvents;

        private ApplicationEventsSink_OnNewEditObjectEventHandler? _onNewEditObjectHandler;
        private UserInterfaceEventsSink_OnEnvironmentChangeEventHandler? _onEnvironmentChangeHandler;

        // Suppression for programmatic Edit()/ExitEdit() round-trips elsewhere
        private int _suppressCount = 0;
        private bool IsSuppressed => _suppressCount > 0;

        // Current session
        private object? _lastEditedSketch;

        // Safe reacquire after exit (avoid stale COM proxies)
        private string? _lastSketchName;
        private bool _lastSketchWas3D;
        private string? _lastDocKey;

        // Show Format restore policy for this session
        private bool _restoreShowFormatOnExit;

        // Exit staging
        private bool _exitPending;
        private bool _finalizeQueued;
        private bool _pendingRestoreShowFormatOnExit;
        private bool _toggleOffOk;
        private string? _pendingSketchName;
        private bool _pendingSketchWas3D;
        private string? _pendingDocKey;

        // Toggle cache (best-effort)
        private bool? _formatToggleState;

        // UI invoker for deferring work off Inventor event stack
        private readonly WF.Control _uiInvoker = new WF.Control();
        private bool _invokerReady = false;

        // Tracks which documents have been primed (by FullFileName or DisplayName)
        private readonly System.Collections.Generic.HashSet<string> _primedDocs
            = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _started;
        public bool IsStarted => _started;

        public SketchShowFormatController(
            Inventor.Application app,
            SketchDimensionPolicy dims,
            SketchStyler styler,
            Action<string, string> logger)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _dims = dims ?? throw new ArgumentNullException(nameof(dims));
            _styler = styler ?? throw new ArgumentNullException(nameof(styler));
            _log = logger ?? ((m, l) => { });
        }

        public void Start()
        {
            if (_started) return;

            _appEvents = _app.ApplicationEvents;
            _uiEvents = _app.UserInterfaceManager.UserInterfaceEvents;

            _onNewEditObjectHandler = AppEvents_OnNewEditObject;
            _onEnvironmentChangeHandler = UiEvents_OnEnvironmentChange;

            _appEvents.OnNewEditObject += _onNewEditObjectHandler;
            _uiEvents.OnEnvironmentChange += _onEnvironmentChangeHandler;

            ResetSession();
            ResetExit();
            _formatToggleState = null;

            try { _uiInvoker.CreateControl(); _invokerReady = true; }
            catch { _invokerReady = false; }

            Log("SketchShowFormatController started.");
            _started = true;
        }

        public void Stop()
        {
            try
            {
                if (_appEvents != null && _onNewEditObjectHandler != null)
                    _appEvents.OnNewEditObject -= _onNewEditObjectHandler;
            }
            catch { }

            try
            {
                if (_uiEvents != null && _onEnvironmentChangeHandler != null)
                    _uiEvents.OnEnvironmentChange -= _onEnvironmentChangeHandler;
            }
            catch { }

            _onNewEditObjectHandler = null;
            _onEnvironmentChangeHandler = null;
            _appEvents = null;
            _uiEvents = null;

            ResetSession();
            ResetExit();
            _primedDocs.Clear();

            try { _uiInvoker?.Dispose(); } catch { }

            Log("SketchShowFormatController stopped.");
            _started = false;
        }

        public void RunSuppressed(Action action)
        {
            _suppressCount++;
            try { action(); }
            finally { _suppressCount--; }
        }

        // ============================================================
        // Sketch environment priming
        // ============================================================

        /// <summary>
        /// Silently edit and exit the first available sketch in the part to prime
        /// Inventor's sketch environment. This ensures that subsequent user-initiated
        /// sketch edits have the environment fully initialised (fixes first-edit issues
        /// with derived sketches and newly opened documents).
        ///
        /// Call from OnActivateDocument or similar. Safe to call multiple times per
        /// document — only primes once per document per session.
        /// </summary>
        public void PrimeIfNeeded(PartDocument partDoc)
        {
            try
            {
                string key = GetDocKey(partDoc);
                if (string.IsNullOrWhiteSpace(key)) return;
                if (_primedDocs.Contains(key)) return;

                // Mark as primed immediately to prevent re-entry
                _primedDocs.Add(key);

                var cd = partDoc.ComponentDefinition;

                // Find any sketch to use for priming
                object? sketchToPrime = null;

                foreach (object o in cd.Sketches)
                {
                    if (o is Sketch sk)
                    {
                        sketchToPrime = sk;
                        break;
                    }
                }

                if (sketchToPrime == null)
                {
                    foreach (object o in cd.Sketches3D)
                    {
                        if (o is Sketch3D sk3)
                        {
                            sketchToPrime = sk3;
                            break;
                        }
                    }
                }

                if (sketchToPrime == null)
                {
                    Log("PrimeIfNeeded: no sketches found, skipping.");
                    return;
                }

                Log("PrimeIfNeeded: priming sketch environment...");

                RunSuppressed(() =>
                {
                    if (sketchToPrime is Sketch s2)
                    {
                        s2.Edit();
                        try { s2.ExitEdit(); } catch { }
                    }
                    else if (sketchToPrime is Sketch3D s3)
                    {
                        s3.Edit();
                        try { s3.ExitEdit(); } catch { }
                    }
                });

                Log("PrimeIfNeeded: done.");
            }
            catch (Exception ex)
            {
                Log("PrimeIfNeeded failed: " + ex.Message, "Warn");
            }
        }

        // ============================================================
        // ENTER sketch
        // ============================================================
        private void AppEvents_OnNewEditObject(
            object EditObject,
            EventTimingEnum BeforeOrAfter,
            NameValueMap Context,
            out HandlingCodeEnum HandlingCode)
        {
            HandlingCode = HandlingCodeEnum.kEventNotHandled;
            if (IsSuppressed) return;
            if (BeforeOrAfter != EventTimingEnum.kAfter) return;

            try
            {
                if (_app.ActiveDocument is not PartDocument partDoc)
                    return;

                if (!IsInSketchEnvironment())
                    return;

                object? activeEdit = null;
                try { activeEdit = _app.ActiveEditObject; } catch { activeEdit = null; }

                object editTarget = EditObject ?? activeEdit ?? throw new InvalidOperationException("No edit context");
                object resolvedSketch = ResolveToNativeSketchIfPossible(editTarget, partDoc);

                _lastEditedSketch = resolvedSketch;
                _lastSketchName = SketchHelpers.TryGetName(resolvedSketch);
                _lastSketchWas3D = resolvedSketch is Sketch3D;
                _lastDocKey = GetDocKey(partDoc);

                // Record current Show Format state and only restore if we changed it.
                bool wasOn = TryGetShowFormatPressed(out bool pressed) ? pressed : false;
                _restoreShowFormatOnExit = !wasOn;

                // Enter: clear any persisted inactive-palette overrides
                try { _styler.ClearOverrides(resolvedSketch); } catch { }

                // Enter: dims show-all
                try { _dims.OnEnter(editTarget); } catch { }

                // Enter: ensure Show Format ON (but do not fight user's existing preference)
                if (!wasOn)
                    TrySetShowFormatToggle(desiredOn: true, context: "enter");

                try { _app.ActiveView.Update(); } catch { }
            }
            catch (Exception ex)
            {
                Log("OnNewEditObject failed: " + ex, "Warn");
            }
        }

        // ============================================================
        // EXIT sketch (no polling)
        // ============================================================
        private void UiEvents_OnEnvironmentChange(
    InvEnvironment Environment,
    EnvironmentStateEnum EnvironmentState,
    EventTimingEnum BeforeOrAfter,
    NameValueMap Context,
    out HandlingCodeEnum HandlingCode)
        {
            HandlingCode = HandlingCodeEnum.kEventNotHandled;
            if (IsSuppressed) return;

            try
            {
                string envName = "";
                try { envName = Environment?.InternalName ?? ""; } catch { envName = ""; }

                bool envIsSketch = envName.IndexOf("Sketch", StringComparison.OrdinalIgnoreCase) >= 0;

                string stName = EnvironmentState.ToString();

                bool isSuspend =
                    stName.IndexOf("Suspend", StringComparison.OrdinalIgnoreCase) >= 0;

                bool isTerminate =
                    stName.IndexOf("Terminate", StringComparison.OrdinalIgnoreCase) >= 0;

                bool isDeactivate =
                    stName.IndexOf("Deactivate", StringComparison.OrdinalIgnoreCase) >= 0;

                // We only care about transitions involving sketch environment
                if (!envIsSketch)
                    return;

                // This log is intentionally verbose; it helps diagnose Inventor's env-state quirks.
                Log($"EnvChange[{(BeforeOrAfter == EventTimingEnum.kBefore ? "kBefore" : "kAfter")}]: env={envName}, state={stName}, restoreOff={_restoreShowFormatOnExit}, exitPending={_exitPending}, queued={_finalizeQueued}");

                // Key behavior change:
                // - Treat Suspend as "not a real exit" (commonly happens on document switching)
                // - Still allow Terminate / Deactivate to stage an exit
                // - But we will *re-verify* at finalize time before applying exit policy
                if (isSuspend)
                {
                    Log($"EnvChange: ignoring sketch suspend (likely doc switch). state={stName}");
                    return;
                }

                bool looksLikeLeaving = isTerminate || isDeactivate;
                if (!looksLikeLeaving)
                    return;

                if (BeforeOrAfter == EventTimingEnum.kBefore)
                {
                    // Stage once
                    _exitPending = true;
                    _finalizeQueued = false;

                    _pendingRestoreShowFormatOnExit = _restoreShowFormatOnExit;
                    _pendingSketchName = _lastSketchName;
                    _pendingSketchWas3D = _lastSketchWas3D;
                    _pendingDocKey = _lastDocKey;

                    _toggleOffOk = true;

                    // If we changed Show Format on enter, we want to restore OFF on exit.
                    // Do it in kBefore if possible (command sometimes disabled during transitions).
                    if (_pendingRestoreShowFormatOnExit)
                        _toggleOffOk = TrySetShowFormatToggle(desiredOn: false, context: "exit kBefore");

                    return;
                }

                // kAfter: defer heavy work (dims + ApplyInactive) off event stack
                if (_exitPending && !_finalizeQueued)
                {
                    _finalizeQueued = true;
                    ScheduleFinalizeExit();
                }
            }
            catch (Exception ex)
            {
                Log("OnEnvironmentChange failed: " + ex, "Warn");
            }
        }

        private void ScheduleFinalizeExit()
        {
            try
            {
                if (!_invokerReady)
                {
                    try { _uiInvoker.CreateControl(); _invokerReady = true; }
                    catch { _invokerReady = false; }
                }

                if (_invokerReady)
                {
                    _uiInvoker.BeginInvoke(new Action(() =>
                    {
                        try { FinalizeExitSafe(); }
                        catch (Exception ex) { Log("FinalizeExitSafe failed: " + ex, "Warn"); }
                    }));
                }
                else
                {
                    // Fallback
                    FinalizeExitSafe();
                }
            }
            catch (Exception ex)
            {
                Log("ScheduleFinalizeExit failed: " + ex, "Warn");
            }
        }

        private void FinalizeExitSafe()
        {
            // If user switched docs quickly, bail
            if (_app.ActiveDocument is not PartDocument partDoc)
            {
                ResetSession();
                ResetExit();
                return;
            }

            // If we are no longer on the same document we staged exit for, bail.
            // (Prevents applying exit policy to a different file.)
            if (!string.IsNullOrWhiteSpace(_pendingDocKey) && GetDocKey(partDoc) != _pendingDocKey)
            {
                ResetSession();
                ResetExit();
                return;
            }

            // Critical fix:
            // If Inventor "deactivated/terminated" the sketch environment due to a document switch,
            // it's possible the sketch edit is STILL active when we get here (or has resumed).
            // In that case, DO NOT apply exit policies (dims hide / inactive palette).
            if (IsSketchEditStillActive())
            {
                Log("FinalizeExitSafe: sketch edit still active -> skipping exit policy.");
                ResetExit(); // clear staged exit; keep session so we don't lose name/doc key
                return;
            }

            object? exitTarget =
                ReacquireSketchFromPart(partDoc, _pendingSketchName, _pendingSketchWas3D)
                ?? _lastEditedSketch;

            if (exitTarget != null)
            {
                try { _dims.OnExit(exitTarget); } catch { }

                // Only apply inactive palette once we are truly out of sketch edit.
                try { _styler.ApplyInactive(exitTarget); } catch { }
            }

            // If we needed to restore OFF but couldn't in kBefore (command disabled), do a safe round-trip.
            if (_pendingRestoreShowFormatOnExit && !_toggleOffOk && exitTarget != null)
            {
                EnsureShowFormatOffViaRoundTrip(exitTarget);
            }

            try { _app.ActiveView.Update(); } catch { }

            ResetSession();
            ResetExit();

            Log("FinalizeExitSafe complete.");
        }

        // ============================================================
        // Helpers / state reset
        // ============================================================
        private void ResetSession()
        {
            _lastEditedSketch = null;
            _lastSketchName = null;
            _lastSketchWas3D = false;
            _lastDocKey = null;
            _restoreShowFormatOnExit = false;
        }

        private void ResetExit()
        {
            _exitPending = false;
            _finalizeQueued = false;
            _pendingRestoreShowFormatOnExit = false;
            _toggleOffOk = false;
            _pendingSketchName = null;
            _pendingSketchWas3D = false;
            _pendingDocKey = null;
        }

        private static string GetDocKey(PartDocument doc)
        {
            try
            {
                var fn = doc.FullFileName;
                if (!string.IsNullOrWhiteSpace(fn)) return fn;
            }
            catch { }

            try { return doc.DisplayName ?? ""; } catch { return ""; }
        }

        private static object? ReacquireSketchFromPart(PartDocument partDoc, string? name, bool is3d)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            try
            {
                var cd = partDoc.ComponentDefinition;

                if (!is3d)
                {
                    foreach (object o in cd.Sketches)
                        if (o is Sketch sk && string.Equals(sk.Name, name, StringComparison.OrdinalIgnoreCase))
                            return sk;
                }
                else
                {
                    foreach (object o in cd.Sketches3D)
                        if (o is Sketch3D sk3 && string.Equals(sk3.Name, name, StringComparison.OrdinalIgnoreCase))
                            return sk3;
                }
            }
            catch { }

            return null;
        }

        private bool IsInSketchEnvironment()
        {
            try
            {
                var env = _app.UserInterfaceManager?.ActiveEnvironment;
                var name = env?.InternalName ?? "";
                return name.IndexOf("Sketch", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private object ResolveToNativeSketchIfPossible(object editTarget, PartDocument partDoc)
        {
            if (editTarget is Sketch || editTarget is Sketch3D)
                return editTarget;

            try
            {
                dynamic d = editTarget;
                object native = d.NativeObject;
                if (native is Sketch || native is Sketch3D)
                    return native;
            }
            catch { }

            var nm = SketchHelpers.TryGetName(editTarget);
            if (!string.IsNullOrWhiteSpace(nm))
            {
                var s2 = SketchHelpers.Find2DSketchByName(partDoc, nm);
                if (s2 != null) return s2;

                var s3 = SketchHelpers.Find3DSketchByName(partDoc, nm);
                if (s3 != null) return s3;
            }

            return editTarget;
        }

        // ============================================================
        // Show Format toggle (Pressed read via ButtonDefinition)
        // ============================================================
        private bool TryGetShowFormatPressed(out bool pressed)
        {
            pressed = false;
            try
            {
                var btn = _app.CommandManager.ControlDefinitions["AppLinePropertiesToggleCmd"] as ButtonDefinition;
                if (btn == null) return false;

                pressed = btn.Pressed;
                _formatToggleState = pressed;
                return true;
            }
            catch
            {
                if (_formatToggleState.HasValue)
                {
                    pressed = _formatToggleState.Value;
                    return true;
                }
                return false;
            }
        }

        private bool TrySetShowFormatToggle(bool desiredOn, string context)
        {
            ButtonDefinition? btn = null;

            try
            {
                btn = _app.CommandManager.ControlDefinitions["AppLinePropertiesToggleCmd"] as ButtonDefinition;
            }
            catch (Exception ex)
            {
                Log($"ShowFormat[{context}]: cannot resolve AppLinePropertiesToggleCmd. {ex.Message}", "Warn");
                return false;
            }

            if (btn == null)
            {
                Log($"ShowFormat[{context}]: AppLinePropertiesToggleCmd is not a ButtonDefinition.", "Warn");
                return false;
            }

            bool enabled;
            bool pressed;

            try { enabled = btn.Enabled; }
            catch { enabled = true; }

            try
            {
                pressed = btn.Pressed;
                _formatToggleState = pressed;
            }
            catch
            {
                pressed = _formatToggleState ?? !desiredOn;
            }

            Log($"ShowFormat[{context}]: enabled={enabled}, pressed={pressed}, cache={(_formatToggleState.HasValue ? _formatToggleState.Value.ToString() : "?")}, desired={desiredOn}");

            if (pressed == desiredOn)
            {
                _formatToggleState = desiredOn;
                return true;
            }

            if (!enabled)
                return false;

            try { btn.Execute(); }
            catch (Exception ex)
            {
                Log($"ShowFormat[{context}]: Execute() failed. {ex.Message}", "Warn");
                return false;
            }

            try
            {
                pressed = btn.Pressed;
                _formatToggleState = pressed;
                return pressed == desiredOn;
            }
            catch
            {
                _formatToggleState = desiredOn;
                return true;
            }
        }

        private void EnsureShowFormatOffViaRoundTrip(object sketchObj)
        {
            try
            {
                RunSuppressed(() =>
                {
                    if (sketchObj is Sketch s2)
                    {
                        s2.Edit();
                        try { TrySetShowFormatToggle(desiredOn: false, context: "roundtrip 2D"); }
                        finally { try { s2.ExitEdit(); } catch { } }
                        return;
                    }

                    if (sketchObj is Sketch3D s3)
                    {
                        s3.Edit();
                        try { TrySetShowFormatToggle(desiredOn: false, context: "roundtrip 3D"); }
                        finally { try { s3.ExitEdit(); } catch { } }
                        return;
                    }

                    try
                    {
                        dynamic d = sketchObj;
                        d.Edit();
                        try { TrySetShowFormatToggle(desiredOn: false, context: "roundtrip dyn"); }
                        finally { try { d.ExitEdit(); } catch { } }
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                Log("EnsureShowFormatOffViaRoundTrip failed: " + ex.Message, "Warn");
            }
        }

        private void Log(string msg, string level = "Info") => _log(msg, level);

        public void Dispose() => Stop();

        private bool IsSketchEditStillActive()
        {
            try
            {
                if (!IsInSketchEnvironment())
                    return false;

                object? editObj = null;
                try { editObj = _app.ActiveEditObject; } catch { editObj = null; }
                if (editObj == null) return false;

                if (editObj is Sketch || editObj is Sketch3D)
                    return true;

                // Some edit objects come through as wrappers; try NativeObject
                try
                {
                    dynamic d = editObj;
                    object native = d.NativeObject;
                    return native is Sketch || native is Sketch3D;
                }
                catch { }

                return false;
            }
            catch { return false; }
        }

    }
}
