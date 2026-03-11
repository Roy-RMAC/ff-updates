// DrawingDock/Services/ViewPlacementService.cs
using Inventor;
using System;
using System.Collections.Generic;

namespace RMAC_Efficiency_Addin.DrawingDock.Services
{
    /// <summary>
    /// Places drawing views on a sheet in a predictable layout.
    /// - No UI
    /// - COM safe (tries hard not to throw)
    /// - Uses dynamic to tolerate Inventor PIA overload differences
    /// </summary>
    internal sealed class ViewPlacementService
    {
        internal enum BaseOrientation
        {
            Front,
            Top,
            Right
        }

        internal sealed class PlaceViewsRequest
        {
            public DrawingDocument Drawing { get; set; } = null!;
            public Sheet Sheet { get; set; } = null!;
            public Document ModelDocument { get; set; } = null!;

            // Layout
            public double StartX { get; set; } = 10.0;
            public double StartY { get; set; } = 10.0;
            public double SpacingX { get; set; } = 12.0;
            public double SpacingY { get; set; } = 12.0;

            // View settings
            public BaseOrientation BaseViewOrientation { get; set; } = BaseOrientation.Front;
            public DrawingViewStyleEnum ViewStyle { get; set; } = DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle;

            /// <summary>
            /// If null/<=0, Inventor chooses. If >0, force this scale (e.g. 0.1 for 1:10).
            /// </summary>
            public double? Scale { get; set; } = null;

            public bool CreateTop { get; set; } = true;
            public bool CreateRight { get; set; } = true;
            public bool CreateIso { get; set; } = false;

            public bool ClearExistingViews { get; set; } = false;

            public bool SetViewNames { get; set; } = true;
        }

        internal sealed class PlaceViewsResult
        {
            public bool Success { get; }
            public DrawingView? BaseView { get; }
            public DrawingView? TopView { get; }
            public DrawingView? RightView { get; }
            public DrawingView? IsoView { get; }
            public IReadOnlyList<string> Warnings { get; }

            internal PlaceViewsResult(
                bool success,
                DrawingView? baseView,
                DrawingView? topView,
                DrawingView? rightView,
                DrawingView? isoView,
                List<string> warnings)
            {
                Success = success;
                BaseView = baseView;
                TopView = topView;
                RightView = rightView;
                IsoView = isoView;
                Warnings = warnings;
            }
        }

        private readonly Inventor.Application _app;

