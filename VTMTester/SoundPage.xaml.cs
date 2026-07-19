using VTMControls.DeviceControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VTMBase;

namespace VTMTester
{
    // Sound Processing page. ROIs + FFT/dB/colormap live in ONE global config on the model
    // (Model.SoundConfig). EDIT mode edits the global ROIs/params; CHECK mode picks which ROIs each
    // SND CHECK step verifies (stored in the step's Condition2); DIAG mode mirrors a running test.
    public partial class SoundPage : Page
    {
        private Program _program;
        // DIAG mode only: the SND step currently being mirrored (so we can show its name + map its ROI results).
        private Step _currentStep;

        // Global sound config on the model - shared ROIs + FFT/dB/colormap (no more per-step config).
        private SoundStepConfig Cfg
        {
            get
            {
                var m = _program?.EditModel;
                if (m == null) return null;
                if (m.SoundConfig == null) m.SoundConfig = new SoundStepConfig();
                return m.SoundConfig;
            }
        }

        // 3 modes (ROIs are global on the model - not tied to any step):
        //  Edit  = editable: create/move/resize the global ROIs, capture templates, edit params.
        //  Check = readonly bench view (like ZEROC pen-off): START realtime scoring; left-click designates
        //          ROIs (transient StopChecked) so the stop-on-pass flag freezes capture when they all match.
        //  Diag  = readonly, passively mirror an auto/manual SND test (snapshot + ROI pass/fail + step name).
        private enum PageMode { Edit, Check, Diag }
        private PageMode Mode
        {
            get
            {
                if (rbModeCheck?.IsChecked == true) return PageMode.Check;
                if (rbModeDiag?.IsChecked == true) return PageMode.Diag;
                return PageMode.Edit;
            }
        }
        private bool StepMode { get { return Mode == PageMode.Edit; } }
        private bool CheckMode { get { return Mode == PageMode.Check; } }
        private bool DiagMode { get { return Mode == PageMode.Diag; } }

        public Program Program
        {
            get { return _program; }
            set
            {
                if (value == null || value == _program) return;
                if (_program?.SoundTester != null)
                {
                    _program.SoundTester.CaptureStarted -= SoundTester_CaptureStarted;
                    _program.SoundTester.CaptureStopped -= SoundTester_CaptureStopped;
                }
                if (_program != null) _program.SoundStepStarted -= Program_SoundStepStarted;
                _program = value;
                if (_program?.SoundTester != null)
                {
                    _program.SoundTester.CaptureStarted += SoundTester_CaptureStarted;
                    _program.SoundTester.CaptureStopped += SoundTester_CaptureStopped;
                    UpdateCaptureUi(_program.SoundTester.IsCapturing);
                }
                if (_program != null) _program.SoundStepStarted += Program_SoundStepStarted;
                RefreshDeviceLabel();
                RefreshStepList();
                EnsureTestWatch();   // always poll ResultSeq to push autotest snapshot into view when DIAG
                Mode_Changed(null, null);   // set initial visibility per default mode (EDIT)
            }
        }

        // Auto/manual test starts an SND step. FULLY ISOLATED: a running test only feeds DIAG (its own layer
        // + its own _diagPass/_diagScore). EDIT/CHECK are never touched - their ROI data/state stays frozen.
        private void Program_SoundStepStarted(object sender, Step testStep)
        {
            if (testStep == null) return;
            Dispatcher.Invoke(() =>
            {
                if (!DiagMode) return;   // do NOT touch EDIT/CHECK state in any way
                _currentStep = testStep;   // remember the running SND step (name + ROI mapping for the snapshot)
                ShowDiagStepName(testStep);
            });
        }

        // DIAG: show the name of the SND step being mirrored (step content + No + mode).
        private void ShowDiagStepName(Step step)
        {
            if (txtDiagStep == null) return;
            if (step == null) { txtDiagStep.Text = "(waiting for an SND test step...)"; return; }
            string name = string.IsNullOrWhiteSpace(step.TestContent) ? "(no name)" : step.TestContent;
            txtDiagStep.Text = $"#{step.No}  {name}   [{(step.Oper ?? "").Trim().ToUpperInvariant()}]";
        }

        private readonly DispatcherTimer _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };

        // True only while the USER is manually capturing on this page (pressed START). A test running SND
        // also starts the shared SoundTester, but EDIT/CHECK must ignore that - only DIAG mirrors a test.
        private bool _userCapture;

        // ROI editor state (model ported from ZEROC SoundCheckPage)
        // SoundRoi.X0/Y0/X1/Y1 normalized 0..1; may invert while dragging, normalized on MouseUp.
        private readonly Dictionary<SoundRoi, System.Windows.Shapes.Rectangle> _roiRects = new Dictionary<SoundRoi, System.Windows.Shapes.Rectangle>();
        private readonly Dictionary<SoundRoi, TextBlock> _roiLabels = new Dictionary<SoundRoi, TextBlock>();
        private SoundRoi _selectedRoi;
        private System.Windows.Shapes.Rectangle[] _handles;   // 4 corner handles of the selected ROI

        private enum RoiDrag { None, Move, Resize }
        private RoiDrag _dragMode = RoiDrag.None;
        private int _resizeCorner;                    // 0=TL 1=TR 2=BL 3=BR
        private double _dnx, _dny, _oX0, _oY0, _oX1, _oY1;   // mousedown anchor + original box
        private const double HandleHitPx = 11;

        // Snapshot of the global SoundConfig at load time, used for Revert (undo unsaved changes)
        private string _stepSavedJson;
        private string _selectedColormap = "Hot";

