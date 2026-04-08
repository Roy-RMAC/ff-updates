// DrawingDock/Services/DrawingContextResolver.cs
using Inventor;
using System;
using System.Collections.Generic;

namespace RMAC_Efficiency_Addin.DrawingDock.Services
{
    /// <summary>
    /// Resolves "where am I?" context for the Drawing Dock feature.
    /// Centralises all COM-sensitive checks:
    /// - Active document type (drawing vs not)
    /// - Active sheet
    /// - Selected drawing view (direct or via selection)
    /// - Referenced model document from the selected view
    /// </summary>
    internal sealed class DrawingContextResolver
    {
        internal sealed class DrawingContext
        {
            public bool IsValidDrawingContext { get; }
            public string ReasonIfInvalid { get; }

            public Document? ActiveDocument { get; }
            public DrawingDocument? DrawingDocument { get; }
            public Sheet? ActiveSheet { get; }

            public DrawingView? SelectedView { get; }
            public Document? ReferencedModelDocument { get; }

            public IReadOnlyList<object> SelectionItems { get; }

            internal DrawingContext(
                bool isValid,
                string reasonIfInvalid,
                Document? activeDoc,
                DrawingDocument? drawingDoc,
                Sheet? activeSheet,
                DrawingView? selectedView,
                Document? referencedModelDoc,
                IReadOnlyList<object> selectionItems)
            {
                IsValidDrawingContext = isValid;
                ReasonIfInvalid = reasonIfInvalid;

                ActiveDocument = activeDoc;
                DrawingDocument = drawingDoc;
                ActiveSheet = activeSheet;

                SelectedView = selectedView;
                ReferencedModelDocument = referencedModelDoc;

                SelectionItems = selectionItems;
            }
        }

        private readonly Inventor.Application _app;

        public DrawingContextResolver(Inventor.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        /// <summary>
        /// Resolve current drawing context from Inventor, using the application's active document.
        /// Thin shim over <see cref="GetContext(Document?)"/> for the common case.
        /// </summary>
        public DrawingContext GetContext()
        {
            return GetContext(TryGetActiveDocument());
        }

        /// <summary>
        /// Resolve drawing context for the given document. Exposed as a separate overload so
        /// tests and other callers can drive resolution without depending on
        /// <c>Application.ActiveDocument</c>.
        /// If a view isn't explicitly selected, tries to infer it from the document's selection set.
        /// </summary>
        public DrawingContext GetContext(Document? activeDoc)
        {
            if (activeDoc == null)
            {
                return Invalid("No active document.", activeDoc);
            }

            if (activeDoc.DocumentType != DocumentTypeEnum.kDrawingDocumentObject)
            {
                return Invalid("Active document is not a drawing.", activeDoc);
            }

            var dwg = activeDoc as DrawingDocument;
            if (dwg == null)
            {
                // Rare COM/casting weirdness
                dwg = TryCastDrawing(activeDoc);
                if (dwg == null)
                    return Invalid("Active document could not be treated as a DrawingDocument.", activeDoc);
            }

            Sheet? sheet = TryGetActiveSheet(dwg);
            if (sheet == null)
                return Invalid("Drawing has no active sheet.", activeDoc, dwg);

            // Snapshot selection
            var selectionItems = GetSelectionSnapshot(activeDoc);

            // Resolve selected view: prefer explicit active/selected view if you already track one,
            // otherwise infer from selection.
            DrawingView? view = TryResolveSelectedView(dwg, selectionItems);

            // Resolve referenced model from selected view (if any)
            Document? modelDoc = null;
            if (view != null)
                modelDoc = TryGetReferencedModelDocument(view);

            return new DrawingContext(
                isValid: true,
                reasonIfInvalid: "",
                activeDoc: activeDoc,
                drawingDoc: dwg,
                activeSheet: sheet,
                selectedView: view,
                referencedModelDoc: modelDoc,
                selectionItems: selectionItems
            );
        }

        // -------------------------
        // Private helpers
        // -------------------------

        private DrawingContext Invalid(string reason, Document? activeDoc, DrawingDocument? dwg = null)
        {
            return new DrawingContext(
                isValid: false,
                reasonIfInvalid: reason,
                activeDoc: activeDoc,
                drawingDoc: dwg,
                activeSheet: null,
                selectedView: null,
                referencedModelDoc: null,
                selectionItems: Array.Empty<object>()
            );
        }

        private Document? TryGetActiveDocument()
        {
            try { return _app.ActiveDocument; }
            catch { return null; }
        }

        private DrawingDocument? TryCastDrawing(Document doc)
        {
            try { return (DrawingDocument)doc; }
            catch { return null; }
        }

        private static Sheet? TryGetActiveSheet(DrawingDocument dwg)
        {
            try { return dwg.ActiveSheet; }
            catch { return null; }
        }

        private static IReadOnlyList<object> GetSelectionSnapshot(Document doc)
        {
            var list = new List<object>(8);

            try
            {
                var ss = doc?.SelectSet;
                if (ss == null) return list;

                // SelectSet is 1-based indexing
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
                        // skip bad selection entries
                    }
                }
            }
            catch
            {
                // ignore selection failures
            }

            return list;
        }

        private static DrawingView? TryResolveSelectedView(DrawingDocument dwg, IReadOnlyList<object> selectionItems)
        {
            // 1) If the selection itself is a DrawingView, return it
            foreach (var obj in selectionItems)
            {
                if (obj is DrawingView dv) return dv;
            }

            // 2) If something selected can provide a parent view (common: DrawingCurveSegment, DrawingCurve)
            foreach (var obj in selectionItems)
            {
                var inferred = TryGetParentView(obj);
                if (inferred != null) return inferred;
            }

            // 3) Fallback: if user has a view "active" via sheet selection, try ActiveSheet.DrawingViews,
            // but Inventor doesn't expose "active view" reliably, so we leave null.
            return null;
        }

        private static DrawingView? TryGetParentView(object selected)
        {
            try
            {
                // Many drawing entities support a Parent/ParentView-like property.
                // Use dynamic guarded access, but never throw.
                dynamic d = selected;

                // Common: DrawingCurveSegment.Parent or .ParentDrawingCurve or .ParentView
                try
                {
                    // e.g. DrawingCurveSegment.Parent might be DrawingCurve
                    var parent = d.Parent;
                    if (parent is DrawingView v1) return v1;
                    if (parent != null)
                    {
                        var pv = TryGetParentView(parent);
                        if (pv != null) return pv;
                    }
                }
                catch { }

                try
                {
                    var pv = d.ParentView;
                    if (pv is DrawingView v2) return v2;
                }
                catch { }

                try
                {
                    var dc = d.ParentDrawingCurve;
                    if (dc != null)
                    {
                        var pv = TryGetParentView(dc);
                        if (pv != null) return pv;
                    }
                }
                catch { }

                try
                {
                    var dv = d.DrawingView;
                    if (dv is DrawingView v3) return v3;
                }
                catch { }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static Document? TryGetReferencedModelDocument(DrawingView view)
        {
            try
            {
                var desc = view.ReferencedDocumentDescriptor;
                if (desc == null) return null;

                object? obj = null;
                try { obj = desc.ReferencedDocument; }
                catch { return null; }

                // Inventor PIAs often expose this as object; safely cast.
                if (obj is Document d) return d;

                // Sometimes it comes through as _Document only
                if (obj is _Document ud) return ud as Document;

                return null;
            }
            catch
            {
                return null;
            }
        }

    }
}