        public ViewPlacementService(Inventor.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public PlaceViewsResult Place(PlaceViewsRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            var warnings = new List<string>();

            if (req.Drawing == null || req.Sheet == null || req.ModelDocument == null)
                return new PlaceViewsResult(false, null, null, null, null, new List<string> { "Invalid request: Drawing/Sheet/ModelDocument is null." });

            try
            {
                if (req.ClearExistingViews)
                    TryClearViews(req.Sheet);
            }
            catch { }

            TransientGeometry tg;
            try { tg = _app.TransientGeometry; }
            catch
            {
                return new PlaceViewsResult(false, null, null, null, null, new List<string> { "Could not access TransientGeometry." });
            }

            var pBase = tg.CreatePoint2d(req.StartX, req.StartY);
            var pTop = tg.CreatePoint2d(req.StartX, req.StartY + req.SpacingY);
            var pRight = tg.CreatePoint2d(req.StartX + req.SpacingX, req.StartY);
            var pIso = tg.CreatePoint2d(req.StartX + req.SpacingX, req.StartY + req.SpacingY);

            DrawingView? baseView = TryAddBaseView(req, pBase, warnings);
            if (baseView == null)
                return new PlaceViewsResult(false, null, null, null, null, warnings);

            // If caller wants to force scale, do it after creation (most reliable across PIAs)
            if (req.Scale.HasValue && req.Scale.Value > 0)
            {
                try { baseView.Scale = req.Scale.Value; }
                catch { warnings.Add("Failed to apply requested scale to base view."); }
            }

            if (req.SetViewNames) TrySetName(baseView, "BASE");

            DrawingView? topView = null;
            DrawingView? rightView = null;

            if (req.CreateTop)
            {
                topView = TryAddProjectedView(req.Sheet, baseView, pTop, req.ViewStyle, warnings);
                if (topView != null && req.SetViewNames) TrySetName(topView, "TOP");
            }

            if (req.CreateRight)
            {
                rightView = TryAddProjectedView(req.Sheet, baseView, pRight, req.ViewStyle, warnings);
                if (rightView != null && req.SetViewNames) TrySetName(rightView, "RIGHT");
            }

            DrawingView? isoView = null;
            if (req.CreateIso)
            {
                isoView = TryAddIsoView(req, pIso, baseView, warnings);
                if (isoView != null && req.SetViewNames) TrySetName(isoView, "ISO");
            }

            return new PlaceViewsResult(true, baseView, topView, rightView, isoView, warnings);
        }

        private DrawingView? TryAddBaseView(PlaceViewsRequest req, Point2d pos, List<string> warnings)
        {
            try
            {
                // Explicit cast to _Document (fixes CS1503)
                var doc = (Inventor._Document)req.ModelDocument;

                // Many PIAs have this overload, but signatures differ — use dynamic as the last mile.
                double scale = (req.Scale.HasValue && req.Scale.Value > 0) ? req.Scale.Value : 1.0;
                var orient = ToViewOrientation(req.BaseViewOrientation);

                dynamic views = req.Sheet.DrawingViews;

                // Try common 5-arg overload first
                try
                {
                    return (DrawingView)views.AddBaseView(doc, pos, scale, orient, req.ViewStyle);
                }
                catch { }

                // Try older/larger overloads (arguments after style are often optional / can be null)
                try
                {
                    return (DrawingView)views.AddBaseView(doc, pos, scale, orient, req.ViewStyle, null, null, null);
                }
                catch { }

                warnings.Add("Failed to create base view (no compatible AddBaseView overload found).");
                return null;
            }
            catch (Exception ex)
            {
                warnings.Add("Failed to create base view: " + ex.Message);
                return null;
            }
        }

        private static DrawingView? TryAddProjectedView(Sheet sheet, DrawingView parent, Point2d pos, DrawingViewStyleEnum style, List<string> warnings)
        {
            try
            {
                dynamic views = sheet.DrawingViews;

                // Your build error shows this signature is required:
                // AddProjectedView(DrawingView, Point2d, DrawingViewStyleEnum, object)
                try
                {
                    return (DrawingView)views.AddProjectedView(parent, pos, style, null);
                }
                catch { }

                // Some versions still have a simpler overload
                try
                {
                    return (DrawingView)views.AddProjectedView(parent, pos);
                }
                catch { }

                warnings.Add("Failed to create projected view (no compatible AddProjectedView overload found).");
                return null;
            }
            catch (Exception ex)
            {
                warnings.Add("Failed to create projected view: " + ex.Message);
                return null;
            }
        }

        private DrawingView? TryAddIsoView(PlaceViewsRequest req, Point2d pos, DrawingView baseView, List<string> warnings)
        {
            try
            {
                var doc = (Inventor._Document)req.ModelDocument;

                double scale = 1.0;
                try { scale = baseView.Scale; } catch { }

                dynamic views = req.Sheet.DrawingViews;

                try
                {
                    return (DrawingView)views.AddBaseView(doc, pos, scale, ViewOrientationTypeEnum.kIsoTopRightViewOrientation, req.ViewStyle);
                }
                catch { }

                try
                {
                    return (DrawingView)views.AddBaseView(doc, pos, scale, ViewOrientationTypeEnum.kIsoTopRightViewOrientation, req.ViewStyle, null, null, null);
                }
                catch { }

                warnings.Add("Failed to create ISO view (no compatible AddBaseView overload found).");
                return null;
            }
            catch (Exception ex)
            {
                warnings.Add("Failed to create ISO view: " + ex.Message);
                return null;
            }
        }

        private static ViewOrientationTypeEnum ToViewOrientation(BaseOrientation o) => o switch
        {
            BaseOrientation.Top => ViewOrientationTypeEnum.kTopViewOrientation,
            BaseOrientation.Right => ViewOrientationTypeEnum.kRightViewOrientation,
            _ => ViewOrientationTypeEnum.kFrontViewOrientation
        };

        private static void TrySetName(DrawingView view, string name)
        {
            try { view.Name = name; } catch { }
        }

        private static void TryClearViews(Sheet sheet)
        {
            try
            {
                var views = sheet.DrawingViews;
                int count = 0;
                try { count = views.Count; } catch { return; }

                for (int i = count; i >= 1; i--)
                {
                    try { views[i].Delete(); } catch { }
                }
            }
            catch { }
        }
    }
}
