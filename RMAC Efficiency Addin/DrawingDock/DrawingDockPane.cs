using Inventor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DAlign = System.Drawing.ContentAlignment;
using DColor = System.Drawing.Color;
using DPen = System.Drawing.Pen;
using IOFile = System.IO.File;
using WF = System.Windows.Forms;
using IOPath = System.IO.Path;
using IODir = System.IO.Directory;
using RMAC_Efficiency_Addin.PartNumbering;
using RMAC_Efficiency_Addin.DrawingDock.Services;
using RMAC_Efficiency_Addin.Exporting;
using RMAC_Efficiency_Addin.Settings;

namespace RMAC_Efficiency_Addin.DrawingDock
{
    public sealed class DrawingDockPane : WF.UserControl
    {
        private readonly Application _app;
        private const string MasterAsmPropName = "RMAC_MasterAssemblyFullFileName";
        private bool _assemblyLocked;
        private string? _lockedDrawingKey;

        // -----------------------------
        // Part Numbering
        // -----------------------------
        private readonly WF.TextBox _txtAssembly;
        private readonly WF.Button _btnSelectAssembly;
        private readonly WF.Button _btnRenumber;
        private readonly WF.Button _btnCheckComments;
        private readonly WF.Label _lblStatus;

        // -----------------------------
        // Services (DrawingDock split)
        // -----------------------------
        private readonly DrawingContextResolver _ctxResolver;
        private readonly SelectionService _selectionSvc;
        private readonly BomTraversalService _bomSvc;
        private readonly ViewPlacementService _viewPlacer;
        private readonly DrawingDocumentLocator _drawingLocator;

        // NEW: automation services extracted out of pane
        private readonly SheetRenamerService _sheetRenamer = new SheetRenamerService();
        private readonly PartsListSortService _partsListSort = new PartsListSortService();
        private readonly ViewCoverageService _viewCoverage = new ViewCoverageService();

        // Keep as object to avoid Inventor PIA inheritance mismatches (AssemblyDocument -> Document/_Document)
        private object? _selectedAssembly;

        // -----------------------------
        // Drawing Insert
        // -----------------------------
        private enum InsertMode { Standard, General }
        private InsertMode _mode = InsertMode.Standard;

        private DrawingDocument? _drawing;
        private AssemblyDocument? _asmDoc;

        // Active list used by Insert/Flat/Next/Prev
        private List<KeyValuePair<string, ComponentDefinition>>? _sortedParts;
        private int _currentPartIndex;

        // Controls (Drawing Insert section)
        private WF.Button _btnModeStandard = null!;
        private WF.Button _btnModeGeneral = null!;
        private WF.Button _btnLoadBom = null!;        // Standard load
        private WF.Button _btnUpdateGen = null!;      // General update
        private WF.Label _lblCurPart = null!;
        private WF.Label _lblCurComment = null!;
        private WF.Button _btnInsert = null!;
        private WF.Button _btnFlat = null!;
        private WF.Button _btnNext = null!;
        private WF.Button _btnPrev = null!;
        private WF.Button _btnNewSheet = null!;
        private WF.Button _btnRenameSheets = null!;
        private WF.Button _btnSortBoms = null!;
        private WF.Button _btnCheckViews = null!;
        private WF.Button _btnExport = null!;

        // Regex for general parts: GEN-P-XXX
        private static readonly Regex RxGenPart = new Regex(@"^GEN-P-(\d{3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Comments to skip for view coverage checks (Parts Only BOM)
        private HashSet<string> _skipComments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public DrawingDockPane(Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));

            _ctxResolver = new DrawingContextResolver(_app);
            _selectionSvc = new SelectionService(_app);
            _bomSvc = new BomTraversalService();
            _viewPlacer = new ViewPlacementService(_app);
            _drawingLocator = new DrawingDocumentLocator(_app);

            Dock = WF.DockStyle.Fill;

            // Theme (match your other dock panes)
            var bg = DColor.FromArgb(45, 45, 48);
            var panelBg = DColor.FromArgb(37, 37, 38);
            var border = DColor.FromArgb(62, 62, 64);
            var text = DColor.Gainsboro;
            var fieldBg = DColor.FromArgb(30, 30, 30);
            var btnBg = DColor.FromArgb(63, 63, 70);

            var root = new WF.Panel
            {
                Dock = WF.DockStyle.Fill,
                Padding = new WF.Padding(8),
                BackColor = bg,
                AutoScroll = true
            };

