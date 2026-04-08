using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Inventor;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Pure logic for sketch colour override operations.
    /// Caller is responsible for document-type validation, sketch-edit checks,
    /// pick interactions, and running inside a transaction.
    /// </summary>
    internal static class SketchColorCore
    {
        /// <summary>
        /// Ensures Inventor's "Show Format" toggle (AppLinePropertiesToggleCmd) is in the
        /// desired state, using a brief sketch Edit/ExitEdit round-trip to read the reliable
        /// in-sketch button state. If <paramref name="showFormat"/> is supplied, the
        /// round-trip runs under its <c>RunSuppressed</c> wrapper to avoid controller
        /// side-effects; otherwise it runs directly.
        /// </summary>
        public static void EnsureShowFormatState(
            Application app,
            object sketchObj,
            bool desiredOn,
            SketchShowFormatController? showFormat)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (sketchObj == null) throw new ArgumentNullException(nameof(sketchObj));

            var btn = app.CommandManager.ControlDefinitions["AppLinePropertiesToggleCmd"] as ButtonDefinition;
            if (btn == null)
                throw new InvalidOperationException("Could not resolve Inventor command: AppLinePropertiesToggleCmd (Show Format).");

            bool pressedOutside;
            try { pressedOutside = btn.Pressed; }
            catch { pressedOutside = !desiredOn; }

            // NOTE:
            // ButtonDefinition.Pressed is not reliably readable outside an active sketch environment for
            // AppLinePropertiesToggleCmd. It can report "false" even when the toggle is effectively ON for sketches.
            // To avoid a silent no-op (especially when turning OFF), we always do a brief sketch Edit/ExitEdit
            // round-trip and decide based on the state read while the sketch environment is active.
            void ExecuteToggleInSketch()
            {
                bool pressed;
                try { pressed = btn.Pressed; }
                catch { pressed = pressedOutside; }

                if (pressed == desiredOn) return;

                try { btn.Execute(); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to execute Show Format toggle: {ex.Message}");
                }
            }

            Action roundTrip = () =>
            {
                if (sketchObj is Sketch s2)
                {
                    s2.Edit();
                    try { ExecuteToggleInSketch(); }
                    finally { try { s2.ExitEdit(); } catch { } }
                    return;
                }

                if (sketchObj is Sketch3D s3)
                {
                    s3.Edit();
                    try { ExecuteToggleInSketch(); }
                    finally { try { s3.ExitEdit(); } catch { } }
                    return;
                }

                try
                {
                    dynamic d = sketchObj;
                    d.Edit();
                    try { ExecuteToggleInSketch(); }
                    finally { try { d.ExitEdit(); } catch { } }
                }
                catch
                {
                    // Last resort: attempt direct execution (may no-op if not in sketch env)
                    ExecuteToggleInSketch();
                }
            };

            if (showFormat != null)
                showFormat.RunSuppressed(roundTrip);
            else
                roundTrip();
        }

        /// <summary>
        /// "Clear Sketch Color Overrides" core: turns Show Format ON via the first sketch.
        /// The existing behaviour only uses the first sketch as the entry point for the toggle;
        /// preserved here.
        /// </summary>
        public static void ClearOverrides(
            Application app,
            object firstSketch,
            SketchShowFormatController? showFormat)
        {
            if (firstSketch == null) throw new ArgumentNullException(nameof(firstSketch));
            EnsureShowFormatState(app, firstSketch, desiredOn: true, showFormat);
        }

        /// <summary>
        /// "Release Sketch Color Hold" core: turns Show Format OFF via the first sketch, then
        /// re-applies the inactive palette to every supplied sketch via the styler so the
        /// custom scheme reappears immediately.
        /// </summary>
        public static void ReleaseHold(
            Application app,
            System.Collections.Generic.IList<object> sketches,
            SketchShowFormatController? showFormat,
            SketchStyler styler)
        {
            if (sketches == null) throw new ArgumentNullException(nameof(sketches));
            if (sketches.Count == 0) throw new ArgumentException("At least one sketch is required.", nameof(sketches));
            if (styler == null) throw new ArgumentNullException(nameof(styler));

            EnsureShowFormatState(app, sketches[0], desiredOn: false, showFormat);

            foreach (var sk in sketches)
            {
                try { styler.ApplyInactive(sk); } catch { }
            }
        }

        /// <summary>
        /// Attempts to clear OverrideColor on a single sketch entity using multiple COM semantics,
        /// and returns a diagnostic report showing what worked (if anything).
        /// </summary>
        public static string ClearOneOverride(object entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var sb = new StringBuilder();

            string typeName = "(unknown)";
            try { typeName = entity.GetType().FullName ?? entity.GetType().Name; } catch { }
            sb.AppendLine($"Target runtime type: {typeName}");

            // Read before (best effort)
            object? before = null;
            bool canRead = false;
            try
            {
                dynamic d = entity;
                before = d.OverrideColor;
                canRead = true;
                sb.AppendLine($"OverrideColor before: {(before == null ? "null" : "non-null")}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"OverrideColor before: <READ FAIL> {ex.GetType().Name} - {ex.Message}");
            }

            // Try clear strategies in order
            var attempts = new (string name, Func<bool> fn)[]
            {
                ("PUTREF VT_DISPATCH(null) via DispatchWrapper(null)", () => TryInvokeMember(entity,
                    BindingFlags.PutRefDispProperty,
                    new object[] { new DispatchWrapper(null) })),

                ("PUTREF null", () => TryInvokeMember(entity,
                    BindingFlags.PutRefDispProperty,
                    new object?[] { null })),

                ("PUT VT_EMPTY via Missing.Value (OptionalParamBinding)", () => TryInvokeMember(entity,
                    BindingFlags.PutDispProperty | BindingFlags.OptionalParamBinding,
                    new object[] { Missing.Value })),

                ("PUT VT_NULL via DBNull.Value", () => TryInvokeMember(entity,
                    BindingFlags.PutDispProperty,
                    new object[] { DBNull.Value })),

                ("PUT null", () => TryInvokeMember(entity,
                    BindingFlags.PutDispProperty,
                    new object?[] { null })),
            };

            bool anyWorked = false;
            foreach (var a in attempts)
            {
                bool ok = false;
                string? err = null;

                try
                {
                    ok = a.fn();
                }
                catch (Exception ex)
                {
                    ok = false;
                    err = $"{ex.GetType().Name} - {ex.Message}";
                }

                sb.AppendLine($"{(ok ? "[OK]  " : "[FAIL]")} {a.name}{(err != null ? " :: " + err : "")}");

                if (ok)
                {
                    anyWorked = true;
                    break;
                }
            }

            sb.AppendLine($"Any call returned OK: {anyWorked}");

            // Read after (best effort)
            object? after = null;
            bool canReadAfter = false;
            try
            {
                dynamic d = entity;
                after = d.OverrideColor;
                canReadAfter = true;
                sb.AppendLine($"OverrideColor after: {(after == null ? "null" : "non-null")}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"OverrideColor after: <READ FAIL> {ex.GetType().Name} - {ex.Message}");
            }

            // Interpretation line
            if (canRead && canReadAfter && before != null && after == null)
                sb.AppendLine("Result: VERIFIED CLEARED (before non-null -> after null).");
            else if (anyWorked && canReadAfter && after != null)
                sb.AppendLine("Result: CALL REPORTED OK but OverrideColor still reads non-null.");
            else if (anyWorked && !canReadAfter)
                sb.AppendLine("Result: CALL REPORTED OK but cannot verify via getter on this object.");
            else
                sb.AppendLine("Result: NO CLEAR METHOD SUCCEEDED (all failed).");

            return sb.ToString();
        }

        private static bool TryInvokeMember(object comObj, BindingFlags flags, object?[] args)
        {
            try
            {
                comObj.GetType().InvokeMember(
                    "OverrideColor",
                    flags,
                    null,
                    comObj,
                    args
                );
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
