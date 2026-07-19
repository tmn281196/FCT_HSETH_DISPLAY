using Camera;
using Camera.VisionTest;
using Utility;
using VTMBase;
using VTMProgram;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Timer = System.Timers.Timer;

namespace VTMTester
{
    /// <summary>
    /// Interaction logic for VisionPage.xaml
    /// </summary>
    public partial class VisionPage : Page
    {        //Variable
        private Step buf;
        private Step currentStep;
        private int _fndProcessing = 0;
        private int _lcdProcessing = 0;

        // Pass/fail colours for the FND / LED live read-out bars (green = reading matches the taught Oper, red =
        // differs, neutral grey = nothing to compare). Same palette as the LCD/FND detect panel's coloured bar.
        private static readonly System.Windows.Media.Brush ReadoutPassBrush =
            new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2E7D32"));
        private static readonly System.Windows.Media.Brush ReadoutFailBrush =
            new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C62828"));
        private static readonly System.Windows.Media.Brush ReadoutNeutralBrush =
            new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#333333"));

        private Model editModel = new Model();

        public Model EditModel
        {
            get { return editModel; }
            set
            {
                if (value != editModel)
                {
                    Program.EditModel = value;
                    editModel = value;

                    this.DataContext = EditModel;
                    if (this._contentLoaded)
                    {
                        if (EditModel.VisionModels != null)
                        {
                            VisionBuider.Models = EditModel.VisionModels;
                            PlaceDetectPanel(null);
                        }
                        else
                        {
                            // row 바뀌면 바뀐 cmd 갖고오고 그게 LCD면 LCD 위치 해당 row lcd roi 갖고오기.
                            EditModel.VisionModels = new Camera.VisionModel();
                            VisionBuider.Models = EditModel.VisionModels;
                            PlaceDetectPanel(null);
                        }
                        cameraSetting.ApplyModelSettings(EditModel.CameraSetting);   // sliders from MODEL, not a camera read

                        DataGrid_FND_0.ItemsSource = VisionBuider.Models.FNDs[0][0].PointSegments.LEDs;
                        DataGrid_FND_1.ItemsSource = VisionBuider.Models.FNDs[1][0].PointSegments.LEDs;
                        DataGrid_FND_2.ItemsSource = VisionBuider.Models.FNDs[2][0].PointSegments.LEDs;
                        DataGrid_FND_3.ItemsSource = VisionBuider.Models.FNDs[3][0].PointSegments.LEDs;
                        DataGrid_FND_4.ItemsSource = VisionBuider.Models.FNDs[4][0].PointSegments.LEDs;
                        DataGrid_FND_5.ItemsSource = VisionBuider.Models.FNDs[5][0].PointSegments.LEDs;
                        DataGrid_FND_6.ItemsSource = VisionBuider.Models.FNDs[6][0].PointSegments.LEDs;

                        GLEDsData.ItemsSource = VisionBuider.Models.GLED[0].GLEDs;
                        LEDsData.ItemsSource = VisionBuider.Models.LED[0].LEDs;
                        PlaceDetectPanel(null);
                        EditModel.VisionModels.UpdateLayout(EditModel.Layout.PCB_Count);
                        EditModel.Layout.PCB_COUNT_CHANGE += Layout_PCB_COUNT_CHANGE;
                        UpdateLayout(EditModel.Layout.PCB_Count);
                        cbbTxnaming_Manual.ItemsSource = editModel.Naming.TxDatas.Select(x => x.Name).ToList();
                        FND_lookupTable.Update(editModel.ModelSegmentLookup);
                        SelectFirstVisionStep();
                    }
                }
            }
        }

        private void SelectFirstVisionStep()
        {
            var firstVisionStep = EditModel.Steps.FirstOrDefault(step => step.CMD == "LED" || step.CMD == "LCD" || step.CMD == "FND" || step.CMD == "GLED");
            VisionStepsGrid.SelectedItem = firstVisionStep;
        }

        private Program program = new Program();

        public Program Program
        {
            get { return program; }
            set
            {
                program = value;
                Program.EditModel = editModel;
                SolenoidControl.SerialPort.Port = program.Solenoid.SerialPort.Port;
                RelayControl.SerialPort.Port = program.Relay.SerialPort.Port;
                SystemControl.System_Board = program.System.System_Board;
                UUT1Com.Content = Program.UUTs[0].LogBoxVision;
                UUT2Com.Content = Program.UUTs[1].LogBoxVision;
                UUT3Com.Content = Program.UUTs[2].LogBoxVision;
                UUT4Com.Content = Program.UUTs[3].LogBoxVision;
            }
        }

        private Timer GetFNDImageSampleTimer = new Timer
        {
            Interval = 100,
        };

        private Timer GetLCDImageSampleTimer = new Timer
        {
            Interval = 100,
        };

        private OCR ocr;

        public VisionPage()
        {
            ocr = new OCR();    // OCR engine is [ThreadStatic] (see OCR.cs): this page's GetLCDSampleTimer thread runs OCR
                                // directly + owns its own predictor - built + run on one thread, no queue => no ONEDNN/NCHW.


            InitializeComponent();

            // Timer get image for test
            GetFNDImageSampleTimer.Elapsed += GetImageSampleTimer_Elapsed;

            GetLCDImageSampleTimer.Elapsed += GetLCDImageSampleTimer_Elapsed;

            EditModel.VisionModels = VisionBuider.Models;

            PlaceDetectPanel(null);

            TransformGroup transformGroup = (TransformGroup)this.FindResource("sharedTransform");
            scaleTransform = (ScaleTransform)transformGroup.Children[0];
            translateTransform = (TranslateTransform)transformGroup.Children[1];
        }

        public void EnableLive()
        {
            GetFNDImageSampleTimer.Start();
            GetLCDImageSampleTimer.Start();

        }

        public void DisableLive()
        {
            GetFNDImageSampleTimer.Stop();
            GetLCDImageSampleTimer.Stop();
        }

   

        private void GetLCDImageSampleTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (Interlocked.CompareExchange(ref _lcdProcessing, 1, 0) != 0) return;
            try
            {
                var lastFrame = Program.Capture?.LastMatFrame;
                if (lastFrame == null) return;
                Program.EditModel.VisionModels.GetLCDSampleImage(lastFrame, ocr);
            }
            finally { Interlocked.Exchange(ref _lcdProcessing, 0); }
        }

        private string ReplaceCharAtIndex(string s, int index, char newChar)
        {
            if (index < 0 || index >= s.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range.");
            }

            char[] chars = s.ToCharArray();
            chars[index] = newChar;
            return new string(chars);
        }

