// DrawingDock/Services/SelectionService.cs
using Inventor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RMAC_Efficiency_Addin.DrawingDock.Services
{
    /// <summary>
    /// COM-safe selection utilities for Inventor.
    /// - Snapshot the SelectSet without throwing.
    /// - Resolve a DrawingView from selected drawing entities (curve segments, dims, etc.).
    /// - Replace / append selection.
    /// </summary>
    internal sealed class SelectionService
    {
        private readonly Inventor.Application _app;

        public SelectionService(Inventor.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        /// <summary>
        /// Returns a snapshot of current selection items from the application's active document.
        /// Thin shim over <see cref="GetSelectionSnapshot(Document?)"/>.
        /// </summary>
        public IReadOnlyList<object> GetSelectionSnapshot()
        {
            return GetSelectionSnapshot(TryGetActiveDocument());
        }

        /// <summary>
        /// Returns a snapshot of selection items from the given document. 1-based COM-safe enumeration.
        /// Exposed as an overload so tests and other callers can drive selection reads without
        /// depending on <c>Application.ActiveDocument</c>.
        /// </summary>
        public static IReadOnlyList<object> GetSelectionSnapshot(Document? doc)
        {
            var list = new List<object>(8);

            SelectSet? ss = TryGetSelectSet(doc);
            if (ss == null) return list;

            int count = 0;
            try { count = ss.Count; } catch { return list; }

            for (int i = 1; i <= count; i++)
            {
                try
                {
                    object item = ss[i];
                    if (item != null) list.Add(item);
                }
                catch
                {
                    // ignore bad selection entries
                }
            }

            return list;
        }

        /// <summary>
        /// Convenience: try get a single selected item of type T, else null.
        /// </summary>
        public T? TryGetSingleSelected<T>() where T : class
        {
            var items = GetSelectionSnapshot();
            if (items.Count != 1) return null;
            return items[0] as T;
        }

        /// <summary>
        /// Tries to resolve a DrawingView from current selection in the active document.
        /// Works when user selects:
        /// - DrawingView
        /// - DrawingCurve / DrawingCurveSegment
        /// - Many other drawing entities that have Parent/ParentView/DrawingView relationships.
        /// </summary>
        public DrawingView? TryResolveDrawingViewFromSelection()
        {
            return TryResolveDrawingViewFromSelection(TryGetActiveDocument());
        }

        /// <summary>
        /// Tries to resolve a DrawingView from the given document's current selection.
        /// Document-explicit overload for tests and other callers that want to avoid
        /// <c>Application.ActiveDocument</c>.
        /// </summary>
        public static DrawingView? TryResolveDrawingViewFromSelection(Document? doc)
        {
            var items = GetSelectionSnapshot(doc);
            return TryResolveDrawingView(items);
        }

        /// <summary>
        /// Tries to resolve a DrawingView from a provided selection snapshot.
        /// </summary>
        public static DrawingView? TryResolveDrawingView(IReadOnlyList<object> selectionItems)
        {
            if (selectionItems == null || selectionItems.Count == 0)
                return null;

            // Direct selection of a DrawingView
            foreach (var obj in selectionItems)
            {
                if (obj is DrawingView dv) return dv;
            }

            // Try infer view from parent relationships
            foreach (var obj in selectionItems)
            {
                var inferred = TryGetParentView(obj);
                if (inferred != null) return inferred;
            }

            return null;
        }

        /// <summary>
        /// Clears the selection set. Safe if no active doc.
        /// </summary>
        public void ClearSelection()
        {
            var ss = TryGetSelectSet();
            if (ss == null) return;

            try { ss.Clear(); } catch { }
        }

        /// <summary>
        /// Selects the given objects. If append = false, clears selection first.
        /// Ignores objects that Inventor rejects.
        /// </summary>
        public void SelectObjects(IEnumerable<object> objectsToSelect, bool append = false)
        {
            if (objectsToSelect == null) return;

            var ss = TryGetSelectSet();
            if (ss == null) return;

            if (!append)
            {
                try { ss.Clear(); } catch { }
            }

            foreach (var obj in objectsToSelect)
            {
                if (obj == null) continue;

                try
                {
                    // SelectSet.Select(object) exists, but the exact call shape varies.
                    // Dynamic avoids brittle COM signatures.
                    dynamic dss = ss;
                    dss.Select(obj);
                }
                catch
                {
                    // ignore objects that cannot be selected
                }
            }
        }

        /// <summary>
        /// Select a single object, replacing existing selection by default.
        /// </summary>
        public void SelectSingle(object obj, bool append = false)
        {
            if (obj == null) return;
            SelectObjects(new[] { obj }, append);
        }

        // --------------------------
        // Internals
        // --------------------------

        private SelectSet? TryGetSelectSet()
        {
            return TryGetSelectSet(TryGetActiveDocument());
        }

        private static SelectSet? TryGetSelectSet(Document? doc)
        {
            try
            {
                if (doc == null) return null;
                return doc.SelectSet;
            }
            catch
            {
                return null;
            }
        }

        private Document? TryGetActiveDocument()
        {
            try { return _app.ActiveDocument; }
            catch { return null; }
        }

        private static DrawingView? TryGetParentView(object selected)
        {
            // This is intentionally dynamic + guarded because Inventor drawing entities
            // vary widely (curve segments, dims, annotations, etc.)
            try
            {
                dynamic d = selected;

                // Common: ParentView
                try
                {
                    var pv = d.ParentView;
                    if (pv is DrawingView v) return v;
                }
                catch { }

                // Common: DrawingView property (some objects expose this directly)
                try
                {
                    var dv = d.DrawingView;
                    if (dv is DrawingView v) return v;
                }
                catch { }

                // Common: Parent chain (curve segment -> curve -> view)
                try
                {
                    var parent = d.Parent;
                    if (parent is DrawingView v) return v;

                    if (parent != null)
                    {
                        var viaParent = TryGetParentView(parent);
                        if (viaParent != null) return viaParent;
                    }
                }
                catch { }

                // Common: ParentDrawingCurve chain
                try
                {
                    var dc = d.ParentDrawingCurve;
                    if (dc != null)
                    {
                        var viaCurve = TryGetParentView(dc);
                        if (viaCurve != null) return viaCurve;
                    }
                }
                catch { }
            }
            catch
            {
                // ignore
            }

            return null;
        }
    }
}
