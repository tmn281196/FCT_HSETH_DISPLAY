using VTMControls.DeviceControl.VisionTest;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;



namespace VTMControls.DeviceControl
{

    public class VisionModel
    {

        public const int CHARNUMBER = 7;
        public const double FND_WIDTH = 20;
        public const double FND_HEIGHT = 30;
        public const int FND_DIAMETER_POINT = 8;
        public const int FND_THRESH_POINT = 80;



        private List<List<FND>> _FNDs { set; get; } = new List<List<FND>>();

        public List<List<FND>> FNDs
        {
            get { return _FNDs; }
            set
            {
                if (value != null || value != _FNDs)
                {
                    _FNDs = value;
                    for (int index_char = 0; index_char < _FNDs.Count; index_char++)
                    {
                        for (int index_board=0; index_board < 4; index_board++)
                        {
                            FNDs[index_char][index_board].Selected += VisionModel_Selected;
                        }
                    }
                }
            }
        }


        private List<LCD> _LCDs = new List<LCD>();
        public List<LCD> LCDs
        {
            get { return _LCDs; }
            set
            {
                if (value != null || value != _LCDs)
                {
                    _LCDs = value;
                    for (int i = 0; i < 4; i++)
                    {
                        LCDs[i].Selected += VisionModel_Selected;
                    }
                }
            }
        }


        private List<LED> _LED = new List<LED>();
        public List<LED> LED
        {
            get { return _LED; }
            set
            {
                if (value != null || value != _LED) _LED = value;
            }
        }

        private List<GLED> _GLED = new List<GLED>();
        public List<GLED> GLED
        {
            get { return _GLED; }
            set
            {
                if (value != null || value != _GLED) _GLED = value;
            }
        }

        public VisionModelOption Option = new VisionModelOption();


        public VisionModel()
        {
            for (int fndChar_index = 0; fndChar_index < CHARNUMBER; fndChar_index++)
            {
                List<FND> FNDchar = new List<FND>();


                for (int i = 0; i < 4; i++)
                {
                    FND fnd = new FND(fndChar_index);
                    fnd.Selected += VisionModel_Selected;
                    fnd.Use = false;
                    FNDchar.Add(fnd);
                }

                FNDs.Add(FNDchar);
            }
            for (int i = 0; i < 4; i++)
            {
                LCDs.Add(new LCD(i));
                LCDs[i].Selected += VisionModel_Selected;
                GLED.Add(new GLED(new System.Windows.Point(5, 5 + 25 * i)));
                LED.Add(new LED(new System.Windows.Point(5, 100 + 25 * i)));

            }
            Option.DataContext = FNDs.First().First();

        }

        // The FND char box currently selected ON THE CANVAS, or null when nothing FND is selected. This is what
        // the FND copy/paste of shapes works on - the operator points at the box on the image rather than
        // relying on which FND1..FND7 tab happens to be open.
        public FND SelectedFnd { get; private set; }

        private void VisionModel_Selected(object sender, EventArgs e)
        {
            FND tryCatchFND = (sender as FND);
            if (tryCatchFND != null)
            {
                Option.SetDataContext(tryCatchFND);
                SelectedFnd = tryCatchFND;
                ShowFndCaptionFor(tryCatchFND);
            }
            LCD tryCatchLCD = (sender as LCD);
            if (tryCatchLCD != null)
            {
                Option.SetDataContext(tryCatchLCD);
                SelectedFnd = null;
                ShowFndCaptionFor(null);   // selection moved off the FNDs - drop their caption
                ShowLedCaptionFor(null);
            }
        }

        // A standalone LED probe was clicked on the canvas: caption that one and clear every other probe, plus
        // the FND captions, since the selection has moved.
        private void SingleLed_Selected(object sender, EventArgs e)
        {
            ShowLedCaptionFor(sender as SingleLED);
            SelectedFnd = null;
            ShowFndCaptionFor(null);
        }

        // Pass null to clear. Only the standalone LED family is touched here - the probes INSIDE an FND char are
        // driven by that char's own selection (FND.SetCaption), never individually.
        private void ShowLedCaptionFor(SingleLED selected)
        {
            if (LED == null) return;
            foreach (var group in LED)
            {
                if (group?.LEDs == null) continue;
                foreach (var led in group.LEDs)
                {
                    if (led != null) led.SetCaption(ReferenceEquals(led, selected));
                }
            }
        }

        // Clicking empty canvas deselects everything, so no caption is left on screen (VisionBuilder calls this
        // from the canvas MouseDown - an ROI marks its own click Handled, so only bare-canvas clicks get here).
        public void ClearSelection()
        {
            SelectedFnd = null;
            ShowFndCaptionFor(null);
            ShowLedCaptionFor(null);
        }