        public SoundPage()
        {
            InitializeComponent();
            _renderTimer.Tick += RenderTimer_Tick;
            roiCanvas.MouseLeftButtonDown += RoiCanvas_MouseDown;
            roiCanvas.MouseMove += RoiCanvas_MouseMove;
            roiCanvas.MouseLeftButtonUp += RoiCanvas_MouseUp;
            roiCanvas.MouseRightButtonDown += RoiCanvas_MouseRight;
            roiCanvas.SizeChanged += (s, e) => UpdateRois();
            PreviewKeyDown += SoundPage_KeyDown;
            Focusable = true;
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        private static bool Inside(SoundRoi r, double nx, double ny)
        {
            return nx >= Math.Min(r.X0, r.X1) && nx <= Math.Max(r.X0, r.X1)
                && ny >= Math.Min(r.Y0, r.Y1) && ny <= Math.Max(r.Y0, r.Y1);
        }
        private static bool Near(Point p, double x, double y) { return Math.Abs(p.X - x) <= HandleHitPx && Math.Abs(p.Y - y) <= HandleHitPx; }
        // Handle index: 0=TL 1=TR 2=BL 3=BR (corners) | 4=Top 5=Bottom 6=Left 7=Right (edges). -1 = none.
        private int CornerHit(SoundRoi r, Point p, double w, double h)
        {
            double x0 = Math.Min(r.X0, r.X1) * w, x1 = Math.Max(r.X0, r.X1) * w;
            double y0 = Math.Min(r.Y0, r.Y1) * h, y1 = Math.Max(r.Y0, r.Y1) * h;
            double mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
            // Corners first (priority)
            if (Near(p, x0, y0)) return 0;
            if (Near(p, x1, y0)) return 1;
            if (Near(p, x0, y1)) return 2;
            if (Near(p, x1, y1)) return 3;
            // Edge midpoints
            if (Near(p, mx, y0)) return 4;   // top
            if (Near(p, mx, y1)) return 5;   // bottom
            if (Near(p, x0, my)) return 6;   // left
            if (Near(p, x1, my)) return 7;   // right
            return -1;
        }
        private void StoreOrig(SoundRoi r) { _oX0 = Math.Min(r.X0, r.X1); _oX1 = Math.Max(r.X0, r.X1); _oY0 = Math.Min(r.Y0, r.Y1); _oY1 = Math.Max(r.Y0, r.Y1); }

        // ROI geometry (create/move/resize/capture) is edited only in EDIT mode; ROIs are global.
        // CHECK mode uses left-click to toggle which ROIs the selected SND CHECK step verifies.
        private bool CanEditRoi { get { return StepMode; } }

        // ---------- Mouse: double-click = new/clone, corner = resize, inside = move, empty = deselect ----------
        private void RoiCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            double w = roiCanvas.ActualWidth, h = roiCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;
            roiCanvas.Focus();
            var p = e.GetPosition(roiCanvas);
            double nx = Clamp01(p.X / w), ny = Clamp01(p.Y / h);

            // CHECK mode: left-click a ROI designates it (✓) for the stop-on-pass flag (ZEROC-style, transient).
            if (CheckMode)
            {
                DesignateRoiAt(nx, ny);
                e.Handled = true;
                return;
            }

            if (!CanEditRoi) return;

            // Double-click: on a box -> clone; empty area -> create new centered box
            if (e.ClickCount == 2)
            {
                SoundRoi hit = null;
                var rois = Cfg?.Rois;
                if (rois != null)
                    for (int i = rois.Count - 1; i >= 0; i--) if (Inside(rois[i], nx, ny)) { hit = rois[i]; break; }
                if (hit != null) CloneRoi(hit);
                else
                {
                    const double hw = 0.09, hh = 0.09;
                    var neo = NewRoi(Clamp01(nx - hw), Clamp01(ny - hh), Clamp01(nx + hw), Clamp01(ny + hh));
                    SelectRoi(neo);
                    txtInfo.Text = "ROI added (double-click empty). Drag inside = move, corner = resize.";
                }
                UpdateRois();
                e.Handled = true;
                return;
            }

            // 1) grab a corner of the selected ROI -> resize
            if (_selectedRoi != null)
            {
                int c = CornerHit(_selectedRoi, p, w, h);
                if (c >= 0) { _dragMode = RoiDrag.Resize; _resizeCorner = c; StoreOrig(_selectedRoi); roiCanvas.CaptureMouse(); return; }
            }
            // 2) inside a ROI (topmost) -> select + move
            var list = Cfg?.Rois;
            if (list != null)
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (Inside(list[i], nx, ny))
                    {
                        SelectRoi(list[i]); _dragMode = RoiDrag.Move; _dnx = nx; _dny = ny; StoreOrig(list[i]);
                        roiCanvas.CaptureMouse(); UpdateRois(); return;
                    }
                }
            // 3) empty area -> deselect (no box creation by dragging)
            SelectRoi(null); UpdateRois();
        }

        // CHECK mode: left-click designates the clicked ROI (✓) for the stop-on-pass flag. Transient, global,
        // NOT tied to any step (like ZEROC's right-click "StopChecked" tick). Click empty area = deselect.
        private void DesignateRoiAt(double nx, double ny)
        {
            var rois = Cfg?.Rois;
            if (rois == null || rois.Count == 0) { txtInfo.Text = "No ROIs - add them in EDIT mode first"; return; }

            SoundRoi hit = null;
            for (int i = rois.Count - 1; i >= 0; i--) if (Inside(rois[i], nx, ny)) { hit = rois[i]; break; }
            if (hit == null) { SelectRoi(null); UpdateRois(); return; }

            hit.StopChecked = !hit.StopChecked;
            int idx1 = rois.IndexOf(hit) + 1;
            SelectRoi(hit);
            UpdateRois();
            txtInfo.Text = hit.StopChecked
                ? $"ROI #{idx1} designated (stop-on-pass will wait for it)"
                : $"ROI #{idx1} un-designated";
        }

        private void RoiCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_dragMode == RoiDrag.None || _selectedRoi == null) return;
            double w = roiCanvas.ActualWidth, h = roiCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var p = e.GetPosition(roiCanvas);
            double nx = Clamp01(p.X / w), ny = Clamp01(p.Y / h);

            if (_dragMode == RoiDrag.Move)
            {
                double wRoi = _oX1 - _oX0, hRoi = _oY1 - _oY0;
                double x0 = _oX0 + (nx - _dnx); if (x0 < 0) x0 = 0; if (x0 + wRoi > 1) x0 = 1 - wRoi;
                double y0 = _oY0 + (ny - _dny); if (y0 < 0) y0 = 0; if (y0 + hRoi > 1) y0 = 1 - hRoi;
                _selectedRoi.X0 = x0; _selectedRoi.X1 = x0 + wRoi; _selectedRoi.Y0 = y0; _selectedRoi.Y1 = y0 + hRoi;
            }
            else if (_dragMode == RoiDrag.Resize)
            {
                switch (_resizeCorner)
                {
                    case 0: _selectedRoi.X0 = nx; _selectedRoi.Y0 = ny; break;   // TL
                    case 1: _selectedRoi.X1 = nx; _selectedRoi.Y0 = ny; break;   // TR
                    case 2: _selectedRoi.X0 = nx; _selectedRoi.Y1 = ny; break;   // BL
                    case 3: _selectedRoi.X1 = nx; _selectedRoi.Y1 = ny; break;   // BR
                    case 4: _selectedRoi.Y0 = ny; break;                         // top edge  (vertical only)
                    case 5: _selectedRoi.Y1 = ny; break;                         // bottom edge
                    case 6: _selectedRoi.X0 = nx; break;                         // left edge (horizontal only)
                    case 7: _selectedRoi.X1 = nx; break;                         // right edge
                }
            }
            RescoreAndRefresh();   // NCC follows the ROI while moving/resizing (like ZEROC)
        }

        // Re-score the ROIs against the last available spectrogram columns, then redraw.
        // Lets the NCC update while moving/resizing even when the live capture is frozen.
        private void RescoreAndRefresh()
        {
            if (_liveColsForScore != null) ScoreRoisLive(_liveColsForScore);
            UpdateRois();
        }

        private void RoiCanvas_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_dragMode == RoiDrag.None) return;
            roiCanvas.ReleaseMouseCapture();
            if (_selectedRoi != null)   // normalize X0<X1, Y0<Y1
            {
                double x0 = Math.Min(_selectedRoi.X0, _selectedRoi.X1), x1 = Math.Max(_selectedRoi.X0, _selectedRoi.X1);
                double y0 = Math.Min(_selectedRoi.Y0, _selectedRoi.Y1), y1 = Math.Max(_selectedRoi.Y0, _selectedRoi.Y1);
                _selectedRoi.X0 = x0; _selectedRoi.X1 = x1; _selectedRoi.Y0 = y0; _selectedRoi.Y1 = y1;
            }
            _dragMode = RoiDrag.None;
            UpdateRois();
        }

        // Right-click on the selected box -> delete; outside -> deselect; double-right-click -> delete all.
        private void RoiCanvas_MouseRight(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!CanEditRoi) return;
            if (e.ClickCount == 2) { ClearRois(); e.Handled = true; return; }
            double w = roiCanvas.ActualWidth, h = roiCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var p = e.GetPosition(roiCanvas);
            double nx = Clamp01(p.X / w), ny = Clamp01(p.Y / h);
            if (_selectedRoi != null && Inside(_selectedRoi, nx, ny)) RemoveRoi(_selectedRoi);
            SelectRoi(null);
            UpdateRois();
            e.Handled = true;
        }

        // F12 = help; arrow keys nudge the selected ROI (edit mode; ignored while typing in a text box).
        private void SoundPage_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F12)
            {
                helpOverlay.Visibility = helpOverlay.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                e.Handled = true; return;
            }
            if (e.Key == System.Windows.Input.Key.Delete && _selectedRoi != null && CanEditRoi)
            {
                RemoveRoi(_selectedRoi); SelectRoi(null); UpdateRois(); e.Handled = true; return;
            }
            if (_selectedRoi == null || !CanEditRoi) return;
            if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
            double step = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 ? 0.02 : 0.004;
            double dx = 0, dy = 0;
            switch (e.Key)
            {
                case System.Windows.Input.Key.Left: dx = -step; break;
                case System.Windows.Input.Key.Right: dx = step; break;
                case System.Windows.Input.Key.Up: dy = -step; break;
                case System.Windows.Input.Key.Down: dy = step; break;
                default: return;
            }
            var r = _selectedRoi;
            double bx0 = Math.Min(r.X0, r.X1), bx1 = Math.Max(r.X0, r.X1);
            double by0 = Math.Min(r.Y0, r.Y1), by1 = Math.Max(r.Y0, r.Y1);
            double wRoi = bx1 - bx0, hRoi = by1 - by0;
            double nX0 = bx0 + dx; if (nX0 < 0) nX0 = 0; if (nX0 + wRoi > 1) nX0 = 1 - wRoi;
            double nY0 = by0 + dy; if (nY0 < 0) nY0 = 0; if (nY0 + hRoi > 1) nY0 = 1 - hRoi;
            r.X0 = nX0; r.X1 = nX0 + wRoi; r.Y0 = nY0; r.Y1 = nY0 + hRoi;
            RescoreAndRefresh();   // NCC follows the nudged ROI
            e.Handled = true;
        }

        // ---------- ROI CRUD ----------
        private SoundRoi NewRoi(double x0, double y0, double x1, double y1)
        {
            var rect = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)), StrokeThickness = 1.5,
                Fill = Brushes.Transparent   // no fill by default; UpdateRois fills only the chosen ROI
            };
            var label = new TextBlock { Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
            roiCanvas.Children.Add(rect);
            roiCanvas.Children.Add(label);
            var roi = new SoundRoi
            {
                Name = "roi" + ((Cfg?.Rois?.Count ?? 0) + 1),
                X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Min = 0.30
            };
            Cfg?.Rois?.Add(roi);
            _roiRects[roi] = rect;
            _roiLabels[roi] = label;
            return roi;
        }

        private void CloneRoi(SoundRoi src)
        {
            if (src == null || Cfg == null) return;
            double x0 = Math.Min(src.X0, src.X1), x1 = Math.Max(src.X0, src.X1);
            double y0 = Math.Min(src.Y0, src.Y1), y1 = Math.Max(src.Y0, src.Y1);
            double hw = (x1 - x0) / 2, hh = (y1 - y0) / 2;
            var roi = NewRoi(0.5 - hw, 0.5 - hh, 0.5 + hw, 0.5 + hh);
            roi.Min = src.Min; roi.Max = src.Max;
            roi.Tpl = src.Tpl?.ToArray(); roi.TplWidth = src.TplWidth; roi.TplHeight = src.TplHeight;
            SelectRoi(roi);
            txtInfo.Text = "ROI cloned to center";
        }

        private void RemoveRoi(SoundRoi roi)
        {
            if (roi == null) return;
            if (_roiRects.TryGetValue(roi, out var rect)) { roiCanvas.Children.Remove(rect); _roiRects.Remove(roi); }
            if (_roiLabels.TryGetValue(roi, out var lb)) { roiCanvas.Children.Remove(lb); _roiLabels.Remove(roi); }
            Cfg?.Rois?.Remove(roi);
            if (_selectedRoi == roi) _selectedRoi = null;
            txtInfo.Text = "ROI deleted";
        }

        private void ClearRois()
        {
            var rois = Cfg?.Rois;
            if (rois == null) return;
            foreach (var r in rois.ToList()) RemoveRoi(r);
            SelectRoi(null);
            UpdateRois();
            txtInfo.Text = "All ROIs cleared";
        }

        private void SelectRoi(SoundRoi roi)
        {
            _selectedRoi = roi;
            RefreshSelRoiUi();
        }

        // Rebuild all visuals from the step config (called when CurrentStep changes).
        private void RepaintRois()
        {
            foreach (var kv in _roiRects) roiCanvas.Children.Remove(kv.Value);
            foreach (var kv in _roiLabels) roiCanvas.Children.Remove(kv.Value);
            _roiRects.Clear();
            _roiLabels.Clear();
            _selectedRoi = null;

            var cfg = Cfg;
            if (cfg?.Rois == null) return;
            foreach (var roi in cfg.Rois)
            {
                var rect = new System.Windows.Shapes.Rectangle { Stroke = Brushes.Red, StrokeThickness = 2, Fill = Brushes.Transparent };
                var label = new TextBlock { Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
                roiCanvas.Children.Add(rect);
                roiCanvas.Children.Add(label);
                _roiRects[roi] = rect;
                _roiLabels[roi] = label;
            }
            UpdateRois();
        }

        private void EnsureHandles()
        {
            if (_handles != null) return;
            _handles = new System.Windows.Shapes.Rectangle[8];   // 4 corners + 4 edge midpoints
            for (int i = 0; i < 8; i++)
            {
                _handles[i] = new System.Windows.Shapes.Rectangle
                {
                    Width = 9, Height = 9, Fill = Brushes.White,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x85)), StrokeThickness = 1,
                    Visibility = Visibility.Collapsed, IsHitTestVisible = false
                };
                roiCanvas.Children.Add(_handles[i]);
            }
        }
        private void PlaceHandle(int i, double x, double y) { Canvas.SetLeft(_handles[i], x - 4.5); Canvas.SetTop(_handles[i], y - 4.5); }

        // Reposition + recolor + label + handles every frame (like ZEROC UpdateRois).
        // PASS/FAIL score only computed in CHECK mode when a template exists (edit mode is neutral yellow).
        private void UpdateRois()
        {
            if (roiCanvas == null) return;
            double w = roiCanvas.ActualWidth, h = roiCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;
            EnsureHandles();

            var cfg = Cfg;
            bool editMode = StepMode;
            // In edit mode ROIs always show; otherwise the Eye toggle controls visibility.
            // DIAG shows nothing until a running test has pushed a snapshot.
            bool show = editMode || (chkRoiVis?.IsChecked == true);
            if (DiagMode && !_diagHasSnapshot) show = false;

            double maxHz = cfg?.MaxHz > 0 ? cfg.MaxHz : 8000;

            // CHECK mode: designated ROIs (✓, StopChecked) get a gold border; the stop-on-pass flag waits for them.
            bool designateMode = CheckMode;

            if (cfg?.Rois != null)
            {
                int riCounter = 0;
                foreach (var roi in cfg.Rois)
                {
                    int idx1 = ++riCounter;   // 1-based, matches cfg.Rois order (shown as #index)
                    if (!_roiRects.TryGetValue(roi, out var rect)) continue;
                    var label = _roiLabels.TryGetValue(roi, out var lb) ? lb : null;
                    if (!show) { rect.Visibility = Visibility.Collapsed; if (label != null) label.Visibility = Visibility.Collapsed; continue; }
                    rect.Visibility = Visibility.Visible;
                    if (label != null) label.Visibility = Visibility.Visible;

                    double x0 = Math.Min(roi.X0, roi.X1), x1 = Math.Max(roi.X0, roi.X1);
                    double y0 = Math.Min(roi.Y0, roi.Y1), y1 = Math.Max(roi.Y0, roi.Y1);
                    double rx = x0 * w, ry = y0 * h;
                    Canvas.SetLeft(rect, rx); Canvas.SetTop(rect, ry);
                    rect.Width = (x1 - x0) * w; rect.Height = (y1 - y0) * h;

                    bool sel = roi == _selectedRoi;
                    // Green only when the ROI passes; otherwise RED - including neutral (no data / no template
                    // / not scored yet). No yellow, no gray.
                    Color stroke;
                    if (roi.LastPass == true)
                        stroke = Color.FromRgb(0x39, 0xD9, 0x8A);
                    else
                        stroke = Color.FromRgb(0xE7, 0x4C, 0x3C);
                    rect.Stroke = new SolidColorBrush(stroke);
                    rect.StrokeThickness = sel ? 3.0 : 1.8;
                    // No fill by default (border only). Only the "chosen" ROI gets a faint fill in the border
                    //  color (green/red) - kept very translucent so the spectrogram underneath stays visible.
                    //  CHECK mode -> the designated ROI (left-clicked); EDIT mode -> the selected ROI.
                    bool chosen = designateMode ? roi.StopChecked : sel;
                    rect.Fill = chosen
                        ? new SolidColorBrush(Color.FromArgb(34, stroke.R, stroke.G, stroke.B))
                        : System.Windows.Media.Brushes.Transparent;

                    if (label != null)
                    {
                        double fTop = (1 - y0) * maxHz, fBot = (1 - y1) * maxHz;
                        string sc = roi.LastScore.HasValue ? $"  NCC {roi.LastScore.Value * 100:F0}%" : (roi.Tpl != null ? "  NCC --" : "  no tpl");
                        label.Text = $"#{idx1} {fBot:F0}-{fTop:F0}Hz{sc} [>={roi.Min * 100:F0}%]";
                        Canvas.SetLeft(label, rx + 3);
                        Canvas.SetTop(label, ry - 15 < 0 ? ry + 2 : ry - 15);
                    }
                }
            }

            // Live NCC readout for the selected ROI (top toolbar)
            if (txtRoiScore != null)
            {
                if (_selectedRoi?.LastScore != null)
                {
                    double s = _selectedRoi.LastScore.Value;
                    txtRoiScore.Text = $"{s * 100:F0}%";
                    bool pass = _selectedRoi.LastPass == true;
                    txtRoiScore.Foreground = pass ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38));
                    if (ncBorder != null) ncBorder.Background = new SolidColorBrush(pass ? Color.FromRgb(0x39, 0xD9, 0x8A) : Color.FromRgb(0xFF, 0xCC, 0x80));
                }
                else
                {
                    txtRoiScore.Text = "--";
                    txtRoiScore.Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38));
                    if (ncBorder != null) ncBorder.Background = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC));
                }
            }

            // Corner + edge handles of the selected ROI (only when it can be edited: EDIT or CHECK)
            if (_selectedRoi != null && show && CanEditRoi)
            {
                foreach (var hd in _handles) { roiCanvas.Children.Remove(hd); roiCanvas.Children.Add(hd); }
                double sx0 = Math.Min(_selectedRoi.X0, _selectedRoi.X1) * w, sx1 = Math.Max(_selectedRoi.X0, _selectedRoi.X1) * w;
                double sy0 = Math.Min(_selectedRoi.Y0, _selectedRoi.Y1) * h, sy1 = Math.Max(_selectedRoi.Y0, _selectedRoi.Y1) * h;
                double mx = (sx0 + sx1) / 2, my = (sy0 + sy1) / 2;
                PlaceHandle(0, sx0, sy0); PlaceHandle(1, sx1, sy0); PlaceHandle(2, sx0, sy1); PlaceHandle(3, sx1, sy1);  // corners
                PlaceHandle(4, mx, sy0);  PlaceHandle(5, mx, sy1);  PlaceHandle(6, sx0, my);  PlaceHandle(7, sx1, my);  // edges
                foreach (var hd in _handles) hd.Visibility = Visibility.Visible;
            }
            else foreach (var hd in _handles) hd.Visibility = Visibility.Collapsed;
        }

        // Live sliding columns kept for position-fixed ROI scoring (ROI lights green when the buzzer
        // scrolls into its box and the patch under it matches the template).
        private List<float[]> _liveColsForScore;

        // DIAG: true once a running test has pushed a snapshot. Until then DIAG shows nothing.
        private bool _diagHasSnapshot;
        // DIAG's own per-ROI results (indexed like SoundConfig.Rois) - kept separate from the live
        // roi.LastPass/LastScore so switching to/from DIAG doesn't wipe the edit/check scores.
        private bool?[] _diagPass;
        private double?[] _diagScore;

        private void RenderTimer_Tick(object sender, EventArgs e)
        {
            // DIAG never renders live capture (it only shows the test snapshot). EDIT/CHECK render only the
            // USER's own capture, never a test-driven one.
            if (DiagMode || !_userCapture) return;

            var tester = _program?.SoundTester;
            if (tester == null || !tester.IsCapturing) return;
            RenderSpectrogram(tester);
        }

        private WriteableBitmap _specBmp;   // reuse buffer, avoid allocating each frame
        private byte[] _specBuf;
        private void RenderSpectrogram(SoundTester tester)
        {
            if (Cfg == null) return;
            var cfg = Cfg;

            SaveToStep();
            // Incremental: FFT only new columns -> no lag
            var cols = tester.PollLiveColumns(cfg);
            if (cols.Count == 0) return;
            _liveColsForScore = cols;   // keep for UpdateRois live scoring

            int W = SoundTester.Cols;   // fixed width window
            int H = SoundTester.Bins;
            int r = Math.Min(cols.Count, W);
            int displayStart = W - r;    // right-aligned, black-pad on the left

            if (_specBmp == null) { _specBmp = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgra32, null); _specBuf = new byte[W * H * 4]; specImage.Source = _specBmp; }
            var buf = _specBuf;
            Array.Clear(buf, 0, buf.Length);   // black pad
            for (int x = 0; x < r; x++)
            {
                var col = cols[cols.Count - r + x];   // newest r cols
                int destX = displayStart + x;
                for (int y = 0; y < H; y++)
                {
                    float v = y < col.Length ? col[y] : 0f;
                    ColorFromValue(v, out byte rr, out byte gg, out byte bb);
                    int idx = (y * W + destX) * 4;
                    buf[idx + 0] = bb;
                    buf[idx + 1] = gg;
                    buf[idx + 2] = rr;
                    buf[idx + 3] = 255;
                }
            }
            _specBmp.WritePixels(new Int32Rect(0, 0, W, H), buf, W * 4, 0);

            // Position-fixed live scoring: score each ROI where it sits, color green on match.
            ScoreRoisLive(cols);
            UpdateRois();

            // Stop-on-pass (flag): only in CHECK mode. Freeze capture the instant the designated ROIs all match.
            if (CheckMode && chkStopOnPass?.IsChecked == true && AllRoisPass())
            {
                _program?.SoundTester?.Stop();
                txtInfo.Text = "Stopped on match (designated ROIs pass).";
            }
        }

        // True when the ROIs to watch have templates and currently pass. If the user designated ROIs (✓ via
        // left-click in CHECK mode) only those count; otherwise every ROI must pass (ZEROC-style).
        private bool AllRoisPass()
        {
            var rois = Cfg?.Rois;
            if (rois == null || rois.Count == 0) return false;
            bool anyDesignated = rois.Any(r => r.StopChecked);
            bool any = false;
            foreach (var r in rois)
            {
                if (anyDesignated && !r.StopChecked) continue;   // only the designated ones gate the stop
                any = true;
                if (r.Tpl == null) return false;                 // no template -> can't be "matched"
                if (r.LastPass != true) return false;
            }
            return any;
        }

        // Score each ROI at its FIXED display position (no tau-slide) against its captured template.
        // Mirrors ZEROC UpdateRois: ROI lights green only when the content currently under it matches.
        private void ScoreRoisLive(List<float[]> cols)
        {
            var rois = Cfg?.Rois;
            if (rois == null || cols == null || cols.Count == 0) return;
            int Cols = SoundTester.Cols;
            int count = Math.Min(cols.Count, Cols);
            int displayStart = Cols - count;
            int baseIdx = cols.Count - count;
            foreach (var roi in rois)
            {
                if (roi.Tpl == null) { roi.LastPass = null; roi.LastScore = null; continue; }
                var patch = ExtractPatchFixed(roi, cols, baseIdx, count, displayStart);
                if (patch == null) { roi.LastPass = false; roi.LastScore = 0; continue; }
                double sc = Ncc(patch, roi.Tpl);
                roi.LastScore = sc;
                roi.LastPass = sc >= roi.Min;
            }
        }

        // Resample the ROI region to a TplH x TplW patch at its fixed display position.
        private float[] ExtractPatchFixed(SoundRoi roi, List<float[]> cols, int baseIdx, int count, int displayStart)
        {
            int Cols = SoundTester.Cols, bins = SoundTester.Bins, tplW = SoundTester.TplW, tplH = SoundTester.TplH;
            double x0 = Math.Min(roi.X0, roi.X1), x1 = Math.Max(roi.X0, roi.X1);
            double y0 = Math.Min(roi.Y0, roi.Y1), y1 = Math.Max(roi.Y0, roi.Y1);
            int rowA = (int)(y0 * bins), rowB = (int)(y1 * bins);
            if (rowA < 0) rowA = 0; if (rowB > bins) rowB = bins; if (rowB <= rowA) rowB = Math.Min(bins, rowA + 1);
            int colA = -1, colB = -1;
            for (int i = 0; i < count; i++)
            {
                double xf = (displayStart + i) / (double)Cols;
                if (xf >= x0 && xf <= x1) { if (colA < 0) colA = i; colB = i; }
            }
            if (colA < 0) return null;
            int rows = rowB - rowA, wcols = colB - colA + 1;
            var patch = new float[tplH * tplW];
            for (int ty = 0; ty < tplH; ty++)
            {
                int sr = rowA + (int)((ty + 0.5) / tplH * rows); if (sr >= bins) sr = bins - 1;
                for (int tx = 0; tx < tplW; tx++)
                {
                    int ci = colA + (int)((tx + 0.5) / tplW * wcols); if (ci >= count) ci = count - 1;
                    var mg = cols[baseIdx + ci];
                    patch[ty * tplW + tx] = (mg != null && sr < mg.Length) ? mg[sr] : 0f;
                }
            }
            return patch;
        }

        // Normalized cross-correlation of two equal-length patches -> 0..1 (negative clamped to 0).
        private static double Ncc(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length || a.Length == 0) return 0;
            int n = a.Length; double ma = 0, mb = 0;
            for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
            ma /= n; mb /= n;
            double num = 0, da = 0, db = 0;
            for (int i = 0; i < n; i++) { double x = a[i] - ma, y = b[i] - mb; num += x * y; da += x * x; db += y * y; }
            double den = Math.Sqrt(da * db);
            if (den <= 1e-9) return 0;
            double rr = num / den;
            return rr < 0 ? 0 : (rr > 1 ? 1 : rr);
        }

        // Colormap Hot / Viridis / Magma / Turbo / Jet / Grayscale
        private void ColorFromValue(float v, out byte r, out byte g, out byte b)
        {
            if (v < 0) v = 0; else if (v > 1) v = 1;
            switch (_selectedColormap)
            {
                case "Viridis":
                    // approx: dark purple -> teal -> yellow
                    r = (byte)(255 * (0.267 + v * (0.993 - 0.267)));
                    g = (byte)(255 * (0.005 + v * (0.906 - 0.005)));
                    b = (byte)(255 * (0.329 + v * (0.144 - 0.329)));
                    break;
                case "Magma":
                    // dark purple -> orange -> yellow
                    r = (byte)(255 * (0.001 + v * (0.988 - 0.001)));
                    g = (byte)(255 * (0.000 + v * (0.998 - 0.000)));
                    b = (byte)(255 * (0.014 + v * (0.641 - 0.014)));
                    break;
                case "Turbo":
                    // blue -> green -> red
                    r = (byte)(Math.Max(0, Math.Min(255, 34 + 380 * (v - 0.5))));
                    g = (byte)(Math.Max(0, Math.Min(255, 200 * (1 - Math.Abs(v - 0.5) * 2))));
                    b = (byte)(Math.Max(0, Math.Min(255, 200 * (1 - v * 2))));
                    break;
                case "Jet":
                    r = (byte)(Math.Max(0, Math.Min(255, 255 * (v > 0.5 ? 1.0 : (v - 0.35) * 4))));
                    g = (byte)(Math.Max(0, Math.Min(255, 255 * (1 - Math.Abs(v - 0.5) * 2))));
                    b = (byte)(Math.Max(0, Math.Min(255, 255 * (v < 0.5 ? 1.0 : (0.65 - v) * 4))));
                    break;
                case "Grayscale":
                    r = g = b = (byte)(255 * v);
                    break;
                case "Hot":
                default:
                    // black -> red -> yellow -> white
                    r = (byte)(Math.Min(255, 3 * v * 255));
                    g = (byte)(Math.Max(0, Math.Min(255, (3 * v - 1) * 255)));
                    b = (byte)(Math.Max(0, Math.Min(255, (3 * v - 2) * 255)));
                    break;
            }
        }

        // Reload the page from the model: model name + global sound config (params + ROIs).
        public void RefreshStepList()
        {
            RefreshModelName();
            LoadFromStep();          // load the global SoundConfig (FFT/dB/colormap + ROIs) into the UI
        }

        private void RefreshDeviceLabel()
        {
            var name = _program?.appSetting?.Communication?.MicrophoneName;
            if (string.IsNullOrEmpty(name)) name = "(default microphone)";
            txtDevice.Text = name;
        }

        // Show the currently open model name (file name without extension, or the model Name).
        private void RefreshModelName()
        {
            if (txtModelName == null) return;
            var model = _program?.EditModel;
            string n = null;
            if (model != null)
            {
                if (!string.IsNullOrEmpty(model.Path)) n = System.IO.Path.GetFileNameWithoutExtension(model.Path);
                if (string.IsNullOrEmpty(n)) n = model.Name;
            }
            txtModelName.Text = string.IsNullOrEmpty(n) ? "(no model)" : n;
        }

        // Load UI from the GLOBAL model sound config (FFT/dB/colormap + shared ROIs).
        private void LoadFromStep()
        {
            var cfg = Cfg;
            if (cfg == null) { if (txtInfo != null) txtInfo.Text = "No model loaded"; return; }

            SelectComboByString(cboFft, cfg.FftSize.ToString());
            txtMaxHz.Text = cfg.MaxHz.ToString();
            txtDbFloor.Text = cfg.DbFloor.ToString();
            txtDbTop.Text = cfg.DbTop.ToString();
            cfg.Metric = "Template";   // fixed to Template
            // ColorMap is global (not per-step) - taken from appSetting
            var globalColor = _program?.appSetting?.SpectrogramColorMap ?? "Hot";
            SelectComboByString(cboColor, globalColor);
            _selectedColormap = globalColor;
            chkStopOnPass.IsChecked = cfg.StopOnPass;
            txtInfo.Text = $"Global ROIs: {cfg.Rois.Count}  Template mode";
            _selectedRoi = null;
            RefreshSelRoiUi();

            // Snapshot for Revert (System.Text.Json, same serializer the model itself uses - see Utility.Extensions)
            _stepSavedJson = Utility.Extensions.ConvertToJson(cfg);

            Dispatcher.BeginInvoke(new Action(RepaintRois), DispatcherPriority.Loaded);
        }

        // Refresh the Min/Max fields for the selected ROI
        private bool _suppressSlider;   // avoid feedback loop when setting the slider from code
        private void RefreshSelRoiUi()
        {
            if (_selectedRoi == null)
            {
                txtSelRoi.Text = "(none)";
                _suppressSlider = true; if (sldRoiMin != null) sldRoiMin.Value = 30; _suppressSlider = false;
                if (txtRoiMinPct != null) txtRoiMinPct.Text = "30%";
                return;
            }
            var tplTag = _selectedRoi.Tpl != null
                ? $" [tpl {_selectedRoi.TplWidth}x{_selectedRoi.TplHeight}]"
                : " [NO TPL]";
            txtSelRoi.Text = (_selectedRoi.Name ?? "roi") + tplTag;
            _suppressSlider = true;
            if (sldRoiMin != null) sldRoiMin.Value = _selectedRoi.Min * 100.0;
            _suppressSlider = false;
            if (txtRoiMinPct != null) txtRoiMinPct.Text = $"{_selectedRoi.Min * 100:F0}%";
        }

        private void RoiMinSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtRoiMinPct != null) txtRoiMinPct.Text = $"{e.NewValue:F0}%";
            if (_suppressSlider || _selectedRoi == null) return;
            _selectedRoi.Min = e.NewValue / 100.0;
            // Re-evaluate pass/fail against the new threshold from the last score.
            if (_selectedRoi.LastScore.HasValue) _selectedRoi.LastPass = _selectedRoi.LastScore.Value >= _selectedRoi.Min;
            UpdateRois();
        }

        private static void SelectComboByString(ComboBox cbo, string value)
        {
            for (int i = 0; i < cbo.Items.Count; i++)
            {
                if (cbo.Items[i] is ComboBoxItem it && it.Content?.ToString() == value)
                {
                    cbo.SelectedIndex = i;
                    return;
                }
            }
        }

        // Save UI (FFT/dB/StopOnPass) back into the global model SoundConfig.
        public void SaveToStep()
        {
            if (Cfg == null) return;
            var cfg = Cfg;

            if (int.TryParse((cboFft.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int fft)) cfg.FftSize = fft;
            if (double.TryParse(txtMaxHz.Text, out double maxHz)) cfg.MaxHz = maxHz;
            if (double.TryParse(txtDbFloor.Text, out double dbF)) cfg.DbFloor = dbF;
            if (double.TryParse(txtDbTop.Text, out double dbT)) cfg.DbTop = dbT;

            cfg.Metric = "Template";   // fixed
            cfg.StopOnPass = chkStopOnPass.IsChecked == true;
            // ROI Min/Max already saved via RoiThreshold_TextChanged as the user types
        }

        // ---------- New handlers for toolbar ----------
        private void cboColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboColor == null) return;   // XAML not initialized yet
            _selectedColormap = (cboColor.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Hot";
            // Global setting -> persist immediately to Config.cfg
            if (_program?.appSetting != null)
            {
                _program.appSetting.SpectrogramColorMap = _selectedColormap;
                try { Utility.Extensions.SaveToFile(_program.appSetting, "Config.cfg"); } catch { }
            }
        }

        private void chkRoiVis_Changed(object sender, RoutedEventArgs e)
        {
            // XAML load may fire this event BEFORE the x:Name fields (iconRoiVis, roiCanvas) are assigned.
            // Use sender to read the state instead of the field for safety.
            if (iconRoiVis == null || roiCanvas == null) return;
            var tb = sender as System.Windows.Controls.Primitives.ToggleButton;
            bool vis = tb?.IsChecked == true;
            iconRoiVis.Icon = vis ? FontAwesome.Sharp.IconChar.Eye : FontAwesome.Sharp.IconChar.EyeSlash;
            roiCanvas.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
        }

        private void chkStopOnPass_Changed(object sender, RoutedEventArgs e)
        {
            if (Cfg == null) return;
            bool on = chkStopOnPass.IsChecked == true;
            Cfg.StopOnPass = on;
            // Turning the flag off clears every ROI designation (✓), like ZEROC. Turning it on keeps picks.
            if (!on && Cfg.Rois != null)
            {
                foreach (var r in Cfg.Rois) r.StopChecked = false;
                UpdateRois();
            }
        }

        private void btnRevertRoi_Click(object sender, RoutedEventArgs e)
        {
            var model = _program?.EditModel;
            if (string.IsNullOrEmpty(_stepSavedJson) || model == null) return;
            try
            {
                model.SoundConfig = Utility.Extensions.ConvertFromJson<SoundStepConfig>(_stepSavedJson)
                                    ?? new SoundStepConfig();
                LoadFromStep();
                txtInfo.Text = "Reverted to saved state";
            }
            catch (Exception ex) { txtInfo.Text = "Revert fail: " + ex.Message; }
        }

        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            helpOverlay.Visibility = helpOverlay.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void btnOpenModel_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.Application.Current?.MainWindow is VTMTester.MainWindow mw)
                mw.OpenModel();
            else
                txtInfo.Text = "Cannot access main window to open a model";
        }

        // Save the whole model (SoundConfig lives inside the model step, so this persists ROIs + templates).
        private void btnSaveModel_Click(object sender, RoutedEventArgs e)
        {
            var model = _program?.EditModel;
            if (model == null) { txtInfo.Text = "No model to save"; return; }
            SaveToStep();
            try
            {
                if (!string.IsNullOrEmpty(model.Path) && System.IO.File.Exists(model.Path))
                {
                    // Save the file directly; the in-memory model already holds the edits.
                    // (Don't call OnEditModelSave - it reloads the model and would drop our step reference.)
                    model.SaveTo(model.Path);
                    RefreshStepList();   // re-evaluate ROIs/Tpl columns; keeps the current step selected
                    txtInfo.Text = "Model saved: " + model.Path;
                }
                else
                {
                    btnSaveAsModel_Click(sender, e);
                }
            }
            catch (Exception ex) { txtInfo.Text = "Save fail: " + ex.Message; }
        }

        private void btnSaveAsModel_Click(object sender, RoutedEventArgs e)
        {
            var model = _program?.EditModel;
            if (model == null) { txtInfo.Text = "No model to save"; return; }
            SaveToStep();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                InitialDirectory = VTMBase.FolderMap.RootFolder,
                AddExtension = true,
                DefaultExt = VTMBase.FolderMap.DefaultModelFileExt
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    model.Name = dlg.SafeFileName;
                    model.Path = dlg.FileName;
                    model.SaveTo(dlg.FileName);
                    RefreshModelName();
                    txtInfo.Text = "Model saved as: " + dlg.FileName;
                }
                catch (Exception ex) { txtInfo.Text = "Save As fail: " + ex.Message; }
            }
        }

        // ------------ event handlers ------------
        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (cboFft == null) return;   // XAML not initialized yet
            var m = Mode;
            bool edit = m == PageMode.Edit;
            bool check = m == PageMode.Check;
            bool diag = m == PageMode.Diag;

            SetEditableControlsEnabled(edit);

            // Toolbar buttons per mode:
            //  EDIT  -> Revert, Capture (ROI always editable, START/STOP still work normally)
            //  CHECK -> Flag (StopOnPass), Eye (RoiVis) - realtime monitoring
            //  DIAG  -> Eye (view overlay); no START/STOP (test decides)
            SetVis(btnRevertRoi, edit);
            SetVis(btnCapture, edit);
            // Flag (StopOnPass) only in CHECK mode.
            SetVis(chkStopOnPass, check);
            SetVis(chkRoiVis, check || diag);

            // ROI hit-testing: EDIT = edit geometry; CHECK = left-click to designate (✓). DIAG = none.
            if (roiCanvas != null) roiCanvas.IsHitTestVisible = edit || check;

            // DIAG uses its own layer; EDIT/CHECK use the live layer.
            if (liveLayer != null) liveLayer.Visibility = diag ? Visibility.Collapsed : Visibility.Visible;
            if (diagLayer != null) diagLayer.Visibility = diag ? Visibility.Visible : Visibility.Collapsed;

            // DIAG step-name bar (shows which SND step the test is mirroring) only shows in DIAG mode.
            if (pnlDiagStep != null) pnlDiagStep.Visibility = diag ? Visibility.Visible : Visibility.Collapsed;
            if (diag) ShowDiagStepName(_currentStep);

            // START/STOP capture: EDIT + CHECK allowed; DIAG not (test drives mic).
            if (btnCaptureToggle != null) btnCaptureToggle.IsEnabled = edit || check;

            // DIAG: keep the last test snapshot (do NOT clear it when re-entering DIAG). It only shows
            // once a test has pushed data; leaving/returning DIAG preserves it. DIAG has its OWN score
            // storage (_diagPass/_diagScore) so it never touches the live (edit/check) roi.LastPass/LastScore.

            // Always redraw ROIs on the live canvas so they reappear (with their live scores) when leaving DIAG.
            UpdateRois();

            txtInfo.Text = edit ? "EDIT mode - edit ROIs/parameters + START to view live spectrogram"
                         : check ? "CHECK mode - START for realtime scoring; left-click ROIs to designate (checkmark) for the stop flag"
                         : "DIAGNOSTIC mode - waiting for auto/manual test to push data over";
        }

        private void ClearRoiScores()
        {
            var rois = Cfg?.Rois;
            if (rois == null) return;
            foreach (var r in rois) { r.LastPass = null; r.LastScore = null; }
        }

        private static void SetVis(UIElement el, bool visible)
        {
            if (el != null) el.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        // Lock/unlock the editable controls when switching mode.
        private void SetEditableControlsEnabled(bool enabled)
        {
            if (cboFft != null) cboFft.IsEnabled = enabled;
            if (txtMaxHz != null) txtMaxHz.IsEnabled = enabled;
            if (txtDbFloor != null) txtDbFloor.IsEnabled = enabled;
            if (txtDbTop != null) txtDbTop.IsEnabled = enabled;
            if (btnRevertRoi != null) btnRevertRoi.IsEnabled = enabled;
            if (btnCapture != null) btnCapture.IsEnabled = enabled;
            if (sldRoiMin != null) sldRoiMin.IsEnabled = enabled;
        }

        // Watch for autotest to push data into the DIAG view (like ZEROC autoWatch).
        // When SoundTester.ResultSeq changes -> autotest just finished Check -> load LastCols into spectrogram.
        private readonly DispatcherTimer _testWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        private bool _testWatchWired;
        private int _lastResultSeq = -1;
        private void EnsureTestWatch()
        {
            if (!_testWatchWired) { _testWatchTimer.Tick += TestWatchTimer_Tick; _testWatchWired = true; }
            _testWatchTimer.Start();
        }
        private void TestWatchTimer_Tick(object sender, EventArgs e)
        {
            var tester = _program?.SoundTester;
            if (tester == null) return;
            if (tester.ResultSeq == _lastResultSeq) return;
            if (tester.LastCols == null) return;
            // Only consume the result in DIAGNOSTIC mode. Do NOT advance _lastResultSeq otherwise, so a test
            // that finished before switching to DIAG is still picked up when the user enters DIAG.
            if (!DiagMode) return;
            _lastResultSeq = tester.ResultSeq;

            _diagHasSnapshot = true;   // test pushed data -> now DIAG may show spectrogram + ROIs
            ShowRecordedSnapshot(tester.LastCols);

            // Store per-ROI pass/fail into DIAG's own arrays (NOT roi.LastPass - that's for the live layer).
            // The running step's Condition2 picked which ROIs were scored (LastRoiPass in that subset order),
            // so map each back to its global index; ROIs the step didn't check stay null (neutral).
            var cfg = Cfg;
            if (cfg?.Rois != null)
            {
                int n = cfg.Rois.Count;
                _diagPass = new bool?[n];
                _diagScore = new double?[n];
                var subset = VTMBase.Program.SelectRoisByCondition(cfg, _currentStep?.Condition2);
                // Only map when the tester produced a full per-ROI result for this subset. Otherwise the test
                // errored early (bad config / missing template) -> leave every ROI neutral (no result).
                bool valid = tester.LastRoiPass != null && tester.LastRoiScore != null
                             && tester.LastRoiPass.Length == subset.Count;
                if (valid)
                {
                    for (int k = 0; k < subset.Count; k++)
                    {
                        int g = cfg.Rois.IndexOf(subset[k]);
                        if (g < 0) continue;
                        _diagPass[g] = tester.LastRoiPass[k];
                        _diagScore[g] = tester.LastRoiScore[k];
                    }
                }
            }
            DrawDiagRois();
            ShowDiagStepName(_currentStep);   // keep the step-name bar current
            string stepName = _currentStep != null ? $"  step #{_currentStep.No} {_currentStep.TestContent}" : "";
            txtInfo.Text = $"DIAG SNAPSHOT #{tester.ResultSeq}  {(tester.LastPass ? "PASS" : "FAIL")}{stepName}  {tester.LastDetail}";
        }

        // Draw ROIs on the DIAG canvas (read-only, colored green/red by pass/fail).
        private void DrawDiagRois()
        {
            if (roiCanvasDiag == null) return;
            roiCanvasDiag.Children.Clear();
            var rois = Cfg?.Rois;
            if (rois == null) return;
            double w = roiCanvasDiag.ActualWidth, h = roiCanvasDiag.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double maxHz = Cfg.MaxHz > 0 ? Cfg.MaxHz : 8000;

            for (int idx = 0; idx < rois.Count; idx++)
            {
                var roi = rois[idx];
                bool? pass = _diagPass != null && idx < _diagPass.Length ? _diagPass[idx] : null;
                double? score = _diagScore != null && idx < _diagScore.Length ? _diagScore[idx] : null;

                double x0 = Math.Min(roi.X0, roi.X1), x1 = Math.Max(roi.X0, roi.X1);
                double y0 = Math.Min(roi.Y0, roi.Y1), y1 = Math.Max(roi.Y0, roi.Y1);
                // Checked by the command -> green (pass) / red (fail) + faint fill.
                // Not checked (no result) -> red border, NO fill (distinguished by the missing fill/score).
                Color stroke = pass.HasValue
                    ? (pass.Value ? Color.FromRgb(0x39, 0xD9, 0x8A) : Color.FromRgb(0xE7, 0x4C, 0x3C))
                    : Color.FromRgb(0xE7, 0x4C, 0x3C);
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Stroke = new SolidColorBrush(stroke),
                    StrokeThickness = 2,
                    Fill = pass.HasValue
                        ? new SolidColorBrush(Color.FromArgb(34, stroke.R, stroke.G, stroke.B))
                        : Brushes.Transparent,
                    Width = (x1 - x0) * w,
                    Height = (y1 - y0) * h
                };
                Canvas.SetLeft(rect, x0 * w); Canvas.SetTop(rect, y0 * h);
                roiCanvasDiag.Children.Add(rect);

                double fTop = (1 - y0) * maxHz, fBot = (1 - y1) * maxHz;
                string sc = score.HasValue ? $"  NCC {score.Value * 100:F0}%" : "";
                var tb = new TextBlock
                {
                    Text = $"#{idx + 1} {fBot:F0}-{fTop:F0}Hz{sc}",
                    Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold, IsHitTestVisible = false
                };
                Canvas.SetLeft(tb, x0 * w + 3);
                Canvas.SetTop(tb, y0 * h - 15 < 0 ? y0 * h + 2 : y0 * h - 15);
                roiCanvasDiag.Children.Add(tb);
            }
        }

        // Draw the recorded block into a full 900-col window placed at LastTau (black-padded), like ZEROC
        // ShowRecorded - so the ROI overlay aligns with where the engine matched the buzzer.
        private void ShowRecordedSnapshot(float[][] vcols)
        {
            if (specImageDiag == null || vcols == null || vcols.Length == 0) return;
            int W = SoundTester.Cols, H = SoundTester.Bins;
            int r = vcols.Length;
            int tau = _program?.SoundTester?.LastTau ?? Math.Max(0, W - r);

            var bmp = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgra32, null);
            var buf = new byte[W * H * 4];   // default 0 = black padding
            for (int c = 0; c < W; c++)
            {
                int ri = c - tau;
                if (ri < 0 || ri >= r) continue;
                var col = vcols[ri];
                for (int y = 0; y < H; y++)
                {
                    float v = y < col.Length ? col[y] : 0f;
                    ColorFromValue(v, out byte rr, out byte gg, out byte bb);
                    int idx = (y * W + c) * 4;
                    buf[idx + 0] = bb;
                    buf[idx + 1] = gg;
                    buf[idx + 2] = rr;
                    buf[idx + 3] = 255;
                }
            }
            bmp.WritePixels(new Int32Rect(0, 0, W, H), buf, W * 4, 0);
            specImageDiag.Source = bmp;
        }

        private void btnCaptureToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (iconCaptureToggle == null || txtCaptureToggle == null) return;
            var tb = sender as System.Windows.Controls.Primitives.ToggleButton;
            bool want = tb?.IsChecked == true;

            if (want)
            {
                if (_program?.SoundTester == null) { txtInfo.Text = "SoundTester not available"; return; }
                if (StepMode) SaveToStep();
                _program.SoundTester.MicrophoneId = _program?.appSetting?.Communication?.MicrophoneId ?? "";
                _userCapture = true;   // mark BEFORE Start (CaptureStarted fires synchronously inside Start)
                _program.SoundTester.Start();

                // If Start fails, show the error on the UI
                if (!_program.SoundTester.IsCapturing)
                {
                    _userCapture = false;
                    txtInfo.Text = $"MIC ERR: {_program.SoundTester.LastError} - {_program.SoundTester.LastDetail}";
                    // Revert toggle
                    btnCaptureToggle.Checked -= btnCaptureToggle_Changed;
                    btnCaptureToggle.Unchecked -= btnCaptureToggle_Changed;
                    btnCaptureToggle.IsChecked = false;
                    btnCaptureToggle.Checked += btnCaptureToggle_Changed;
                    btnCaptureToggle.Unchecked += btnCaptureToggle_Changed;
                    return;
                }
                iconCaptureToggle.Icon = FontAwesome.Sharp.IconChar.Stop;
                txtCaptureToggle.Text = "STOP";
                btnCaptureToggle.Background = new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));
                txtInfo.Text = $"Capturing... mic={(_program?.appSetting?.Communication?.MicrophoneName ?? "default")}";
            }
            else
            {
                _program?.SoundTester?.Stop();
                iconCaptureToggle.Icon = FontAwesome.Sharp.IconChar.Play;
                txtCaptureToggle.Text = "START";
                btnCaptureToggle.Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            }
        }

        private void btnCapture_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRoi == null) { txtInfo.Text = "Select a ROI first (click a box)"; return; }
            if (_program?.SoundTester == null || Cfg == null) return;
            SaveToStep();

            var cfg = Cfg;
            int fft = cfg.FftSize > 0 ? cfg.FftSize : 2048;
            int hop = Math.Max(1, fft / 4);

            // Build spectrogram with the exact Cols=900 window (like RenderSpectrogram) so ROI-x maps correctly.
            int needed = SoundTester.Cols * hop + fft;
            var samples = _program.SoundTester.SnapshotSamples(needed);
            if (samples.Length < fft) { txtInfo.Text = "Not enough samples - press START capture first"; return; }
            var cols = _program.SoundTester.BuildLiveColumns(samples, cfg);
            if (cols.Count == 0) { txtInfo.Text = "No spectrogram cols"; return; }

            // ExtractPatch per ZEROC model: ROI-x normalized to Cols=900, cols right-aligned.
            int Cols = SoundTester.Cols;
            int count = Math.Min(cols.Count, Cols);
            int displayStart = Cols - count;   // first displayed column (oldest)
            int bins = SoundTester.Bins;
            int tplW = SoundTester.TplW, tplH = SoundTester.TplH;

            double x0 = Math.Min(_selectedRoi.X0, _selectedRoi.X1);
            double x1 = Math.Max(_selectedRoi.X0, _selectedRoi.X1);
            double y0 = Math.Min(_selectedRoi.Y0, _selectedRoi.Y1);
            double y1 = Math.Max(_selectedRoi.Y0, _selectedRoi.Y1);

            int rowA = (int)(y0 * bins), rowB = (int)(y1 * bins);
            if (rowA < 0) rowA = 0; if (rowB > bins) rowB = bins; if (rowB <= rowA) rowB = Math.Min(bins, rowA + 1);

            // Find the source column range within ROI-x (through the 900 display window)
            int colA = -1, colB = -1;
            for (int i = 0; i < count; i++)
            {
                double xf = (displayStart + i) / (double)Cols;
                if (xf >= x0 && xf <= x1) { if (colA < 0) colA = i; colB = i; }
            }
            if (colA < 0) { txtInfo.Text = "ROI outside the data area - drag the box to the right (newest)"; return; }

            int srcCols = cols.Count;   // index into cols: newest column at the end
            int baseIdx = srcCols - count;   // start of the newest count columns
            int rows = rowB - rowA, wcols = colB - colA + 1;
            var patch = new float[tplH * tplW];
            for (int ty = 0; ty < tplH; ty++)
            {
                int sr = rowA + (int)((ty + 0.5) / tplH * rows); if (sr >= bins) sr = bins - 1;
                for (int tx = 0; tx < tplW; tx++)
                {
                    int ci = colA + (int)((tx + 0.5) / tplW * wcols); if (ci >= count) ci = count - 1;
                    var mg = cols[baseIdx + ci];
                    patch[ty * tplW + tx] = (mg != null && sr < mg.Length) ? mg[sr] : 0f;
                }
            }
            var captured = _selectedRoi;
            captured.Tpl = patch;
            captured.TplWidth = tplW;
            captured.TplHeight = tplH;

            cfg.Metric = "Template";
            // Score against the very columns we captured from, so the captured ROI reads NCC ~100% right away
            // (patch == template extracted at the same position -> NCC = 1.0).
            _liveColsForScore = cols;
            ScoreRoisLive(cols);
            SelectRoi(captured);   // keep the same ROI selected (global ROI, edited in place)
            UpdateRois();
            txtInfo.Text = $"Template captured for ROI '{captured.Name}' ({tplW}x{tplH}) - NCC {(captured.LastScore ?? 0) * 100:F0}%";
        }

        // Param changed -> flush straight into the model step config (no Save button needed;
        // model is saved by the external Save button - follows the model).
        private void Param_Changed(object sender, RoutedEventArgs e)
        {
            if (Cfg == null) return;
            SaveToStep();
        }

        private void SoundTester_CaptureStarted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_userCapture) return;   // test-driven capture -> ignore here (DIAG mirrors tests instead)
                UpdateCaptureUi(true);
                ClearRoiScores();       // fresh run starts clean (neutral until first match)
                _renderTimer.Start();   // render loop does live spectrogram + position-fixed ROI scoring
            });
        }

        private void SoundTester_CaptureStopped(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_userCapture) return;   // a test stopping the shared mic must not touch EDIT/CHECK
                _userCapture = false;
                UpdateCaptureUi(false);
                _renderTimer.Stop();
                // Keep the frozen columns + scores so moving/resizing a ROI still re-scores against
                // the last spectrogram (like ZEROC). Scores are cleared on the next START.
                UpdateRois();
            });
        }

        private void UpdateCaptureUi(bool capturing)
        {
            // Sync toggle state without re-firing event
            if (btnCaptureToggle != null && btnCaptureToggle.IsChecked != capturing)
            {
                btnCaptureToggle.Checked -= btnCaptureToggle_Changed;
                btnCaptureToggle.Unchecked -= btnCaptureToggle_Changed;
                btnCaptureToggle.IsChecked = capturing;
                btnCaptureToggle.Checked += btnCaptureToggle_Changed;
                btnCaptureToggle.Unchecked += btnCaptureToggle_Changed;
                if (iconCaptureToggle != null)
                {
                    iconCaptureToggle.Icon = capturing ? FontAwesome.Sharp.IconChar.Stop : FontAwesome.Sharp.IconChar.Play;
                }
                if (txtCaptureToggle != null)
                {
                    txtCaptureToggle.Text = capturing ? "STOP" : "START";
                }
                btnCaptureToggle.Background = new SolidColorBrush(capturing
                    ? Color.FromRgb(0xB7, 0x1C, 0x1C)
                    : Color.FromRgb(0x4C, 0xAF, 0x50));
            }
        }

    }
}