        private void GetImageSampleTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (Interlocked.CompareExchange(ref _fndProcessing, 1, 0) != 0) return;
            try
            {
                var lastFrame = Program.Capture?.LastMatFrame;
                if (lastFrame == null) return;

                // Heavy OpenCV compute on ThreadPool
                Program.EditModel.VisionModels.GetFNDSampleImage(lastFrame);
                Program.EditModel.VisionModels.GetLEDSampleImage(lastFrame);
                Program.EditModel.VisionModels.GetGLEDSampleImage(lastFrame);

                // Light UI label updates on UI thread
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateVisionPageLabels();
                }));
            }
            finally { Interlocked.Exchange(ref _fndProcessing, 0); }
        }

        // Which of the four board toggles (A/B/C/D) is selected, as a 0-based index; -1 if none. The toggles are
        // mutually exclusive (set in BoardSelect_Click), so first match wins. Replaces the repeated if(tgbSelectA)...
        // chains in the value/threshold handlers - and folding the FND handlers through it fixes the board-D copy-
        // paste bug where they read FND char index [2] instead of [3].
        private int SelectedBoardIndex()
        {
            if (tgbSelectA.IsChecked == true) return 0;
            if (tgbSelectB.IsChecked == true) return 1;
            if (tgbSelectC.IsChecked == true) return 2;
            if (tgbSelectD.IsChecked == true) return 3;
            return -1;
        }

        // Runs off the vision timer, so it must survive every "not measured yet" state rather than throw onto a
        // Dispatcher callback where nobody catches it.
        private void UpdateVisionPageLabels()
        {
            int boardIndex = SelectedBoardIndex();
            if (boardIndex < 0) return;

            var models = Program?.EditModel?.VisionModels;
            if (models == null) return;

            if (lbGLEDvalue != null)
                lbGLEDvalue.Content = models.GLED[boardIndex].CalculatorOutputString ?? "";

            // FND read-out, in realtime off the timer - this is what the (now removed) GetValue button used to do.
            // The compute (GetFNDSampleImage) already runs each timer tick, so DetectedString is fresh.
            if (lbMatrixPointValue != null)
            {
                string fndVal = "";
                foreach (var fndChar in models.FNDs) fndVal += fndChar[boardIndex].DetectedString;
                lbMatrixPointValue.Content = fndVal;
                ColorReadoutBar(lbMatrixPointValue, fndVal, CMDs.FND);
            }

            // CalculatorBinaryOutputString is null until LED.GetValue(Mat) has run at least once - i.e. on every
            // freshly opened model, before the first LED frame is measured. It used to arrive pre-filled from the
            // file; VisionModels is no longer persisted, so the null window is now the normal startup state.
            string tempOutput = models.LED[boardIndex].CalculatorBinaryOutputString;
            if (string.IsNullOrEmpty(tempOutput))
            {
                if (lbLEDvalue != null)
                {
                    lbLEDvalue.Content = "";
                    ColorReadoutBar(lbLEDvalue, "", CMDs.LED);
                }
                return;
            }

            int indexLED = 0;
            foreach (var item in models.LED[boardIndex].LEDs)
            {
                if (indexLED >= tempOutput.Length) break;   // fewer measured bits than LEDs - nothing to mask
                if (!item.Use)
                {
                    tempOutput = ReplaceCharAtIndex(tempOutput, indexLED, 'X');
                }
                indexLED++;
            }

            try
            {
                string bits = new string(tempOutput.Replace("X", "").Reverse().ToArray());
                lbLEDvalue.Content = bits.Length == 0 ? "" : Convert.ToInt32(bits, 2).ToString("X");
                ColorReadoutBar(lbLEDvalue, lbLEDvalue.Content?.ToString() ?? "", CMDs.LED);
            }
            catch (Exception)
            {
                if (lbLEDvalue != null)
                {
                    lbLEDvalue.Content = "";
                    ColorReadoutBar(lbLEDvalue, "", CMDs.LED);
                }
            }
        }

        // Colour an FND / LED live read-out bar by pass/fail (replaces the old fixed-black background). Green when
        // the live reading matches the selected step's taught expected value (Oper), red when it differs, neutral
        // grey when there is nothing to compare: no reading yet, or the selected step isn't of this family. Mirrors
        // the test comparison (reading == step.Oper) and the LCD/FND detect panel's coloured bar. The active step is
        // resolved the same way CopyDetectedToOper does (currentStep, falling back to the grid selection).
        private void ColorReadoutBar(System.Windows.Controls.Label bar, string reading, CMDs family)
        {
            if (bar == null) return;
            var step = currentStep ?? (VisionStepsGrid != null ? VisionStepsGrid.SelectedItem as Step : null);
            if (string.IsNullOrEmpty(reading) || step == null || step.cmd != family)
            {
                bar.Background = ReadoutNeutralBrush;
                return;
            }
            bar.Background = reading == (step.Oper ?? "") ? ReadoutPassBrush : ReadoutFailBrush;
        }

        private void btOpenModel_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog
            {
                DefaultExt = ".model",
                Title = "Open model",
            };
            openFile.Filter = "VTM model files (*.vmdl)|*.vmdl";
            openFile.RestoreDirectory = true;
            if (openFile.ShowDialog() == true)
            {
                Utility.Debug.Write("Load model:" + openFile.FileName, Utility.Debug.ContentType.Notify);
                //var fileInfor = new FileInfo(openFile.FileName);
                string modelStr = System.IO.File.ReadAllText(openFile.FileName);
                try
                {
                    string modelString = Utility.Extensions.Decoder(modelStr, System.Text.Encoding.UTF7);
                    //TestModel = Utility.Extensions.ConvertFromJson<Model>(modelString);
                    EditModel = Utility.Extensions.ConvertFromJson<Model>(modelString);
                    EditModel.Path = openFile.FileName;
                    foreach (var item in EditModel.Steps)
                    {
                        item.ValueGet1 = "";
                        item.ValueGet2 = "";
                        item.ValueGet3 = "";
                        item.ValueGet4 = "";
                    }
                    Program.OnEditModelLoaded();
                }
                catch (Exception)
                {
                    Utility.Debug.Write("Load model fail, file not correct format. \n" +
                        "Model folder: " + openFile.FileName, Utility.Debug.ContentType.Error);
                }
            }
        }

        // ---- Nudge every vision ROI of EVERY step (arrow buttons) ----
        private int NudgeStep()
        {
            if (txtNudgeStep != null && int.TryParse(txtNudgeStep.Text, out int s) && s > 0) return s;
            return 2;
        }
        private void NudgeLeft_Click(object sender, RoutedEventArgs e)  { NudgeRois(-NudgeStep(), 0); }
        private void NudgeRight_Click(object sender, RoutedEventArgs e) { NudgeRois(NudgeStep(), 0); }
        private void NudgeUp_Click(object sender, RoutedEventArgs e)    { NudgeRois(0, -NudgeStep()); }
        private void NudgeDown_Click(object sender, RoutedEventArgs e)  { NudgeRois(0, NudgeStep()); }

        // Shift EVERY vision ROI of EVERY step by (dx, dy) - for re-aligning a whole model after the camera or
        // the jig has shifted, without re-editing each step by hand.
        //
        // Two stores hold ROI coordinates, and each ROI must move EXACTLY once:
        //   1. VisionBuider.Models - the live/editor copy. For LED / LCD / FND segments it is only a working copy
        //      of the SELECTED step (loaded on selection, written back on selection-change and on Save Model).
        //      For GLED it is the ONE AND ONLY store - GLED has no per-step fields - so moving it here moves it
        //      for the whole model, once.
        //   2. Step.LedList / LCDRoiValue0..3 / FNDsBoard0[].PointSegments / RectFNDsBoard0[] - stored per-step.
        //
        // Skipping `buf` (the step the live copy mirrors) avoids shifting that one step twice - but ONLY for the
        // families the live copy genuinely round-trips. RectFNDsBoard0 is NOT one of them: SelectionChanged never
        // writes it back, and its load is commented out (see "FNDchar[0].Rect = FNDchar[0].Rect;"), so the live
        // FND box does not mirror buf at all. It therefore has to move for EVERY step, buf included - otherwise
        // the selected step's crop boxes silently stay behind while its own segment points move.
        //
        // Everything goes through Translate(), never through X/Y or the Rect property: X/Y are a CACHE that is
        // 0 on a freshly built model, and the Rect property bounds-guards (and truncates) the move. Either would
        // apply a different delta to the live copy than to the stores and tear the model apart.
        private void NudgeRois(int dx, int dy)
        {
            if (VisionBuider?.Models == null || EditModel?.Steps == null) return;

            var m = VisionBuider.Models;

            // ALL-OR-NOTHING. Translate() deliberately bypasses the bounds guard on the Rect setters so that every
            // ROI takes the same delta - but that guard was also the only thing keeping an ROI inside the frame,
            // and stepping outside it is not cosmetic:
            //   * LCD.TestImage is `async void`, so the OpenCV crop it does on an out-of-frame rect throws on a
            //     ThreadPool thread where no caller can catch it -> the whole app goes down.
            //   * At test time UpdateLcdRoi/UpdateFndRoi assign through the guarded Rect setter, which SILENTLY
            //     REJECTS an out-of-range value, leaving the PREVIOUS step's ROI in place - a wrong pass/fail.
            // So the bounds are checked BEFORE anything moves, and the whole click is refused if any ROI would
            // leave the canvas. Refusing is safe; clamping per-ROI is not, because it would move some ROIs and
            // not others and tear the model apart.
            string blocker = NudgeBlocker(dx, dy);
            if (blocker != null)
            {
                // Name the offender: there are ~450 ROI slots across a model, and "something is out of frame"
                // leaves the operator no way to find which one.
                Utility.Debug.Write("VISION:NUDGE REFUSED - " + blocker + " would leave the frame",
                                        Utility.Debug.ContentType.Warning);
                return;
            }

            // 1. Live/editor copy. Every family moves, regardless of the selected step's cmd.
            //    Translate() calls SetPosition() -> the overlay redraws automatically.
            foreach (var led in m.LED[0].LEDs) led.Translate(dx, dy);
            foreach (var g in m.GLED[0].GLEDs) g.Translate(dx, dy);
            foreach (var lcd in m.LCDs) lcd.Translate(dx, dy);
            foreach (var fndChar in m.FNDs)
            {
                var fnd = fndChar[0];
                fnd.Translate(dx, dy);   // the char box...
                foreach (var seg in fnd.PointSegments.LEDs) seg.Translate(dx, dy);   // ...and its 7 segments
            }

            // 2. Stored per-step data - EVERY step, buf included.
            //    buf is NOT skipped: every live->buf writeback (SelectionChanged, Save Model, Save As) is a
            //    wholesale ASSIGN from the live copy, never an accumulate, so shifting buf's store as well can
            //    not double-shift it - both sides carry the same delta and whichever wins holds the same value.
            //    Skipping it, on the other hand, left buf torn in half: its FND box moved (no live mirror) while
            //    its segments did not, and ModelPage/SoundPage could save that state before any writeback ran.
            foreach (var step in EditModel.Steps)
            {
                if (step == null) continue;
                NudgeStepStore(step, dx, dy);
            }
        }

        // Shift one step's STORED ROI data.
        // Deliberately unconditional on step.cmd: the selection-change writeback copies every family into the
        // step regardless of its cmd, so a step's unused families still hold coordinates and must stay in step
        // with the rest rather than drift. Offset()/IsUnset skip the slots that were never filled in.
        private void NudgeStepStore(Step step, int dx, int dy)
        {
            // LED - only the LEDs actually in use are stored.
            foreach (var led in step.LedList)
                if (!IsUnset(led.rect)) led.Translate(dx, dy);

            // LCD - four plain Rect structs (value types: read, offset, write back).
            // Null = this step has no LCD ROI; nothing to move.
            if (step.LCDRoiValue0.HasValue) step.LCDRoiValue0 = Offset(step.LCDRoiValue0.Value, dx, dy);
            if (step.LCDRoiValue1.HasValue) step.LCDRoiValue1 = Offset(step.LCDRoiValue1.Value, dx, dy);
            if (step.LCDRoiValue2.HasValue) step.LCDRoiValue2 = Offset(step.LCDRoiValue2.Value, dx, dy);
            if (step.LCDRoiValue3.HasValue) step.LCDRoiValue3 = Offset(step.LCDRoiValue3.Value, dx, dy);

            // FND - each char's own box, and its 7 segment points.
            for (int i = 0; i < step.RectFNDsBoard0.Count; i++)
                step.RectFNDsBoard0[i] = Offset(step.RectFNDsBoard0[i], dx, dy);
            foreach (var fnd in step.FNDsBoard0)
            {
                if (fnd?.PointSegments?.LEDs == null) continue;
                foreach (var seg in fnd.PointSegments.LEDs)
                    if (!IsUnset(seg.rect)) seg.Translate(dx, dy);
            }

            // No GLED here on purpose: GLED has no per-step store, it lives only on the live model above.
        }

        // Offset a stored Rect. An UNSET rect is left alone: Step.LCDRoiValue0..3 have no initializer, so a step
        // the operator never selected still holds default(Rect) = (0,0,0,0). Offsetting that would turn it into a
        // real-looking (dx,dy,0,0) which the live LCD would then load as a zero-sized ROI.
        private static Rect Offset(Rect r, int dx, int dy)
        {
            if (IsUnset(r)) return r;
            return new Rect(r.X + dx, r.Y + dy, r.Width, r.Height);
        }

        // A rect that was never given a size is not an ROI - it is an empty slot.
        private static bool IsUnset(Rect r)
        {
            return r.Width <= 0 || r.Height <= 0;
        }

        // How far one coordinate sits OUTSIDE the range the Rect setters and the runtime ROI loaders accept
        // (`> 0 && < limit - size`). 0 means inside. Used to compare before/after rather than pass/fail, because
        // an ROI can already be out of frame WITHOUT the nudge's help: the LED grid's X/Y columns are editable
        // and SingleLED's X/Y setters are unguarded, so an operator can simply type -5 into a cell.
        private static double OutBy(double pos, double size, double limit)
        {
            double max = limit - size;
            if (pos <= 0) return 1 - pos;        // past the left/top edge (the setters use a strict > 0)
            if (pos >= max) return pos - max + 1;   // past the right/bottom edge
            return 0;
        }

        // Does the move leave this ROI no worse off than it already was, on each axis independently?
        //
        // NOT "is it inside the frame" - that was too strict in both directions. A single ROI with a bad X would
        // veto a purely VERTICAL nudge (which never touches X), and would veto the nudge that moves it back
        // toward the frame - i.e. one mistyped cell killed all four arrows forever, with no way to recover using
        // the feature itself. Comparing OutBy before vs after handles every case for free: an unmoved axis scores
        // equal and passes; inside -> inside passes; inside -> outside is refused; outside -> further outside is
        // refused; outside -> less outside (or back inside) is allowed.
        private static bool Fits(Rect before, Rect after, Rect canvas)
        {
            if (IsUnset(after)) return true;   // empty slot, never moved, never loaded
            return OutBy(after.X, after.Width, canvas.Width) <= OutBy(before.X, before.Width, canvas.Width)
                && OutBy(after.Y, after.Height, canvas.Height) <= OutBy(before.Y, before.Height, canvas.Height);
        }

        // Would (dx, dy) push any ROI - live OR stored, across every step - further out of the frame than it
        // already is? Returns the offending ROI's name, or null when the whole nudge is safe to apply.
        private string NudgeBlocker(int dx, int dy)
        {
            var m = VisionBuider.Models;
            // One camera frame, so one canvas size for everything. Taken from a live element: the stored clones
            // never had their ParentCanvasSize set, and testing against 0x0 would refuse every nudge.
            Rect canvas = m.LCDs[0].ParentCanvasSize;
            if (canvas.Width <= 0 || canvas.Height <= 0) return null;   // canvas not measured yet - do not block

            for (int i = 0; i < m.LED[0].LEDs.Count; i++)
                if (!Ok(m.LED[0].LEDs[i].rect)) return "live LED[" + i + "]";
            for (int i = 0; i < m.GLED[0].GLEDs.Count; i++)
                if (!Ok(m.GLED[0].GLEDs[i].rect)) return "live GLED[" + i + "]";
            for (int i = 0; i < m.LCDs.Count; i++)
                if (!Ok(m.LCDs[i].rect)) return "live LCD[" + i + "]";
            for (int c = 0; c < m.FNDs.Count; c++)
            {
                if (!Ok(m.FNDs[c][0].rect)) return "live FND[" + c + "] box";
                var segs = m.FNDs[c][0].PointSegments.LEDs;
                for (int s = 0; s < segs.Count; s++)
                    if (!Ok(segs[s].rect)) return "live FND[" + c + "] segment[" + s + "]";
            }

            for (int n = 0; n < EditModel.Steps.Count; n++)
            {
                var step = EditModel.Steps[n];
                if (step == null) continue;
                string at = "step " + (n + 1) + " ";
                for (int i = 0; i < step.LedList.Count; i++)
                    if (!Ok(step.LedList[i].rect)) return at + "LED[" + step.LedList[i].Index + "]";
                if (step.LCDRoiValue0.HasValue && !Ok(step.LCDRoiValue0.Value)) return at + "LCD ROI 0";
                if (step.LCDRoiValue1.HasValue && !Ok(step.LCDRoiValue1.Value)) return at + "LCD ROI 1";
                if (step.LCDRoiValue2.HasValue && !Ok(step.LCDRoiValue2.Value)) return at + "LCD ROI 2";
                if (step.LCDRoiValue3.HasValue && !Ok(step.LCDRoiValue3.Value)) return at + "LCD ROI 3";
                for (int i = 0; i < step.RectFNDsBoard0.Count; i++)
                    if (!Ok(step.RectFNDsBoard0[i])) return at + "FND[" + i + "] box";
                for (int c = 0; c < step.FNDsBoard0.Count; c++)
                {
                    var fnd = step.FNDsBoard0[c];
                    if (fnd?.PointSegments?.LEDs == null) continue;
                    for (int s = 0; s < fnd.PointSegments.LEDs.Count; s++)
                        if (!Ok(fnd.PointSegments.LEDs[s].rect)) return at + "FND[" + c + "] segment[" + s + "]";
                }
            }
            return null;

            bool Ok(Rect r) { return Fits(r, Offset(r, dx, dy), canvas); }
        }

        private async void btSaveModel_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep != null)
            {
                if (currentStep.cmd == CMDs.LED)
                {
                    currentStep.LedList.Clear();

                    foreach (var item in LEDsData.Items)
                    {
                        var led = (item as Camera.SingleLED);
                        //var ledClone = led.Clone();
                        if (led.Use)
                        {
                            currentStep.LedList.Add(led.Clone());
                            // parentCanvas == null it cannot clone
                        }
                    }
                }
                if (currentStep.cmd == CMDs.LCD)
                {
                    var lcdList = VisionBuider.Models.LCDs;

                    currentStep.LCDRoiValue0 = lcdList[0].Rect;
                    currentStep.LCDRoiValue1 = lcdList[1].Rect;
                    currentStep.LCDRoiValue2 = lcdList[2].Rect;
                    currentStep.LCDRoiValue3 = lcdList[3].Rect;
                }

                // InitialFND() rather than a bare cmd check: a step just retyped to FND has no store yet, and the
                // loop below indexes it a hardcoded 7 times. Idempotent, so an existing store is left alone.
                if (currentStep.cmd == CMDs.FND)
                {
                    currentStep.InitialFND();
                    int index_char = 0;
                    foreach (var FNDchar in VisionBuider.Models.FNDs)
                    {
                        if (EditModel.Layout.PCB_Count >= 1)

                        {
                            for (int index_led = 0; index_led < 7; index_led++)
                            {
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].X = FNDchar[0].PointSegments.LEDs[index_led].X;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Y = FNDchar[0].PointSegments.LEDs[index_led].Y;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Dir = FNDchar[0].PointSegments.LEDs[index_led].Dir;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].ON = FNDchar[0].PointSegments.LEDs[index_led].ON;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].OFF = FNDchar[0].PointSegments.LEDs[index_led].OFF;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Thresh = FNDchar[0].PointSegments.LEDs[index_led].Thresh;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Use = FNDchar[0].PointSegments.LEDs[index_led].Use;
                            }

                            currentStep.RectFNDsBoard0[index_char] = FNDchar[0].rect;
                            //currentStep.FNDsBoard0[index_char].rect = FNDchar[0].rect;
                            currentStep.UseFNDsBoard0[index_char] = FNDchar[0].Use;
                            //currentStep.FNDsBoard0[index_char].Use = FNDchar[0].Use;
                        }

                        index_char++;
                    }
                }
            }

            CommitCameraToModel();   // persist the CURRENT slider values (not a fresh camera read - see the method)
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.Brightness).ToString(), Debug.ContentType.Notify);
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.BackLight).ToString(), Debug.ContentType.Notify);
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.Contrast).ToString(), Debug.ContentType.Notify);
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.Exposure).ToString(), Debug.ContentType.Notify);
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.Focus).ToString(), Debug.ContentType.Notify);
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.Saturation).ToString(), Debug.ContentType.Notify);
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.Sharpness).ToString(), Debug.ContentType.Notify);
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.Zoom).ToString(), Debug.ContentType.Notify);
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.WhiteBalanceBlueU).ToString(), Debug.ContentType.Notify);
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.WhiteBalanceRedV).ToString(), Debug.ContentType.Notify);
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.Pan).ToString(), Debug.ContentType.Notify);
            //Debug.Write(Program.Capture.videoCapture.Get(OpenCvSharp.VideoCaptureProperties.Gain).ToString(), Debug.ContentType.Notify);

            EditModel.ModelSegmentLookup = Camera.FND.SEG_LOOKUP.Clone();
            if (File.Exists(EditModel.Path))
            {
                saveLabel.Visibility = Visibility.Visible;
                await Task.Delay(100);
                EditModel.SaveTo(EditModel.Path);
                Utility.Debug.Write("MODEL: Saved " + System.IO.Path.GetFileName(EditModel.Path), Utility.Debug.ContentType.Notify);
                //Program.OnEditModelSave();
                await Task.Delay(100);
                saveLabel.Visibility = Visibility.Hidden;
            }
            else
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.InitialDirectory = VTMProgram.FolderMap.RootFolder;
                saveFileDialog.AddExtension = true;
                saveFileDialog.DefaultExt = FolderMap.DefaultModelFileExt;
                if ((bool)saveFileDialog.ShowDialog())
                {
                    saveLabel.Visibility = Visibility.Visible;
                    await Task.Delay(100);
                    EditModel.Name = saveFileDialog.SafeFileName;
                    EditModel.Path = saveFileDialog.FileName;
                    EditModel.SaveTo(saveFileDialog.FileName);
                    Utility.Debug.Write("MODEL: Saved " + saveFileDialog.SafeFileName, Utility.Debug.ContentType.Notify);
                    await Task.Delay(100);
                    saveLabel.Visibility = Visibility.Hidden;
                }
            }
            Program.OnEditModelSave();
        }

        private async void btSaveAsModel_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep != null)
            {
                if (currentStep.cmd == CMDs.LED)
                {
                    if (currentStep.LedList.Count > 0)
                    {
                        currentStep.LedList.Clear();

                        foreach (var item in LEDsData.Items)
                        {
                            var led = (item as Camera.SingleLED);
                            //var ledClone = led.Clone();
                            if (led.Use)
                            {
                                currentStep.LedList.Add(led.Clone());
                                // parentCanvas == null it canot clone
                            }
                        }
                    }
                }

                if (currentStep.cmd == CMDs.LCD)
                {
                    var lcdList = VisionBuider.Models.LCDs;

                    currentStep.LCDRoiValue0 = lcdList[0].Rect;
                    currentStep.LCDRoiValue1 = lcdList[1].Rect;
                    currentStep.LCDRoiValue2 = lcdList[2].Rect;
                    currentStep.LCDRoiValue3 = lcdList[3].Rect;
                }

                // See btSaveModel_Click - a freshly retyped FND step needs its 7 slots before this indexes them.
                if (currentStep.cmd == CMDs.FND)
                {
                    currentStep.InitialFND();
                    int index_char = 0;

                    foreach (var FNDchar in VisionBuider.Models.FNDs)
                    {
                        if (EditModel.Layout.PCB_Count >= 1)
                        {
                            for (int index_led = 0; index_led < 7; index_led++)
                            {
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].X = FNDchar[0].PointSegments.LEDs[index_led].X;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Y = FNDchar[0].PointSegments.LEDs[index_led].Y;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Dir = FNDchar[0].PointSegments.LEDs[index_led].Dir;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].ON = FNDchar[0].PointSegments.LEDs[index_led].ON;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].OFF = FNDchar[0].PointSegments.LEDs[index_led].OFF;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Thresh = FNDchar[0].PointSegments.LEDs[index_led].Thresh;
                                currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Use = FNDchar[0].PointSegments.LEDs[index_led].Use;
                            }

                            currentStep.RectFNDsBoard0[index_char] = FNDchar[0].rect;
                            //currentStep.FNDsBoard0[index_char].rect = FNDchar[0].rect;
                            currentStep.UseFNDsBoard0[index_char] = FNDchar[0].Use;
                            //currentStep.FNDsBoard0[index_char].Use = FNDchar[0].Use;
                        }

                        index_char++;
                    }
                }
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.InitialDirectory = VTMProgram.FolderMap.RootFolder;
            saveFileDialog.AddExtension = true;
            saveFileDialog.DefaultExt = FolderMap.DefaultModelFileExt;
            if ((bool)saveFileDialog.ShowDialog())
            {
                saveLabel.Visibility = Visibility.Visible;
                await Task.Delay(100);
                EditModel.Name = saveFileDialog.SafeFileName;
                EditModel.Path = saveFileDialog.FileName;
                CommitCameraToModel();   // persist the CURRENT slider values (not a fresh camera read)
                EditModel.ModelSegmentLookup = Camera.FND.SEG_LOOKUP.Clone();
                EditModel.SaveTo(saveFileDialog.FileName);
                Utility.Debug.Write("MODEL: Saved " + saveFileDialog.SafeFileName, Utility.Debug.ContentType.Notify);
                saveLabel.Visibility = Visibility.Hidden;
                await Task.Delay(100);
            }
            Program.OnEditModelSave();
        }

        private void LEDsData_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            if (LEDsData.SelectedItem != null)
            {
                LEDsData.ScrollIntoView(LEDsData.SelectedItem);
            }
        }

        private void waitCheckboxLED_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var item in LEDsData.Items)
            {
                (item as Camera.SingleLED).Use = false;
            }
        }

        private void waitCheckboxLED_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var item in LEDsData.Items)
            {
                (item as Camera.SingleLED).Use = true;
            }
        }

        private void waitCheckboxGLED_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var item in GLEDsData.Items)
            {
                (item as Camera.SingleGLED).Use = false;
            }
        }

        private void waitCheckboxGLED_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var item in GLEDsData.Items)
            {
                (item as Camera.SingleGLED).Use = true;
            }
        }

        private void UpdateLayout(int PCB_Count)
        {
            tgbSelectA.Visibility = PCB_Count >= 1 ? Visibility.Visible : Visibility.Collapsed;
            tgbSelectB.Visibility = PCB_Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
            tgbSelectC.Visibility = PCB_Count >= 3 ? Visibility.Visible : Visibility.Collapsed;
            tgbSelectD.Visibility = PCB_Count >= 4 ? Visibility.Visible : Visibility.Collapsed;

            // The board filter alone leaves every ROI family on screen; re-apply the cmd filter after it.
            ShowRoisFor(currentStep);
        }

        // Make sure an FND step really has its 7 slots, and report whether it is safe to index.
        // InitialFND() is idempotent, so this fills in a step just retyped to FND and leaves an existing store
        // alone; HasFndStore then covers a truncated or hand-edited file that InitialFND cannot repair.
        private static bool Fnd(Step step)
        {
            if (step == null) return false;
            step.InitialFND();
            return step.HasFndStore;
        }

        // Draw only the ROI family the selected step actually uses - and nothing at all when no step is selected.
        //
        // The canvas is shared by every family and VisionModel's constructor builds ALL of them up front (28 FND
        // boxes + 4 LCDs + 4 LEDs x32 + 4 GLEDs x32) regardless of what any step needs, while the only filter
        // that existed - VisionModel.UpdateLayout - gates on the BOARD index and has never known about cmd. That
        // is why opening the page with nothing selected showed a canvas full of every family's default ROIs.
        //
        // Visibility only: this touches nothing that is saved, so it cannot alter a model.
        // The detect-option panel (VisionModel.Option) is a SINGLE reused control. It lives in the LCD tab for an
        // LCD step and in the FND tab for an FND step, so it must be reparented per-cmd - a WPF element has exactly
        // one parent. Detach-then-attach is the same single-parent-safe pattern as AttachVisionTester; assigning a
        // control that still has a parent would throw. Idempotent: no-op when it is already in the right holder.
        private void PlaceDetectPanel(CMDs? family)
        {
            // The detect-option panel is LCD-only now (FND no longer shows it), so it always lives in the LCD tab's
            // holder (componentOptionHolder). The `family` arg is no longer used for placement.
            var opt = VisionBuider?.Models?.Option;
            if (opt == null || componentOptionHolder == null) return;
            // Wire the panel's Copy-to-Oper event to THIS instance. Idempotent (unsubscribe first): the model - and
            // therefore Option - is a new instance per loaded model, and this runs on every step selection.
            opt.CopyToOperRequested -= Option_CopyToOperRequested;
            opt.CopyToOperRequested += Option_CopyToOperRequested;
            if (componentOptionHolder.Child != opt) componentOptionHolder.Child = opt;
        }

        // The LCD/FND detect panel copied its live reading into its own SpectString and asks us to write the step's
        // Oper (the expected value FND/LED/LCD actually compare - see CopyDetectedToOper). Same target as the value
        // Copy buttons; keeps the visible "Oper" column and the test in sync.
        private void Option_CopyToOperRequested(object sender, string detected)
        {
            var step = currentStep ?? (VisionStepsGrid != null ? VisionStepsGrid.SelectedItem as Step : null);
            if (step == null)
            {
                Utility.Debug.Write("VISION:COPY - no step selected", Utility.Debug.ContentType.Warning);
                return;
            }
            step.Oper = detected ?? "";
        }

        // Copy the CURRENT (slider-updated) values into EditModel.CameraSetting so Save Model persists them. Called
        // from the Save / Save As paths. Does NOT call GetParammeter() - that re-reads the live camera, which under
        // the on-demand model still holds the OLD values until "Write Setting to Camera", so it would overwrite the
        // slider changes and save the wrong values (the reported bug).
        private void CommitCameraToModel()
        {
            var live = cameraSetting?.Capture?.cameraSetting;
            if (EditModel?.CameraSetting == null || live == null) return;
            var m = EditModel.CameraSetting;
            m.Exposure = live.Exposure;
            m.Brightness = live.Brightness;
            m.Contrast = live.Contrast;
            m.Saturation = live.Saturation;
            m.WBTemperature = live.WBTemperature;
            m.Sharpness = live.Sharpness;
            m.Focus = live.Focus;
            m.Zoom = live.Zoom;
            m.Gain = live.Gain;
            m.Backlight = live.Backlight;
            EditModel.HaveApplyCamsetting = true;
            Utility.Debug.Write("CAMERA: saved to model (Bri=" + m.Brightness + " Con=" + m.Contrast + " Exp="
                + m.Exposure + " Foc=" + m.Focus + " WB=" + m.WBTemperature + " Gain=" + m.Gain + ")",
                Utility.Debug.ContentType.Notify);
        }

        // "Write Setting to Camera": push the current slider values to the live camera (sliders alone no longer
        // apply on drag).
        private void btnApplyCameraManual_Click(object sender, RoutedEventArgs e)
        {
            cameraSetting?.ApplyManual();
        }

        // "Read Setting From Camera": pull the camera's current settings into the sliders (e.g. after tuning via the
        // camera driver dialog). Save Model then persists them.
        private void btnReadCameraSettings_Click(object sender, RoutedEventArgs e)
        {
            cameraSetting?.GetcameraSettingValue();
            Utility.Debug.Write("CAMERA: settings read from camera into sliders",
                                    Utility.Debug.ContentType.Notify);
        }

        private void ShowRoisFor(Step step)
        {
            var m = VisionBuider?.Models;
            if (m == null) return;

            CMDs? family = step == null ? (CMDs?)null : step.cmd;
            int boards = EditModel != null && EditModel.Layout != null ? EditModel.Layout.PCB_Count : 0;

            for (int i = 0; i < 4; i++)
            {
                bool onBoard = boards > i;
                Visibility fnd = onBoard && family == CMDs.FND ? Visibility.Visible : Visibility.Collapsed;
                Visibility lcd = onBoard && family == CMDs.LCD ? Visibility.Visible : Visibility.Collapsed;
                Visibility led = onBoard && family == CMDs.LED ? Visibility.Visible : Visibility.Collapsed;

                // An FND char shows only if the family is on AND that char is used - an unused char is not drawn
                // at all (box included), not shown greyed. FND.Visibility then cascades to the char's segments.
                foreach (var fndChar in m.FNDs)
                    fndChar[i].Visibility = (fnd == Visibility.Visible && fndChar[i].Use)
                        ? Visibility.Visible : Visibility.Collapsed;
                m.LCDs[i].Visibility = lcd;
                m.LED[i].Visibility = led;
                // GLED is retired - no deployed model uses it, so it is never drawn. Its CMDs slot stays put.
                m.GLED[i].Visibility = Visibility.Collapsed;
            }

            // Same rule for the editor tabs, decided here so the tabs and the canvas can never disagree about
            // which family the step owns. Nothing selected -> every tab hidden.
            if (tabLcdFnd != null)
            {
                // Family-scoped tabs: LCD tab for an LCD step; FND tab (detect + lookup) and FND Segments for an FND
                // step; LEDs for LED. Nothing selected -> every tab hidden. The shared detect panel is reparented
                // into whichever of the LCD / FND holders is currently on screen.
                tabLcdFnd.Visibility = family == CMDs.LCD ? Visibility.Visible : Visibility.Collapsed;
                tabFnd.Visibility = family == CMDs.FND ? Visibility.Visible : Visibility.Collapsed;
                tabFndSegments.Visibility = family == CMDs.FND ? Visibility.Visible : Visibility.Collapsed;
                tabLeds.Visibility = family == CMDs.LED ? Visibility.Visible : Visibility.Collapsed;
                // tabGleds stays Collapsed from XAML - GLED is retired.

                PlaceDetectPanel(family);

                // The detect panel is LCD-only; point it at this step's LCD component (site A) so it shows the LCD,
                // not the default FND1. The ROI's Selected event only fires on a click, so a step change alone would
                // otherwise leave the panel on whatever component was last clicked.
                if (family == CMDs.LCD && m.LCDs != null && m.LCDs.Count > 0 && m.Option != null)
                {
                    // Spect string mirrors the step's Oper (the expected value), NOT the LCD component's stale "8888"
                    // default. Set it before binding so the panel shows Oper on load. (Editing the box then syncs the
                    // other way via SpectText_TextChanged -> Oper.)
                    if (step != null) m.LCDs[0].SpectString = step.Oper ?? "";
                    m.Option.SetDataContext(m.LCDs[0]);
                }

                // Land on the family's primary editing tab: FND -> FND Segments (ROI grids), LED -> LEDs, LCD -> LCD.
                if (tabVisionFamilies != null)
                {
                    System.Windows.Controls.TabItem target =
                        family == CMDs.FND ? tabFndSegments :
                        family == CMDs.LED ? tabLeds :
                        family == CMDs.LCD ? tabLcdFnd : null;
                    if (target != null && target.Visibility == Visibility.Visible)
                        tabVisionFamilies.SelectedItem = target;
                }
            }
        }

        private void BoardSelect_Click(object sender, RoutedEventArgs e)
        {
            var bt = (sender as ToggleButton);
            tgbSelectA.IsChecked = bt == tgbSelectA;
            tgbSelectB.IsChecked = bt == tgbSelectB;
            tgbSelectC.IsChecked = bt == tgbSelectC;
            tgbSelectD.IsChecked = bt == tgbSelectD;
            switch (bt.Name)
            {
                case "tgbSelectA":
                    DataGrid_FND_0.ItemsSource = VisionBuider.Models.FNDs[0][0].PointSegments.LEDs;
                    DataGrid_FND_1.ItemsSource = VisionBuider.Models.FNDs[1][0].PointSegments.LEDs;
                    DataGrid_FND_2.ItemsSource = VisionBuider.Models.FNDs[2][0].PointSegments.LEDs;
                    DataGrid_FND_3.ItemsSource = VisionBuider.Models.FNDs[3][0].PointSegments.LEDs;
                    DataGrid_FND_4.ItemsSource = VisionBuider.Models.FNDs[4][0].PointSegments.LEDs;
                    DataGrid_FND_5.ItemsSource = VisionBuider.Models.FNDs[5][0].PointSegments.LEDs;
                    DataGrid_FND_6.ItemsSource = VisionBuider.Models.FNDs[6][0].PointSegments.LEDs;

                    GLEDsData.ItemsSource = VisionBuider.Models.GLED[0].GLEDs;
                    LEDsData.ItemsSource = VisionBuider.Models.LED[0].LEDs;
                    break;

                case "tgbSelectB":
                    DataGrid_FND_0.ItemsSource = VisionBuider.Models.FNDs[0][1].PointSegments.LEDs;
                    DataGrid_FND_1.ItemsSource = VisionBuider.Models.FNDs[1][1].PointSegments.LEDs;
                    DataGrid_FND_2.ItemsSource = VisionBuider.Models.FNDs[2][1].PointSegments.LEDs;
                    DataGrid_FND_3.ItemsSource = VisionBuider.Models.FNDs[3][1].PointSegments.LEDs;
                    DataGrid_FND_4.ItemsSource = VisionBuider.Models.FNDs[4][1].PointSegments.LEDs;
                    DataGrid_FND_5.ItemsSource = VisionBuider.Models.FNDs[5][1].PointSegments.LEDs;
                    DataGrid_FND_6.ItemsSource = VisionBuider.Models.FNDs[6][1].PointSegments.LEDs;

                    GLEDsData.ItemsSource = VisionBuider.Models.GLED[1].GLEDs;
                    LEDsData.ItemsSource = VisionBuider.Models.LED[1].LEDs;
                    break;

                case "tgbSelectC":
                    DataGrid_FND_0.ItemsSource = VisionBuider.Models.FNDs[0][2].PointSegments.LEDs;
                    DataGrid_FND_1.ItemsSource = VisionBuider.Models.FNDs[1][2].PointSegments.LEDs;
                    DataGrid_FND_2.ItemsSource = VisionBuider.Models.FNDs[2][2].PointSegments.LEDs;
                    DataGrid_FND_3.ItemsSource = VisionBuider.Models.FNDs[3][2].PointSegments.LEDs;
                    DataGrid_FND_4.ItemsSource = VisionBuider.Models.FNDs[4][2].PointSegments.LEDs;
                    DataGrid_FND_5.ItemsSource = VisionBuider.Models.FNDs[5][2].PointSegments.LEDs;
                    DataGrid_FND_6.ItemsSource = VisionBuider.Models.FNDs[6][2].PointSegments.LEDs;

                    GLEDsData.ItemsSource = VisionBuider.Models.GLED[2].GLEDs;
                    LEDsData.ItemsSource = VisionBuider.Models.LED[2].LEDs;
                    break;

                case "tgbSelectD":
                    DataGrid_FND_0.ItemsSource = VisionBuider.Models.FNDs[0][3].PointSegments.LEDs;
                    DataGrid_FND_1.ItemsSource = VisionBuider.Models.FNDs[1][3].PointSegments.LEDs;
                    DataGrid_FND_2.ItemsSource = VisionBuider.Models.FNDs[2][3].PointSegments.LEDs;
                    DataGrid_FND_3.ItemsSource = VisionBuider.Models.FNDs[3][3].PointSegments.LEDs;
                    DataGrid_FND_4.ItemsSource = VisionBuider.Models.FNDs[4][3].PointSegments.LEDs;
                    DataGrid_FND_5.ItemsSource = VisionBuider.Models.FNDs[5][3].PointSegments.LEDs;
                    DataGrid_FND_6.ItemsSource = VisionBuider.Models.FNDs[6][3].PointSegments.LEDs;

                    GLEDsData.ItemsSource = VisionBuider.Models.GLED[3].GLEDs;
                    LEDsData.ItemsSource = VisionBuider.Models.LED[3].LEDs;
                    break;

                default:
                    break;
            }
        }

        private void Layout_PCB_COUNT_CHANGE(object sender, EventArgs e)
        {
            UpdateLayout(EditModel.Layout.PCB_Count);
        }

        private void btGetValue_Click(object sender, RoutedEventArgs e)
        {
            Program.EditModel.VisionModels.GetLEDSampleImage(Program.Capture?.LastMatFrame);
            int b = SelectedBoardIndex();
            if (b < 0) return;
            lbLEDvalue.Content = Program.EditModel.VisionModels.LED[b].CalculatorOutputString;
        }

        private void btGetGLEDValue_Click(object sender, RoutedEventArgs e)
        {
            Program.EditModel.VisionModels.GetGLEDSampleImage(Program.Capture?.LastMatFrame);
            int b = SelectedBoardIndex();
            if (b < 0) return;
            lbGLEDvalue.Content = Program.EditModel.VisionModels.GLED[b].CalculatorOutputString;
        }

        private void btThresholdCalculate_Click(object sender, RoutedEventArgs e)
        {
            int b = SelectedBoardIndex();
            if (b < 0) return;
            Program.EditModel.VisionModels.LED[b].CALC_THRESH();
        }

        private void btGLEDThresholdCalculate_Click(object sender, RoutedEventArgs e)
        {
            int b = SelectedBoardIndex();
            if (b < 0) return;
            Program.EditModel.VisionModels.GLED[b].CALC_THRESH();
        }

        private void cbbTxnaming_Mainual_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbbTxnaming_Manual.SelectedItem != null)
            {
                var txData = EditModel.Naming.TxDatas.Where(o => o.Name == (string)cbbTxnaming_Manual.SelectedItem).First();
                if (txData != null)
                {
                    var data = cbbUUTconfig_Manual.Text == "P1" ? EditModel.P1_Config.GetFrame(txData.Data) : EditModel.P2_Config.GetFrame(txData.Data);
                    string dataStr = "";
                    foreach (var item in data)
                    {
                        dataStr += item.ToString("X2") + " ";
                    }
                    lbTxData.Content = dataStr;
                }
            }
        }

        private void ClearUUTLog_Click(object sender, RoutedEventArgs e)
        {
            if (Program?.UUTs == null) return;
            foreach (var uut in Program.UUTs) uut.ClearLog();
        }

        private void ButtonSendUUT_Click(object sender, RoutedEventArgs e)
        {
            if (cbbTxnaming_Manual.SelectedItem != null)
            {
                var txData = EditModel.Naming.TxDatas.Where(o => o.Name == (string)cbbTxnaming_Manual.SelectedItem).First();
                foreach (var item in Program.UUTs)
                {
                    if (cbbUUTconfig_Manual.Text == "P1" && item.Config != EditModel.P1_Config)
                    {
                        item.Config = EditModel.P1_Config;
                    }
                    else if (cbbUUTconfig_Manual.Text == "P2")
                    {
                        item.Config = EditModel.P1_Config;
                    }
                }
                if (txData != null)
                {
                    if (EditModel.Layout.PCB_Count >= 1) Program.UUTs[0].Send(txData);
                    if (EditModel.Layout.PCB_Count >= 2) Program.UUTs[1].Send(txData);
                    if (EditModel.Layout.PCB_Count >= 3) Program.UUTs[2].Send(txData);
                    if (EditModel.Layout.PCB_Count >= 4) Program.UUTs[3].Send(txData);
                }
            }
        }

        private void VisionStepsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // when user change step
            // then buf has the checkbox value (checked or not checked)
            //waitCheckbox1.Checked = true;
            // previous, current
            // previous save roi
            // if previous = current

            // --- LED ---
            // step's led.isUse[] (which is the index of the led so i can both turn on/off (visually) as well as retrieve data properly)
            // so that i can turn the using led's for each step and get their values

            // 스탭 당 led 만 보여주기

            currentStep = (sender as DataGrid).SelectedItem as Step;

            // 새로운 row 선택할때
            // Write the live editor copy back into the step being LEFT - but ONLY the family that step's cmd
            // owns. This used to copy all three families into every step regardless of cmd (Save Model has
            // always been cmd-guarded; this path never was), which is exactly how a full FND store ended up on
            // every SND/DLY/LED step of every model. It is also now load-bearing: a non-FND step's FNDsBoard0 is
            // empty, and the FND block below indexes buf.FNDsBoard0[index_char] with a hardcoded 7 iterations,
            // so without this guard leaving a non-FND step throws ArgumentOutOfRangeException.
            if (buf != null && buf.cmd == CMDs.LED)
            {
                buf.LedList.Clear();

                foreach (var item in LEDsData.Items)
                {
                    var led = (item as Camera.SingleLED);
                    //var ledClone = led.Clone();
                    if (led.Use)
                    {
                        buf.LedList.Add(led.Clone());
                        // parentCanvas == null it canot clone
                    }
                }
            }

            if (buf != null && buf.cmd == CMDs.LCD)
            {
                var lcdList = VisionBuider.Models.LCDs;

                buf.LCDRoiValue0 = lcdList[0].Rect;
                buf.LCDRoiValue1 = lcdList[1].Rect;
                buf.LCDRoiValue2 = lcdList[2].Rect;
                buf.LCDRoiValue3 = lcdList[3].Rect;
            }

            // InitialFND() fills the store if the step was just retyped to FND (idempotent, so an existing one is
            // untouched); HasFndStore is the airbag in case it is still short - the loop below indexes a
            // hardcoded 7 times, bounded by the LIVE model rather than by buf's own Count.
            if (buf != null && buf.cmd == CMDs.FND && Fnd(buf))
            {
                int index_char = 0;

                foreach (var FNDchar in VisionBuider.Models.FNDs)
                {
                    if (EditModel.Layout.PCB_Count >= 1)
                    {
                        for (int index_led = 0; index_led < 7; index_led++)
                        {
                            buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].X = FNDchar[0].PointSegments.LEDs[index_led].X;
                            buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Y = FNDchar[0].PointSegments.LEDs[index_led].Y;
                            buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Dir = FNDchar[0].PointSegments.LEDs[index_led].Dir;
                            buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].ON = FNDchar[0].PointSegments.LEDs[index_led].ON;
                            buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].OFF = FNDchar[0].PointSegments.LEDs[index_led].OFF;
                            buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Thresh = FNDchar[0].PointSegments.LEDs[index_led].Thresh;
                            buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Use = FNDchar[0].PointSegments.LEDs[index_led].Use;
                        }

                        buf.UseFNDsBoard0[index_char] = buf.FNDsBoard0[index_char].PointSegments.LEDs.Any(led => led.Use);
                        // Save the char box too, so dragging the FND box survives a step switch (not only Save
                        // Model). The load reads this back; without it the box reverted while the segments moved.
                        buf.RectFNDsBoard0[index_char] = FNDchar[0].rect;
                    }

                    index_char++;
                }
            }

            // buf 없을때
            if (buf != currentStep && currentStep != null)
            {
                buf = currentStep;

                foreach (var item in LEDsData.Items)
                {
                    (item as Camera.SingleLED).Use = false;
                }

                // LEDs = 32
                if (buf.LedList.Count > 0)
                {
                    foreach (var item in buf.LedList)
                    {
                        var led = (LEDsData.Items[item.Index] as Camera.SingleLED);
                        led.X = item.X;
                        led.Y = item.Y;
                        led.Dir = item.Dir;
                        led.ON = item.ON;
                        led.OFF = item.OFF;
                        led.Thresh = item.Thresh;
                        led.Use = item.Use;
                    }
                }

                if (buf.cmd == CMDs.LCD)
                {
                    var lcdList = VisionBuider.Models.LCDs;

                    // Null = the step has no LCD ROI stored; leave the live one alone rather than read .Value.
                    if (buf.LCDRoiValue0.HasValue) lcdList[0].Rect = buf.LCDRoiValue0.Value;
                    if (buf.LCDRoiValue1.HasValue) lcdList[1].Rect = buf.LCDRoiValue1.Value;
                    if (buf.LCDRoiValue2.HasValue) lcdList[2].Rect = buf.LCDRoiValue2.Value;
                    if (buf.LCDRoiValue3.HasValue) lcdList[3].Rect = buf.LCDRoiValue3.Value;
                }

                // Only an FND step has an FND store to load from. A non-FND step's FNDsBoard0 is empty, and this
                // loop indexes it with a hardcoded 7 iterations bounded by the LIVE model - selecting such a step
                // without this guard throws ArgumentOutOfRangeException.
                // Only an FND step has an FND store to load from, and only a complete one is safe to index -
                // this loop runs a hardcoded 7 times bounded by the LIVE model, not by buf's own Count.
                int index_char = 0;
                foreach (var FNDchar in Fnd(buf) ? VisionBuider.Models.FNDs : new List<List<Camera.FND>>())
                {
                    if (EditModel.Layout.PCB_Count >= 1)
                    {
                        for (int index_led = 0; index_led < 7; index_led++)
                        {
                            FNDchar[0].PointSegments.LEDs[index_led].X = buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].X;
                            FNDchar[0].PointSegments.LEDs[index_led].Y = buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Y;
                            FNDchar[0].PointSegments.LEDs[index_led].Dir = buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Dir;
                            FNDchar[0].PointSegments.LEDs[index_led].ON = buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].ON;
                            FNDchar[0].PointSegments.LEDs[index_led].OFF = buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].OFF;
                            FNDchar[0].PointSegments.LEDs[index_led].Thresh = buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Thresh;
                            FNDchar[0].PointSegments.LEDs[index_led].Use = buf.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Use;
                        }

                        // Load the char box from the step, like every other field here. This line used to be
                        // `FNDchar[0].Rect = FNDchar[0].Rect;` - a self-assign, with the real load commented out
                        // beneath it - so the live FND box never followed the selected step: it was a
                        // session-global that Save then stamped onto whichever FND step happened to be current.
                        FNDchar[0].Rect = buf.RectFNDsBoard0[index_char];
                        // Char Use from the stored per-char flag (matches AutoPage's load and the header
                        // checkboxes), not re-derived from the segments.
                        FNDchar[0].Use = buf.UseFNDsBoard0[index_char];
                    }

                    index_char++;
                }
            }

            // Only the newly selected step's family stays on the canvas.
            ShowRoisFor(currentStep);
            RefreshFndUseChecks();

        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            // Traverse up the visual tree
            while (child != null)
            {
                if (child is T parent)
                {
                    return parent;
                }
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void FNDSegmentsData_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            if (dataGrid != null)
            {
                if (dataGrid.SelectedItem != null)
                {
                    dataGrid.ScrollIntoView(dataGrid.SelectedItem);
                }
            }
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var FNDchar in VisionBuider.Models.FNDs)
            {
                if (tgbSelectA.IsChecked == true)
                {
                    FNDchar[0].Use = FNDchar[0].PointSegments.LEDs.Any(led => led.Use);
                }
            }
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var FNDchar in VisionBuider.Models.FNDs)
            {
                if (tgbSelectA.IsChecked == true)
                {
                    FNDchar[0].Use = FNDchar[0].PointSegments.LEDs.Any(led => led.Use);
                }
            }
        }

        // ---- Use all / Use none (icon buttons on the FND Segments and LEDs tabs) ----
        // These drive the LIVE model, which is what the selection-change writeback and Save then copy into the
        // step - the same path the per-row checkboxes use. They replace the tri-state checkbox that used to live
        // in the column header.

        private void LedUseAll_Click(object sender, RoutedEventArgs e) { SetLedUse(true); }
        private void LedUseNone_Click(object sender, RoutedEventArgs e) { SetLedUse(false); }

        private void SetLedUse(bool use)
        {
            if (LEDsData == null) return;
            foreach (var item in LEDsData.Items)
            {
                var led = item as Camera.SingleLED;
                if (led != null) led.Use = use;
            }
        }

        // Copy the live reading (already updated in realtime by the vision timer) into the selected step's
        // Condition1 - a teach gesture: capture the current value as the expected one.
        private void CopyLedValueToCondition1_Click(object sender, RoutedEventArgs e)
        {
            CopyDetectedToOper(lbLEDvalue);
        }

        private void CopyFndValueToCondition1_Click(object sender, RoutedEventArgs e)
        {
            CopyDetectedToOper(lbMatrixPointValue);
        }

        // Teach the expected value from the live reading. The target is Oper, NOT Condition1: FND/LED/LCD all
        // declare Oper as the expected value ("String detected" / "LED hex data") and leave Condition1 "not use"
        // (Commands.cs). Writing Condition1 was the bug - it landed in a field the check never reads and that no
        // column in this grid shows, so it looked like nothing happened. Oper raises PropertyChanged, so the
        // step-grid Oper column refreshes immediately.
        private void CopyDetectedToOper(System.Windows.Controls.Label valueLabel)
        {
            // Fall back to the grid selection: currentStep is only set by the step grid's SelectionChanged, which
            // may not have fired if the operator never touched that grid this session.
            var step = currentStep ?? (VisionStepsGrid != null ? VisionStepsGrid.SelectedItem as Step : null);
            if (step == null)
            {
                Utility.Debug.Write("VISION:COPY - no step selected", Utility.Debug.ContentType.Warning);
                return;
            }
            string value = valueLabel != null && valueLabel.Content != null ? valueLabel.Content.ToString() : "";
            step.Oper = value;
        }

        // One Use checkbox per FND char, sitting in each sub-tab header (chkFndUse0..6, Tag = char index).
        // All-or-nothing per char: ticking it turns on every segment of that char, unticking turns them all off.
        // Uses Click (not Checked/Unchecked) so setting IsChecked from code in RefreshFndUseChecks does not
        // re-fire the handler.
        private void FndCharUse_Click(object sender, RoutedEventArgs e)
        {
            var chk = sender as System.Windows.Controls.CheckBox;
            if (chk == null || chk.Tag == null) return;
            int charIndex;
            if (!int.TryParse(chk.Tag.ToString(), out charIndex)) return;
            SetFndUse(charIndex, chk.IsChecked == true);
        }

        private void SetFndUse(int charIndex, bool use)
        {
            var m = VisionBuider?.Models;
            if (m == null || charIndex < 0 || charIndex >= m.FNDs.Count) return;

            var fnd = m.FNDs[charIndex][0];
            foreach (var seg in fnd.PointSegments.LEDs) seg.Use = use;
            fnd.Use = use;
        }

        // Point each header checkbox at its char's current Use state. Call after loading a step so the ticks
        // match the model. Click-driven, so assigning IsChecked here does not loop back into SetFndUse.
        private void RefreshFndUseChecks()
        {
            var m = VisionBuider?.Models;
            if (m == null) return;
            var boxes = new[] { chkFndUse0, chkFndUse1, chkFndUse2, chkFndUse3, chkFndUse4, chkFndUse5, chkFndUse6 };
            for (int i = 0; i < boxes.Length && i < m.FNDs.Count; i++)
            {
                if (boxes[i] != null) boxes[i].IsChecked = m.FNDs[i][0].Use;
            }
        }

        // (removed FND_Use_HeaderCheckBox_Checked/_Unchecked - the per-char header checkboxes they served were
        //  replaced by the FndUseAll/FndUseNone buttons above.)

        private void VisibilityCheckboxFNDSegment_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void VisibilityCheckboxFNDSegment_Unchecked(object sender, RoutedEventArgs e)
        {
        }

        private void btGetFNDSegmentValue_Click(object sender, RoutedEventArgs e)
        {
            Program.EditModel.VisionModels.GetFNDSampleImage(Program.Capture?.LastMatFrame);
            int b = SelectedBoardIndex();
            if (b < 0) return;
            lbMatrixPointValue.Content = "";
            for (int i = 0; i < Program.EditModel.VisionModels.FNDs.Count; i++)
            {
                // was FNDs[i][b].DetectedString - the old unrolled tgbSelectD branch read char index [2] not [3].
                lbMatrixPointValue.Content += Program.EditModel.VisionModels.FNDs[i][b].DetectedString;
            }
        }

        private void btFNDSegmentThresholdCalculate_Click(object sender, RoutedEventArgs e)
        {
            int b = SelectedBoardIndex();
            if (b < 0) return;
            for (int i = 0; i < Program.EditModel.VisionModels.FNDs.Count; i++)
            {
                Program.EditModel.VisionModels.FNDs[i][b].PointSegments.CALC_THRESH();
            }
        }

        private ScaleTransform scaleTransform;
        private TranslateTransform translateTransform;
        private const double MinScale = 1.0;
        private const double MaxScale = 5.0;

        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoomFactor = e.Delta > 0 ? 0.1 : -0.1;
            double newScale = scaleTransform.ScaleX + zoomFactor;

            if (newScale < MinScale || newScale > MaxScale)
                return;

            Point cursorPosition = e.GetPosition(mainCanvas);

            double relativeX = (cursorPosition.X - translateTransform.X) / scaleTransform.ScaleX;
            double relativeY = (cursorPosition.Y - translateTransform.Y) / scaleTransform.ScaleY;

            scaleTransform.ScaleX = newScale;
            scaleTransform.ScaleY = newScale;

            translateTransform.X = cursorPosition.X - relativeX * newScale;
            translateTransform.Y = cursorPosition.Y - relativeY * newScale;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            scaleTransform.ScaleX = 1.0;
            scaleTransform.ScaleY = 1.0;
            translateTransform.X = 0;
            translateTransform.Y = 0;
        }
    }
}