        // Subscribe to every standalone probe's click. MUST be re-run whenever the model is (re)attached:
        // loading a model REPLACES the LEDs collections, so any earlier subscription is left on discarded
        // objects. Unsubscribe-then-subscribe keeps it idempotent, so calling it repeatedly is harmless.
        public void WireLedSelection()
        {
            if (LED == null) return;
            foreach (var group in LED)
            {
                if (group?.LEDs == null) continue;
                foreach (var led in group.LEDs)
                {
                    if (led == null) continue;
                    led.Sellected -= SingleLed_Selected;
                    led.Sellected += SingleLed_Selected;
                }
            }
        }

        // Name the FND char that was just clicked on the canvas ("FND1".."FND7") and clear every other one, so
        // exactly one caption is on screen. Coordinated here because a char cannot see its siblings; this class
        // already owns the list and is already the single subscriber of every ROI's Selected event.
        // Pass null to clear them all. Caption placement matches LCD's detected string - see FND.DetectCaption.
        private void ShowFndCaptionFor(FND selected)
        {
            if (FNDs == null) return;
            for (int c = 0; c < FNDs.Count; c++)
            {
                var col = FNDs[c];
                if (col == null) continue;
                for (int b = 0; b < col.Count; b++)
                {
                    var fnd = col[b];
                    // All-caps "FND1", matching the model's own Name (FND.cs sets Name = "FND" + index).
                    if (fnd != null) fnd.SetCaption("FND" + (c + 1), ReferenceEquals(fnd, selected));
                }
            }
        }

        public void UpdateLayout(int PCB_Count)
        {
            for (int i = 0; i < 4; i++)
            {
                foreach (var fnds_char in  FNDs)
                {
                    fnds_char[i].Visibility = PCB_Count > i ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                }
          

                LCDs[i].Visibility = PCB_Count > i ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                GLED[i].Visibility = PCB_Count > i ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                LED[i].Visibility = PCB_Count > i ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
        }

        public void GetFNDSampleImage(Mat mat)
        {

            foreach (var fnds_char in FNDs)
            {
                foreach (var item in fnds_char)
                {
                    if (item.Use)
                    {
                        item.TestImage(mat);
                    }
                    else
                    {
                        item.DetectedString = string.Empty;
                    }
                }
            }
        }


        public void GetLCDSampleImage(Mat mat, OCR ocr)
        {
            LCDs[0].TestImage(ocr, mat);
        }

        // Wipe every detected string so a test step can only ever match a value produced during THIS run.
        // Nothing else clears LCD.DetectedString - it holds the last OCR result indefinitely. That was harmless
        // while OCR ran continuously (the value tracked whatever the camera saw), but now that vision only runs
        // during a test, the previous board's reading survives into the next run: a step that samples it before
        // the first fresh OCR result lands would pass on the PREVIOUS board's display. Call this when vision
        // starts for a run.
        public void ClearDetected()
        {
            if (LCDs != null)
            {
                foreach (var lcd in LCDs)
                {
                    if (lcd != null) lcd.DetectedString = string.Empty;
                }
            }
            if (FNDs != null)
            {
                foreach (var fnds_char in FNDs)
                {
                    if (fnds_char == null) continue;
                    foreach (var fnd in fnds_char)
                    {
                        if (fnd != null) fnd.DetectedString = string.Empty;
                    }
                }
            }
        }


        public void GetGLEDSampleImage(Mat mat)
        {
            foreach (var item in GLED)
            {
                item.GetValue(mat);
            }
        }
        public void GetLEDSampleImage(Mat mat)
        {
            foreach (var item in LED)
            {
                item.GetValue(mat);
            }
        }

        /// <summary>
        /// Thread-safe Mat to BitmapSource conversion. Unlike ToBitmapSource() which
        /// requires the UI thread (uses WriteableBitmap), this uses BitmapSource.Create
        /// which works from any thread. Always returns a Frozen BitmapSource.
        /// </summary>
        public static BitmapSource MatToBitmapSource(Mat mat)
        {
            if (mat == null || mat.Empty()) return null;

            Mat continuous = mat.IsContinuous() ? mat : mat.Clone();
            try
            {
                PixelFormat pixelFormat;
                if (continuous.Channels() == 1)
                    pixelFormat = PixelFormats.Gray8;
                else if (continuous.Channels() == 4)
                    pixelFormat = PixelFormats.Bgra32;
                else
                    pixelFormat = PixelFormats.Bgr24;

                int stride = continuous.Cols * continuous.ElemSize();
                int bufferSize = stride * continuous.Rows;
                byte[] buffer = new byte[bufferSize];
                Marshal.Copy(continuous.Data, buffer, 0, bufferSize);

                var bmp = BitmapSource.Create(
                    continuous.Cols, continuous.Rows, 96, 96,
                    pixelFormat, null, buffer, stride);
                bmp.Freeze();
                return bmp;
            }
            finally
            {
                if (!mat.IsContinuous()) continuous.Dispose();
            }
        }
    }
}