            var main = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = bg
            };
            main.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 330)); // Part numbering
            main.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100));  // Tools

            // ============================================================
            // Group 1: RMAC PART NUMBERING
            // ============================================================
            var grpPartNo = new WF.GroupBox
            {
                Dock = WF.DockStyle.Fill,
                Text = "RMAC PART NUMBERING",
                ForeColor = text,
                BackColor = bg,
                Padding = new WF.Padding(10, 12, 10, 10)
            };

            var pnLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = bg
            };
            pnLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 18)); // label
            pnLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 36)); // textbox frame
            pnLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 10)); // spacer
            pnLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 120)); // buttons
            pnLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 10)); // spacer
            pnLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100)); // filler

            var lblAsm = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                Text = "Selected assembly:",
                ForeColor = text,
                BackColor = bg,
                TextAlign = DAlign.MiddleLeft
            };

            var asmFrame = MakeFramedPanel(panelBg, border);

            _txtAssembly = new WF.TextBox
            {
                Dock = WF.DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = WF.BorderStyle.None,
                BackColor = fieldBg,
                ForeColor = text
            };
            asmFrame.Controls.Add(_txtAssembly);

            var pnButtons = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = bg
            };
            pnButtons.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34));
            pnButtons.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34));
            pnButtons.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34));

            _btnSelectAssembly = MakeButton("Load Assembly", border, text, btnBg);
            _btnSelectAssembly.Width = 220;
            _btnSelectAssembly.Anchor = WF.AnchorStyles.None;
            _btnSelectAssembly.Click += (_, __) => SelectAssemblyFromDrawingView();

            _btnRenumber = MakeButton("Renumber", border, text, btnBg);
            _btnRenumber.Width = 220;
            _btnRenumber.Anchor = WF.AnchorStyles.None;
            _btnRenumber.Click += (_, __) => RmacPartNumberingRenumberer.Run(_app, _selectedAssembly);

            _btnCheckComments = MakeButton("Check Comments", border, text, btnBg);
            _btnCheckComments.Width = 220;
            _btnCheckComments.Anchor = WF.AnchorStyles.None;
            _btnCheckComments.Click += (_, __) => RunCheckComments();

            pnButtons.Controls.Add(_btnSelectAssembly, 0, 0);
            pnButtons.Controls.Add(_btnRenumber, 0, 1);
            pnButtons.Controls.Add(_btnCheckComments, 0, 2);

            pnLayout.Controls.Add(lblAsm, 0, 0);
            pnLayout.Controls.Add(asmFrame, 0, 1);
            pnLayout.Controls.Add(pnButtons, 0, 3);

            grpPartNo.Controls.Add(pnLayout);

            // ============================================================
            // Group 2: PART INSERTION
            // ============================================================
            var grpTools = new WF.GroupBox
            {
                Dock = WF.DockStyle.Fill,
                Text = "PART INSERTION",
                ForeColor = text,
                BackColor = bg,
                Padding = new WF.Padding(10, 12, 10, 10)
            };

            // Part insertion (core) layout
            var toolLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 11,
                BackColor = bg
            };

            toolLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34)); // mode
            toolLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34)); // load/update
            toolLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 18)); // part caption
            toolLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 40)); // part label
            toolLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 18)); // comment caption
            toolLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 40)); // comment label
            toolLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34)); // insert
            toolLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34)); // flat
            toolLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34)); // nav
            toolLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34)); // next sheet
            toolLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100)); // status

            // Mode buttons
            var modePanel = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = bg
            };
            modePanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 50));
            modePanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 50));

            _btnModeStandard = MakeButton("Standard Parts", border, text, btnBg);
            _btnModeGeneral = MakeButton("General Parts", border, text, btnBg);
            _btnModeStandard.Dock = WF.DockStyle.Fill;
            _btnModeGeneral.Dock = WF.DockStyle.Fill;

            _btnModeStandard.Click += (_, __) => SetMode(InsertMode.Standard);
            _btnModeGeneral.Click += (_, __) => SetMode(InsertMode.General);

            modePanel.Controls.Add(_btnModeStandard, 0, 0);
            modePanel.Controls.Add(_btnModeGeneral, 1, 0);

            // Load/Update (same cell, toggled by SetMode)
            var loadPanel = new WF.Panel { Dock = WF.DockStyle.Fill, BackColor = bg };

            _btnLoadBom = MakeButton("Load BOM from current drawing (View 1)", border, text, btnBg);
            _btnLoadBom.Dock = WF.DockStyle.Fill;
            _btnLoadBom.Click += (_, __) => LoadBomFromCurrentDrawing();

            _btnUpdateGen = MakeButton("Update General Parts List (GEN-P-XXX)", border, text, btnBg);
            _btnUpdateGen.Dock = WF.DockStyle.Fill;
            _btnUpdateGen.Click += (_, __) => LoadGenFromSelectedAssemblyPartsOnly();

            loadPanel.Controls.Add(_btnLoadBom);
            loadPanel.Controls.Add(_btnUpdateGen);

            var lblPartCaption = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                Text = "Current Part Number:",
                ForeColor = text,
                BackColor = bg,
                TextAlign = DAlign.MiddleLeft
            };

            var partFrame = MakeFramedPanel(panelBg, border);
            _lblCurPart = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                Text = "<not loaded>",
                BackColor = fieldBg,
                ForeColor = text,
                TextAlign = DAlign.MiddleCenter
            };
            partFrame.Controls.Add(_lblCurPart);

            var lblCommentCaption = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                Text = "Comments:",
                ForeColor = text,
                BackColor = bg,
                TextAlign = DAlign.MiddleLeft
            };

            var commentFrame = MakeFramedPanel(panelBg, border);
            _lblCurComment = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                Text = "",
                BackColor = fieldBg,
                ForeColor = text,
                TextAlign = DAlign.MiddleCenter
            };
            commentFrame.Controls.Add(_lblCurComment);

            _btnInsert = MakeButton("Insert", border, text, btnBg);
            _btnInsert.Dock = WF.DockStyle.Fill;
            _btnInsert.Click += (_, __) => InsertCurrentPartBaseView();

            _btnFlat = MakeButton("Flat Pattern", border, text, btnBg);
            _btnFlat.Dock = WF.DockStyle.Fill;
            _btnFlat.Click += (_, __) => InsertCurrentPartFlatPatternView();

            var navPanel = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = bg
            };
            navPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 50));
            navPanel.ColumnStyles.Add(new WF.ColumnStyle(WF.SizeType.Percent, 50));

            _btnPrev = MakeButton("Previous", border, text, btnBg);
            _btnNext = MakeButton("Next", border, text, btnBg);
            _btnPrev.Dock = WF.DockStyle.Fill;
            _btnNext.Dock = WF.DockStyle.Fill;

            _btnPrev.Click += (_, __) => PrevPart();
            _btnNext.Click += (_, __) => NextPart();

            navPanel.Controls.Add(_btnPrev, 0, 0);
            navPanel.Controls.Add(_btnNext, 1, 0);

            _btnNewSheet = MakeButton("Next Sheet", border, text, btnBg);
            _btnNewSheet.Dock = WF.DockStyle.Fill;
            _btnNewSheet.Click += (_, __) => AddNextPartsSheet();

            // Status
            var statusFrame = MakeFramedPanel(panelBg, border);
            _lblStatus = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                Text = "",
                BackColor = fieldBg,
                ForeColor = text,
                TextAlign = DAlign.TopLeft
            };
            statusFrame.Controls.Add(_lblStatus);

            toolLayout.Controls.Add(modePanel, 0, 0);
            toolLayout.Controls.Add(loadPanel, 0, 1);
            toolLayout.Controls.Add(lblPartCaption, 0, 2);
            toolLayout.Controls.Add(partFrame, 0, 3);
            toolLayout.Controls.Add(lblCommentCaption, 0, 4);
            toolLayout.Controls.Add(commentFrame, 0, 5);
            toolLayout.Controls.Add(_btnInsert, 0, 6);
            toolLayout.Controls.Add(_btnFlat, 0, 7);
            toolLayout.Controls.Add(navPanel, 0, 8);
            toolLayout.Controls.Add(_btnNewSheet, 0, 9);
            toolLayout.Controls.Add(statusFrame, 0, 10);

            // ============================================================
            // DRAWING AUTOMATION group
            // ============================================================
            _btnRenameSheets = MakeButton("Rename Sheets", border, text, btnBg);
            _btnRenameSheets.Dock = WF.DockStyle.Fill;
            _btnRenameSheets.Click += (_, __) => RunRenameSheets();

            _btnSortBoms = MakeButton("Sort BOMs (Parts Lists)", border, text, btnBg);
            _btnSortBoms.Dock = WF.DockStyle.Fill;
            _btnSortBoms.Click += (_, __) => RunSortBoms();

            _btnCheckViews = MakeButton("Check Views Coverage", border, text, btnBg);
            _btnCheckViews.Dock = WF.DockStyle.Fill;
            _btnCheckViews.Click += (_, __) => RunViewCoverage();

            var grpAutomation = new WF.GroupBox
            {
                Dock = WF.DockStyle.Fill,
                Text = "DRAWING AUTOMATION",
                ForeColor = text,
                BackColor = bg,
                Padding = new WF.Padding(10, 12, 10, 10)
            };

            var autoLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = bg
            };
            autoLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34));
            autoLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34));
            autoLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 34));

            autoLayout.Controls.Add(_btnRenameSheets, 0, 0);
            autoLayout.Controls.Add(_btnSortBoms, 0, 1);
            autoLayout.Controls.Add(_btnCheckViews, 0, 2);

            grpAutomation.Controls.Add(autoLayout);

            // ============================================================
            // EXPORT group
            // ============================================================
            _btnExport = MakeButton("Export", border, text, btnBg);
            _btnExport.Dock = WF.DockStyle.Fill;
            _btnExport.Click += (_, __) =>
            {
                if (_app.ActiveDocument is not DrawingDocument dd)
                {
                    WF.MessageBox.Show("Open a Drawing document first.");
                    return;
                }

                try { Enabled = false; } catch { }

                try
                {
                    GlobalExporter.Run(_app, dd, _selectedAssembly, msg =>
                    {
                        try { _lblStatus.Text = msg; _lblStatus.Refresh(); } catch { }
                    });
                }
                finally
                {
                    try { Enabled = true; } catch { }
                }
            };

            var grpExport = new WF.GroupBox
            {
                Dock = WF.DockStyle.Fill,
                Text = "EXPORT",
                ForeColor = text,
                BackColor = bg,
                Padding = new WF.Padding(10, 12, 10, 10)
            };

            var exportLayout = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                BackColor = bg
            };
            exportLayout.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100));
            exportLayout.Controls.Add(_btnExport, 0, 0);
            grpExport.Controls.Add(exportLayout);

            // ============================================================
            // Stack: Part Insertion + Drawing Automation + Export
            // ============================================================
            var toolsContainer = new WF.TableLayoutPanel
            {
                Dock = WF.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = bg
            };
            toolsContainer.RowStyles.Add(new WF.RowStyle(WF.SizeType.Percent, 100));  // Part insertion
            toolsContainer.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 150)); // automation
            toolsContainer.RowStyles.Add(new WF.RowStyle(WF.SizeType.Absolute, 85));  // export

            toolsContainer.Controls.Add(toolLayout, 0, 0);
            toolsContainer.Controls.Add(grpAutomation, 0, 1);
            toolsContainer.Controls.Add(grpExport, 0, 2);

            grpTools.Controls.Add(toolsContainer);

            main.Controls.Add(grpPartNo, 0, 0);
            main.Controls.Add(grpTools, 0, 1);

            root.Controls.Add(main);
            Controls.Add(root);

            // Init
            RefreshSelectedAssemblyFromCurrentContext();
            SetMode(InsertMode.Standard);
            SetInsertUiEnabled(false);
        }

        private static WF.Panel MakeFramedPanel(DColor fill, DColor border)
        {
            var p = new WF.Panel
            {
                Dock = WF.DockStyle.Fill,
                Padding = new WF.Padding(6),
                BackColor = fill
            };

            p.Paint += (s, e) =>
            {
                using var pen = new DPen(border);
                var r = p.ClientRectangle;
                r.Width -= 1;
                r.Height -= 1;
                e.Graphics.DrawRectangle(pen, r);
            };

            return p;
        }

        private static WF.Button MakeButton(string text, DColor border, DColor fore, DColor back)
        {
            var btn = new WF.Button
            {
                Text = text,
                Height = 28,
                BackColor = back,
                ForeColor = fore,
                FlatStyle = WF.FlatStyle.Flat
            };

            btn.FlatAppearance.BorderColor = border;
            btn.FlatAppearance.BorderSize = 1;
            return btn;
        }

        // ============================================================
        // Mode handling
        // ============================================================
        private void SetMode(InsertMode mode)
        {
            _mode = mode;

            _sortedParts = null;
            _currentPartIndex = 0;
            _lblCurPart.Text = "<not loaded>";
            _lblCurComment.Text = "";
            SetInsertUiEnabled(false);

            _btnLoadBom.Visible = mode == InsertMode.Standard;
            _btnUpdateGen.Visible = mode == InsertMode.General;

            _btnModeStandard.Enabled = mode != InsertMode.Standard;
            _btnModeGeneral.Enabled = mode != InsertMode.General;

            _lblStatus.Text = mode == InsertMode.Standard
                ? "Mode: Standard Parts. Use 'Load BOM from current drawing'."
                : "Mode: General Parts. Use 'Update General Parts List'.";
        }

        // ============================================================
        // Part Numbering section
        // ============================================================
        public void RefreshSelectedAssemblyFromCurrentContext()
        {
            try
            {
                var ctx = _ctxResolver.GetContext();
                if (ctx.IsValidDrawingContext && ctx.DrawingDocument != null)
                {
                    string key = GetDrawingKey(ctx.DrawingDocument);

                    if (!string.Equals(_lockedDrawingKey, key, StringComparison.OrdinalIgnoreCase))
                    {
                        _assemblyLocked = false;
                        _lockedDrawingKey = key;
                    }

                    if (!_assemblyLocked)
                    {
                        var storedAsm = TryLoadLockedAssemblyFromDrawingProperty(ctx.DrawingDocument);
                        if (storedAsm != null)
                        {
                            SetSelectedAssembly(storedAsm, "Assembly loaded from drawing iProperty (master assembly).");
                            _assemblyLocked = true;
                            return;
                        }
                    }

                    if (_assemblyLocked && _selectedAssembly is AssemblyDocument)
                    {
                        _lblStatus.Text = "Using locked master assembly (won't auto-change).";
                        return;
                    }

                    var inferred = TryGetAssemblyFromSelectSet(ctx.DrawingDocument);
                    if (inferred != null)
                    {
                        SetSelectedAssembly(inferred, "Assembly inferred from current drawing selection.");
                        return;
                    }

                    SetSelectedAssembly(null, "No assembly inferred. Use 'Load Assembly' to pick a drawing view.");
                    return;
                }

                if (_app.ActiveDocument is AssemblyDocument ad)
                {
                    if (!_assemblyLocked)
                        SetSelectedAssembly(ad, "Active document is an assembly.");
                    return;
                }

                if (!_assemblyLocked)
                    SetSelectedAssembly(null, "Open a Drawing document to use this tool.");
            }
            catch
            {
                if (!_assemblyLocked)
                    SetSelectedAssembly(null, "Unable to read current context.");
            }
        }

        private void SelectAssemblyFromDrawingView()
        {
            try
            {
                if (_app.ActiveDocument is not DrawingDocument)
                {
                    WF.MessageBox.Show("Open a Drawing document to select an assembly from a drawing view.");
                    return;
                }

                object picked = _app.CommandManager.Pick(
                    SelectionFilterEnum.kDrawingViewFilter,
                    "Select a drawing view that references the target assembly."
                );

                if (picked is not DrawingView dv)
                {
                    WF.MessageBox.Show("No drawing view was selected.");
                    return;
                }

                var asm = TryGetAssemblyFromDrawingView(dv);
                if (asm == null)
                {
                    WF.MessageBox.Show("That view does not reference an Assembly document.");
                    return;
                }

                SetSelectedAssembly(asm, "Assembly selected from drawing view.");
                LockSelectedAssemblyForDrawing((DrawingDocument)_app.ActiveDocument, asm, persistToIProperty: true);
                _lblStatus.Text = "Master assembly locked for this drawing (save the drawing to persist).";
            }
            catch (COMException)
            {
                // cancelled
            }
            catch (Exception ex)
            {
                WF.MessageBox.Show(ex.Message, "Load Assembly - Error");
            }
        }

        private void SetSelectedAssembly(object? asmDoc, string status)
        {
            _selectedAssembly = asmDoc;

            _txtAssembly.Text = asmDoc == null
                ? "<none>"
                : GetPropertyValueSafe(asmDoc, "Design Tracking Properties", "Part Number") ?? "<NO PART NUMBER>";

            _lblStatus.Text = status;
        }

        private void RunCheckComments()
        {
            try
            {
                if (_selectedAssembly is not AssemblyDocument asm)
                {
                    WF.MessageBox.Show(
                        "No assembly selected in the Part Numbering section.\n\nUse 'Load Assembly' first.",
                        "Check Comments"
                    );
                    return;
                }

                CommentsChecker.Run(_app, asm);
            }
            catch (Exception ex)
            {
                WF.MessageBox.Show(ex.Message, "Check Comments - Error");
            }
        }

        private AssemblyDocument? TryGetAssemblyFromSelectSet(DrawingDocument dd)
        {
            try
            {
                var sel = _selectionSvc.GetSelectionSnapshot();
                var dv = SelectionService.TryResolveDrawingView(sel);

                if (dv != null)
                {
                    var asm = TryGetAssemblyFromDrawingView(dv);
                    if (asm != null) return asm;
                }

                try
                {
                    foreach (object o in dd.SelectSet)
                    {
                        if (o is DrawingView dv2)
                        {
                            var asm2 = TryGetAssemblyFromDrawingView(dv2);
                            if (asm2 != null) return asm2;
                        }
                    }
                }
                catch { }
            }
            catch { }

            return null;
        }

        internal static AssemblyDocument? TryGetAssemblyFromDrawingView(DrawingView dv)
        {
            try
            {
                var desc = dv.ReferencedDocumentDescriptor;
                if (desc == null) return null;

                object refDocObj = desc.ReferencedDocument;
                return refDocObj as AssemblyDocument;
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        // Drawing Insert
        // ============================================================
        private void SetInsertUiEnabled(bool enabled)
        {
            _btnInsert.Enabled = enabled;
            _btnFlat.Enabled = enabled;
            _btnNext.Enabled = enabled;
            _btnPrev.Enabled = enabled;
            _btnNewSheet.Enabled = enabled;
        }

        private void LoadBomFromCurrentDrawing()
        {
            try
            {
                var ctx = _ctxResolver.GetContext();
                if (!ctx.IsValidDrawingContext || ctx.DrawingDocument == null || ctx.ActiveSheet == null)
                {
                    WF.MessageBox.Show("Open a Drawing document first.");
                    return;
                }

                if (ctx.ActiveSheet.DrawingViews.Count == 0)
                {
                    WF.MessageBox.Show("ERR-001 - No views found on the active sheet.", "Error");
                    return;
                }

                DrawingView view1 = ctx.ActiveSheet.DrawingViews[1];
                var refDoc = view1.ReferencedDocumentDescriptor?.ReferencedDocument;

                if (refDoc is not AssemblyDocument asm)
                {
                    WF.MessageBox.Show("ERR-003 - The primary view is not an assembly", "Error");
                    return;
                }

                _drawing = ctx.DrawingDocument;
                _asmDoc = asm;

                var entries = _bomSvc.GetBomEntries(
                    asm,
                    BomTraversalService.BomViewKind.Structured,
                    new BomTraversalService.Options
                    {
                        StructuredFirstLevelOnly = true,
                        DeduplicateByPartNumber = true
                    });

                _sortedParts = entries
                    .OrderBy(e => e.PartNumber, StringComparer.OrdinalIgnoreCase)
                    .Select(e => new KeyValuePair<string, ComponentDefinition>(e.PartNumber, e.ComponentDefinition))
                    .ToList();

                _currentPartIndex = 0;

                UpdateCurrentPartLabels();
                SetInsertUiEnabled(_sortedParts.Count > 0);

                _lblStatus.Text = $"Standard list loaded: {_sortedParts.Count} unique part numbers.";
            }
            catch (COMException)
            {
                WF.MessageBox.Show("Inventor COM error while loading BOM. Ensure the drawing view references an accessible assembly.");
            }
            catch (Exception ex)
            {
                WF.MessageBox.Show(ex.Message, "Load BOM - Error");
            }
        }

        private void LoadGenFromSelectedAssemblyPartsOnly()
        {
            try
            {
                var ctx = _ctxResolver.GetContext();
                if (!ctx.IsValidDrawingContext || ctx.DrawingDocument == null)
                {
                    WF.MessageBox.Show("Open a Drawing document first.");
                    return;
                }

                var asm = _selectedAssembly as AssemblyDocument;
                if (asm == null)
                {
                    WF.MessageBox.Show("No assembly selected in the Part Numbering section.\n\nUse 'Load Assembly' first.", "General Parts");
                    return;
                }

                _drawing = ctx.DrawingDocument;

                var entries = _bomSvc.GetBomEntries(
                    asm,
                    BomTraversalService.BomViewKind.PartsOnly,
                    new BomTraversalService.Options
                    {
                        EnablePartsOnlyView = true,
                        DeduplicateByPartNumber = true,
                        PartNumberFilter = pn => RxGenPart.IsMatch(pn)
                    });

                _sortedParts = entries
                    .OrderBy(e => e.PartNumber, StringComparer.OrdinalIgnoreCase)
                    .Select(e => new KeyValuePair<string, ComponentDefinition>(e.PartNumber, e.ComponentDefinition))
                    .ToList();

                _currentPartIndex = 0;

                UpdateCurrentPartLabels();
                SetInsertUiEnabled(_sortedParts.Count > 0);

                _lblStatus.Text = $"General list loaded: {_sortedParts.Count} GEN parts.";
            }
            catch (Exception ex)
            {
                WF.MessageBox.Show(ex.Message, "General Parts - Error");
            }
        }

        private void UpdateCurrentPartLabels()
        {
            if (_sortedParts == null || _sortedParts.Count == 0)
            {
                _lblCurPart.Text = "<not loaded>";
                _lblCurComment.Text = "";
                SetInsertUiEnabled(false);
                return;
            }

            _lblCurPart.Text = _sortedParts[_currentPartIndex].Key;

            var cd = _sortedParts[_currentPartIndex].Value;
            _lblCurComment.Text = GetPropertyValueSafe(cd.Document, "Inventor Summary Information", "Comments")
                                  ?? "No comment available";
        }

        private void NextPart()
        {
            if (_sortedParts == null || _sortedParts.Count == 0) return;

            if (_currentPartIndex < _sortedParts.Count - 1)
            {
                _currentPartIndex++;
                UpdateCurrentPartLabels();
            }
            else
            {
                WF.MessageBox.Show("Already at the last part.", "Navigation", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Information);
            }
        }

        private void PrevPart()
        {
            if (_sortedParts == null || _sortedParts.Count == 0) return;

            if (_currentPartIndex > 0)
            {
                _currentPartIndex--;
                UpdateCurrentPartLabels();
            }
            else
            {
                WF.MessageBox.Show("Already at the first part.", "Navigation", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Information);
            }
        }

        private void InsertCurrentPartBaseView()
        {
            try
            {
                if (_sortedParts == null || _sortedParts.Count == 0)
                {
                    WF.MessageBox.Show("Load a list first.");
                    return;
                }

                var ctx = _ctxResolver.GetContext();
                if (!ctx.IsValidDrawingContext || ctx.DrawingDocument == null || ctx.ActiveSheet == null)
                {
                    WF.MessageBox.Show("Open a Drawing document first.");
                    return;
                }

                var modelDoc = _sortedParts[_currentPartIndex].Value.Document as Document;
                if (modelDoc == null)
                {
                    WF.MessageBox.Show("Could not resolve part document reference for base view insertion.");
                    return;
                }

                var res = _viewPlacer.Place(new ViewPlacementService.PlaceViewsRequest
                {
                    Drawing = ctx.DrawingDocument,
                    Sheet = ctx.ActiveSheet,
                    ModelDocument = modelDoc,

                    StartX = -10,
                    StartY = 10,

                    CreateTop = false,
                    CreateRight = false,
                    CreateIso = false,

                    BaseViewOrientation = ViewPlacementService.BaseOrientation.Front,
                    ViewStyle = DrawingViewStyleEnum.kHiddenLineDrawingViewStyle,

                    Scale = 1.0,
                    ClearExistingViews = false
                });

                if (!res.Success)
                {
                    WF.MessageBox.Show("Error inserting view:\n" + string.Join("\n", res.Warnings), "Error",
                        WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                WF.MessageBox.Show("Error inserting view: " + ex.Message, "Error",
                    WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Error);
            }
        }

        private void InsertCurrentPartFlatPatternView()
        {
            try
            {
                if (_drawing == null || _sortedParts == null || _sortedParts.Count == 0)
                {
                    WF.MessageBox.Show("Load a list first.");
                    return;
                }

                if (!ReferenceEquals(_drawing, _app.ActiveDocument))
                {
                    WF.MessageBox.Show("Please reload the BOM first.", "RMAC", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Warning);
                    return;
                }

                var partDoc = _sortedParts[_currentPartIndex].Value.Document as PartDocument;
                if (partDoc == null)
                {
                    WF.MessageBox.Show("Error: Selected document is not a valid part.", "Error", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Error);
                    return;
                }

                var smcd = partDoc.ComponentDefinition as SheetMetalComponentDefinition;
                if (smcd == null)
                {
                    WF.MessageBox.Show("Error: Selected part is not a Sheet Metal part.", "Error", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Error);
                    return;
                }

                var sheet = _drawing.ActiveSheet;

                var tgeom = _app.TransientGeometry;
                var pt = tgeom.CreatePoint2d(-10, 10);

                var options = _app.TransientObjects.CreateNameValueMap();
                options.Add("SheetMetalFoldedModel", false);

                if (!smcd.HasFlatPattern)
                    smcd.Unfold();

                sheet.DrawingViews.AddBaseView(
                    (_Document)partDoc,
                    pt,
                    1,
                    ViewOrientationTypeEnum.kDefaultViewOrientation,
                    DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle,
                    null,
                    null,
                    options);
            }
            catch (Exception ex)
            {
                WF.MessageBox.Show("Error inserting Flat Pattern: " + ex.Message, "Error", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Error);
            }
        }

        private void AddNextPartsSheet()
        {
            try
            {
                if (_drawing == null)
                {
                    WF.MessageBox.Show("Open a drawing and load a list first.");
                    return;
                }

                if (!ReferenceEquals(_drawing, _app.ActiveDocument))
                {
                    WF.MessageBox.Show("Please reload the BOM first.", "RMAC", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Warning);
                    return;
                }

                if (_asmDoc == null)
                {
                    WF.MessageBox.Show("Load the Standard BOM first to create Parts sheets.", "Next Sheet");
                    return;
                }

                string title = GetPropertyValueSafe(_asmDoc, "Design Tracking Properties", "Part Number") ?? _asmDoc.DisplayName;
                int nextIndex = _drawing.Sheets.Count + 1;

                AddPartsSheet(_drawing, title, nextIndex);
            }
            catch (Exception ex)
            {
                WF.MessageBox.Show("Error adding new sheet: " + ex.Message, "Error", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Error);
            }
        }

        private void AddPartsSheet(DrawingDocument drawing, string titleNo, int sheetCount)
        {
            var activeSheet = drawing.ActiveSheet;

            var size = activeSheet.Size;
            var orientation = activeSheet.Orientation;
            var width = activeSheet.Width;
            var height = activeSheet.Height;

            string newSheetName = $"{titleNo} Parts {sheetCount}";
            var newSheet = drawing.Sheets.Add(size, orientation, newSheetName, width, height);

            try
            {
                var activeBorder = activeSheet.Border;
                var activeBorderDef = activeBorder?.Definition;

                if (activeBorderDef != null)
                    newSheet.AddBorder(activeBorderDef);
            }
            catch { }

            var tbDef = ResolveTitleBlockDefinition(
                drawing,
                AddinSettings.Current.DrawingPartsTitleBlockName,
                fallbackName: "RMAC Parts Title");

            if (tbDef != null)
                newSheet.AddTitleBlock(tbDef);
        }

        private static TitleBlockDefinition? ResolveTitleBlockDefinition(DrawingDocument drawing, string? preferredName, string? fallbackName)
        {
            try
            {
                if (drawing == null) return null;

                var pref = (preferredName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(pref))
                {
                    foreach (TitleBlockDefinition def in drawing.TitleBlockDefinitions)
                        if (string.Equals(def.Name, pref, StringComparison.OrdinalIgnoreCase))
                            return def;
                }

                var fb = (fallbackName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(fb))
                {
                    foreach (TitleBlockDefinition def in drawing.TitleBlockDefinitions)
                        if (string.Equals(def.Name, fb, StringComparison.OrdinalIgnoreCase))
                            return def;
                }

                try
                {
                    if (drawing.TitleBlockDefinitions.Count > 0)
                        return drawing.TitleBlockDefinitions[1];
                }
                catch { }

                return null;
            }
            catch { return null; }
        }

        // ============================================================
        // Rename Sheets via SheetRenamerService
        // ============================================================
        private void RunRenameSheets()
        {
            if (_app.ActiveDocument is not DrawingDocument dd)
            {
                WF.MessageBox.Show("Open a Drawing document first.");
                return;
            }

            var res = _sheetRenamer.Run(dd, new SheetRenamerService.Options
            {
                PartsTitleBlockName = AddinSettings.Current.DrawingPartsTitleBlockName,
                EnableLog = true,
                ReorderSheets = true,
                OrderingMode = AddinSettings.Current.SheetOrderingMode
            });

            _lblStatus.Text = res.Status;

            if (!res.Success)
            {
                WF.MessageBox.Show(res.Status, "Rename Sheets - Error",
                    WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Error);
                return;
            }

            if (res.Warnings.Count > 0)
            {
                WF.MessageBox.Show(
                    "Rename Sheets completed with warnings:\n\n" +
                    string.Join("\n", res.Warnings.Take(40)) +
                    (res.Warnings.Count > 40 ? $"\n... ({res.Warnings.Count - 40} more)" : ""),
                    "Rename Sheets",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Warning
                );
            }
        }

        // ============================================================
        // Sort BOMs via PartsListSortService
        // ============================================================
        private void RunSortBoms()
        {
            if (_app.ActiveDocument is not DrawingDocument dd)
            {
                WF.MessageBox.Show("Open a Drawing document first.");
                return;
            }

            var res = _partsListSort.Run(dd, new PartsListSortService.Options
            {
                SortColumnContainsText = "Part Number",
                SortAscending = true,
                OnlyAssemblyPartsLists = true,
                RenumberAfterSort = true
            });

            _lblStatus.Text = $"Sort BOMs complete. Updated {res.Processed} parts lists. Skipped {res.Skipped}.";

            if (!res.Success)
            {
                WF.MessageBox.Show(
                    "Sort BOMs failed:\n\n" + string.Join("\n", res.Warnings.Take(20)),
                    "Sort BOMs - Error",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Error
                );
                return;
            }

            if (res.Warnings.Count > 0)
            {
                WF.MessageBox.Show(
                    "Sort BOMs completed with warnings:\n\n" +
                    string.Join("\n", res.Warnings.Take(40)) +
                    (res.Warnings.Count > 40 ? $"\n... ({res.Warnings.Count - 40} more)" : ""),
                    "Sort BOMs",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Warning
                );
            }
        }

        // ============================================================
        // View Coverage via ViewCoverageService
        // ============================================================
        private void RunViewCoverage()
        {
            LoadViewCoverageSkipComments();

            if (_app.ActiveDocument is not DrawingDocument dd)
            {
                WF.MessageBox.Show("Open a Drawing document first.");
                return;
            }

            var asm = _selectedAssembly as AssemblyDocument;
            if (asm == null)
            {
                WF.MessageBox.Show("No assembly selected in the Part Numbering section.\n\nUse 'Load Assembly' first.", "Check Views Coverage");
                return;
            }

            var res = _viewCoverage.Run(dd, asm, new ViewCoverageService.Options
            {
                IncludeStructuredTopLevel = true,
                IncludePartsOnly = true,
                StructuredFirstLevelOnly = true,
                EnablePartsOnlyView = true,
                SkipComments = _skipComments,
                IncludeBlankPartNumbers = true
            });

            if (!res.Success)
            {
                WF.MessageBox.Show(
                    "View coverage check failed:\n\n" + string.Join("\n", res.Warnings.Take(20)),
                    "Check Views Coverage - Error",
                    WF.MessageBoxButtons.OK,
                    WF.MessageBoxIcon.Error);
                return;
            }

            if (res.MissingPartNumbers.Count == 0)
            {
                WF.MessageBox.Show("All required items have drawing views.", "Check Views Coverage");
                return;
            }

            string reportText = "The following items do not have drawing views:\n\n" +
                                string.Join("\n", res.MissingPartNumbers);

            try { WF.Clipboard.SetText(reportText); } catch { }

            WF.MessageBox.Show(
                reportText + "\n\n(Also copied to clipboard)",
                "Check Views Coverage",
                WF.MessageBoxButtons.OK,
                WF.MessageBoxIcon.Warning
            );
        }

        // ============================================================
        // Shared property helper
        // ============================================================
        private static string? GetPropertyValueSafe(object docOrDefWithProps, string propSetName, string propName)
        {
            try
            {
                dynamic d = docOrDefWithProps;
                var ps = d.PropertySets[propSetName];
                var p = ps[propName];
                object v = p.Value;
                return v?.ToString();
            }
            catch
            {
                return null;
            }
        }

        // ============================================================
        // Master assembly lock
        // ============================================================
        private static string GetDrawingKey(DrawingDocument dd)
        {
            try
            {
                string fn = dd.FullFileName;
                if (!string.IsNullOrWhiteSpace(fn)) return fn;
            }
            catch { }
            return dd.DisplayName ?? "<unsaved drawing>";
        }

        private AssemblyDocument? TryLoadLockedAssemblyFromDrawingProperty(DrawingDocument dd)
        {
            string? path = GetUserDefinedPropertyValueSafe(dd, MasterAsmPropName);
            if (string.IsNullOrWhiteSpace(path)) return null;

            // 1) Already open?
            try
            {
                foreach (Document d in _app.Documents)
                {
                    try
                    {
                        if (string.Equals(d.FullFileName, path, StringComparison.OrdinalIgnoreCase) && d is AssemblyDocument ad)
                            return ad;
                    }
                    catch { }
                }
            }
            catch { }

            // 2) File exists on disk?
            if (!IOFile.Exists(path)) return null;

            // 3) Open invisibly
            try
            {
                var doc = _app.Documents.Open(path, false);
                return doc as AssemblyDocument;
            }
            catch
            {
                return null;
            }
        }

        private void LockSelectedAssemblyForDrawing(DrawingDocument dd, AssemblyDocument asm, bool persistToIProperty)
        {
            _assemblyLocked = true;
            _lockedDrawingKey = GetDrawingKey(dd);

            if (persistToIProperty)
            {
                try
                {
                    SetUserDefinedPropertyString(dd, MasterAsmPropName, asm.FullFileName);
                }
                catch { }
            }
        }

        private static string? GetUserDefinedPropertyValueSafe(object doc, string propName)
        {
            try
            {
                dynamic d = doc;
                var ps = d.PropertySets["Inventor User Defined Properties"];
                var p = ps[propName];
                object v = p.Value;
                return v?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static void SetUserDefinedPropertyString(object doc, string propName, string value)
        {
            dynamic d = doc;
            var ps = d.PropertySets["Inventor User Defined Properties"];

            try
            {
                var p = ps[propName];
                p.Value = value;
            }
            catch
            {
                ps.Add(value, propName);
            }
        }

        private void LoadViewCoverageSkipComments()
        {
            try
            {
                AddinSettings.EnsureLoaded();
                var opts = AddinSettings.Current.CommentsOptions;
                opts.SanitizeInPlace();

                _skipComments = new HashSet<string>(
                    (opts.SkipCommentsForViewCoverage ?? Enumerable.Empty<string>())
                        .Select(x => (x ?? "").Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase
                );
            }
            catch
            {
                _skipComments = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FASTENERS", "FASTENER" };
            }
        }


    }
}
