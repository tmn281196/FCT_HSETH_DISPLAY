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
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Timer = System.Timers.Timer;


namespace VTMMain
{
    /// <summary>
    /// Interaction logic for AutoPage.xaml
    /// </summary>
    public partial class AutoPage : Page
    {
        public CancellationTokenSource _shutDown = new CancellationTokenSource();
        private int _fndProcessing = 0;
        private int _lcdProcessing = 0;

        private string appDir = AppDomain.CurrentDomain.BaseDirectory + "\\" + "qty.txt";

        //Variable
        private Model testModel = new Model();

        public Model TestModel
        {
            get { return testModel; }
            set
            {
                if (value != testModel)
                {
                    testModel = value;
                    if (dgModelSteps != null) dgModelSteps.ItemsSource = TestModel.Steps;
                    if (lbModelName != null) lbModelName.Text = TestModel.Name;

                    // Drive the SHARED Program.VisionTester (no local clone) so the ROIs the operator sees are
                    // the ones the state machine measures and colours green/red - same as Manual.
                    if (Program != null && Program.VisionTester != null)
                    {
                        Program.VisionTester.Models = TestModel.VisionModels;
                        Program.VisionTester.Models.UpdateLayout(TestModel.Layout.PCB_Count);
                        Program.VisionTester.FuntionsUpdate();
                        AttachVisionTester();
                    }

                    dtttSite1.Visibility = TestModel.Layout.PCB_Count >= 1 ? Visibility.Visible : Visibility.Collapsed;
                    dtttSite2.Visibility = TestModel.Layout.PCB_Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
                    dtttSite3.Visibility = TestModel.Layout.PCB_Count >= 3 ? Visibility.Visible : Visibility.Collapsed;
                    dtttSite4.Visibility = TestModel.Layout.PCB_Count >= 4 ? Visibility.Visible : Visibility.Collapsed;

                    Program.ResultPanel.PBA = TestModel.Layout;
                }
            }
        }

        private Program program = new Program();

        public Program Program
        {
            get { return program; }
            set
            {
                program = value;
                program.StepTestChange += Program_StepTestChange;
                program.StateChange += Program_StateChange;
                program.TestRunFinish += Program_TestRunFinish;
                program.EscapTimeChange += Program_EscapTimeChange;
                program.TesttingStateChange += Program_TesttingStateChange;
                //VisionTesterHolder.Child = Program.VisionTester;
                dgrBarcode.ItemsSource = program.Boards;
                dgrVersion.ItemsSource = program.Boards;
                ResultPannelHolder.Child = program.ResultPanel;
            }
        }

        private void Program_TesttingStateChange(object sender, EventArgs e)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                btAutoRun.Content = Program.IsTestting ? "STOP" : "RUN";
            }
            ));
        }

        private void Program_EscapTimeChange(object sender, EventArgs e)
        {
            this.Dispatcher.Invoke(new Action(() =>
            lbEscapTime.Content = Program.EscapTime.ToString("0.00") + "s"
            ));
        }

        private Timer GetFNDImageSampleTimer = new Timer
        {
            Interval = 100,
        };

        private Timer GetLCDSampleTimer = new Timer
        {
            Interval = 100,
        };

        private OCR ocr;
        public AutoPage()
        {
            ocr = new OCR();    // OCR engine is [ThreadStatic] (see OCR.cs): this page's GetLCDSampleTimer thread runs OCR
                                // directly + owns its own predictor - built + run on one thread, no queue => no ONEDNN/NCHW.



            

            InitializeComponent();
            Utility.Debug.dispatcher = this.Dispatcher;
            Utility.Debug.LogBox = rtbProgramLog;
            btAutoRun.IsEnabled = false;

            dtttSite1.Visibility = TestModel.Layout.PCB_Count >= 1 ? Visibility.Visible : Visibility.Collapsed;
            dtttSite2.Visibility = TestModel.Layout.PCB_Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
            dtttSite3.Visibility = TestModel.Layout.PCB_Count >= 3 ? Visibility.Visible : Visibility.Collapsed;
            dtttSite4.Visibility = TestModel.Layout.PCB_Count >= 4 ? Visibility.Visible : Visibility.Collapsed;

            GetFNDImageSampleTimer.Elapsed += GetImageSampleTimer_Elapsed;
            GetLCDSampleTimer.Elapsed += GetLCDSampleTimer_Elapsed;

            try
            {
                string[] data = File.ReadAllText(appDir).Split(',');
                graphResult.Pass = double.Parse(data[0]);
                graphResult.Fail = double.Parse(data[1]);
            }
            catch (Exception)
            {
            }
        }

        private void ClearProgramLog_Click(object sender, RoutedEventArgs e)
        {
            Utility.Debug.ClearLog();
        }

        // Stand-in for the scanner: same barcode path, same logs, no hardware. Needs a model, since the fake
        // barcode is built from that model's own BarcodeOption rules.
        private void FakeScan_Click(object sender, RoutedEventArgs e)
        {
            if (!Program.IsloadModel)
            {
                Utility.Debug.Write("SCANNER:FAKE SCAN needs a model loaded", Utility.Debug.ContentType.Warning);
                return;
            }
            // fake:true rides with the barcode so the run it starts skips the .lgd export.
            Program.AcceptBarcode(Program.MakeFakeBarcode(), true);
        }

        public string LoadModel()
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
                Utility.Debug.Write("MODEL: Load " + System.IO.Path.GetFileName(openFile.FileName), Utility.Debug.ContentType.Notify);
                //var fileInfor = new FileInfo(openFile.FileName);
                string modelStr = System.IO.File.ReadAllText(openFile.FileName);
                try
                {
                    string modelString = Utility.Extensions.Decoder(modelStr, System.Text.Encoding.UTF7);
                    //Console.WriteLine(modelString);
                    //TestModel = Utility.Extensions.ConvertFromJson<Model>(modelString);
                    TestModel = Utility.Extensions.ConvertFromJson<Model>(modelString);
                    TestModel.Path = openFile.FileName;
                    foreach (var item in TestModel.Steps)
                    {
                        item.ValueGet1 = "";
                        item.ValueGet2 = "";
                        item.ValueGet3 = "";
                        item.ValueGet4 = "";
                    }
                    Program.TestModel = TestModel;
                    btAutoRun.IsEnabled = true;
                    // Nho path cho auto-load lan sau
                    Program.appSetting.LastModelPath = openFile.FileName;
                    Utility.Extensions.SaveToFile(Program.appSetting, "Config.cfg");
                    return modelString;
                }
                catch (Exception)
                {
                    Utility.Debug.Write("Load model fail, file not correct format. \n" +
                        "Model folder: " + openFile.FileName, Utility.Debug.ContentType.Error);
                }
            }

            return null;
        }

        public string LoadModel(string path)
        {
            Utility.Debug.Write("MODEL: Load " + System.IO.Path.GetFileName(path), Utility.Debug.ContentType.Notify);
            if (path == null) return null;

            //var fileInfor = new FileInfo(openFile.FileName);
            string modelStr = System.IO.File.ReadAllText(path);
            if (modelStr == null)
            {
                return null;
            }
            try
            {
                string modelString = Utility.Extensions.Decoder(modelStr, System.Text.Encoding.UTF7);
                //Console.WriteLine(modelString);
                //TestModel = Utility.Extensions.ConvertFromJson<Model>(modelString);
                TestModel = Utility.Extensions.ConvertFromJson<Model>(modelString);
                TestModel.Path = path;   // Name derives from Path - use the ACTUAL opened file, not the serialized one
                foreach (var item in TestModel.Steps)
                {
                    item.ValueGet1 = "";
                    item.ValueGet2 = "";
                    item.ValueGet3 = "";
                    item.ValueGet4 = "";
                }
                Program.TestModel = TestModel;
                btAutoRun.IsEnabled = true;
                // Nho path cho auto-load lan sau
                Program.appSetting.LastModelPath = path;
                Utility.Extensions.SaveToFile(Program.appSetting, "Config.cfg");
                return modelString;
            }
            catch (Exception)
            {
            }

            return null;
        }

        private void Program_StepTestChange(object sender, EventArgs e)
        {
            if (!this._shutDown.IsCancellationRequested)
            {
                pgbTestProgress.Dispatcher.Invoke(new Action(() =>
                {
                    var progress = Math.Round((((int)sender + 1) / (double)TestModel.Steps.Count) * 100.0, 2);
                    pgbTestProgress.Value = (int)sender > 0 ? pgbTestProgress.Value < progress ? progress : pgbTestProgress.Value : progress;
                    dgModelSteps.SelectedIndex = (int)sender;
                    dgModelSteps.ScrollIntoView(dgModelSteps.SelectedItem);
                }
                ));

                Program.VisionTester.Dispatcher.Invoke(new Action(() =>
                {
                    var currentStep = (dgModelSteps.SelectedItem as Step);
                    UpdateLcdRoi(currentStep);
                    UpdateFndRoi(currentStep);
                    UpdateLedRoi(currentStep);
                }));
            }
        }



        private void UpdateLedRoi(Step currentStep)
        {
            if (currentStep.CMD == CMDs.LED.ToString())
            {
                // One shared VisionTester now (Auto displays it too).
                var ledList = Program.VisionTester.Models.LED;
                ledList[0].LEDs = currentStep.LedList;

                foreach (var led in ledList[0].LEDs)
                {
                    led.SetPosition();
                }
                Program.VisionTester.LedFunctionUpdate();

                // Immediate detection (like ManualPage)
                var lastFrameToTest = Program.Capture?.LastMatFrame;
                if (lastFrameToTest != null)
                {
                    Program.VisionTester.Models.GetLEDSampleImage(lastFrameToTest);
                }
            }
        }

        // Move the shared Program.VisionTester into this page's holder. A WPF element has one parent, so it has
        // to be detached from wherever it currently lives (Auto's or Manual's holder) first. Called on page-show
        // and on model load, so whichever of Auto/Manual is visible owns the tester.
        public void AttachVisionTester()
        {
            if (Program == null || Program.VisionTester == null || VisionTesterHolder == null) return;
            var vt = Program.VisionTester;
            var old = vt.Parent as System.Windows.Controls.Border;
            if (old != null && old != VisionTesterHolder) old.Child = null;
            if (VisionTesterHolder.Child != vt) VisionTesterHolder.Child = vt;
        }

        private void UpdateLcdRoi(Step currentStep)
        {
            if (currentStep.CMD == CMDs.LCD.ToString())
            {
                // LCDRoiValue0..3 are Rect? and are null on a step that has no LCD ROI. A null one is simply not
                // applied - the ROI keeps whatever it had - rather than being read through .Value, which would
                // throw InvalidOperationException here, mid-test.
                // One shared VisionTester now (it is what Auto displays too), so a single update.
                ApplyLcdRoi(Program.VisionTester.Models.LCDs, currentStep);
                Program.VisionTester.LcdFunctionUpdate();
            }
        }

        private static void ApplyLcdRoi(System.Collections.Generic.List<Camera.LCD> lcds, Step step)
        {
            // Show only the LCDs this step uses; FuntionsUpdate collapsed all of them (no Use flag on LCD).
            lcds[0].Visibility = step.LCDRoiValue0.HasValue ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            lcds[1].Visibility = step.LCDRoiValue1.HasValue ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            lcds[2].Visibility = step.LCDRoiValue2.HasValue ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            lcds[3].Visibility = step.LCDRoiValue3.HasValue ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (step.LCDRoiValue0.HasValue) lcds[0].Rect = step.LCDRoiValue0.Value;
            if (step.LCDRoiValue1.HasValue) lcds[1].Rect = step.LCDRoiValue1.Value;
            if (step.LCDRoiValue2.HasValue) lcds[2].Rect = step.LCDRoiValue2.Value;
            if (step.LCDRoiValue3.HasValue) lcds[3].Rect = step.LCDRoiValue3.Value;
        }

        private void UpdateFndRoi(Step currentStep)
        {
            // HasFndStore, not just the cmd check: the loops below index FNDsBoard0/RectFNDsBoard0/
            // UseFNDsBoard0 a hardcoded 7 times, bounded by the LIVE model rather than the step's own Count.
            // This runs mid-test, so a short store here takes the machine down between boards, not at edit time.
            if (currentStep.CMD == CMDs.FND.ToString() && currentStep.HasFndStore)
            {
                // Update Program's VisionTester (used by test state machine)
                int index_char_prog = 0;
                foreach (var FNDchar in Program.VisionTester.Models.FNDs)
                {
                    if (TestModel.Layout.PCB_Count >= 1)
                    {
                        for (int index_led = 0; index_led < 7; index_led++)
                        {
                            FNDchar[0].PointSegments.LEDs[index_led].X = currentStep.FNDsBoard0[index_char_prog].PointSegments.LEDs[index_led].X;
                            FNDchar[0].PointSegments.LEDs[index_led].Y = currentStep.FNDsBoard0[index_char_prog].PointSegments.LEDs[index_led].Y;
                            FNDchar[0].PointSegments.LEDs[index_led].Dir = currentStep.FNDsBoard0[index_char_prog].PointSegments.LEDs[index_led].Dir;
                            FNDchar[0].PointSegments.LEDs[index_led].ON = currentStep.FNDsBoard0[index_char_prog].PointSegments.LEDs[index_led].ON;
                            FNDchar[0].PointSegments.LEDs[index_led].OFF = currentStep.FNDsBoard0[index_char_prog].PointSegments.LEDs[index_led].OFF;
                            FNDchar[0].PointSegments.LEDs[index_led].Thresh = currentStep.FNDsBoard0[index_char_prog].PointSegments.LEDs[index_led].Thresh;
                            FNDchar[0].PointSegments.LEDs[index_led].Use = currentStep.FNDsBoard0[index_char_prog].PointSegments.LEDs[index_led].Use;
                        }

                        FNDchar[0].Rect = currentStep.RectFNDsBoard0[index_char_prog];
                        FNDchar[0].Use = currentStep.UseFNDsBoard0[index_char_prog];
                    }
                    index_char_prog++;
                }
                Program.VisionTester.FndFunctionUpdate();
            }
        }

        private void Program_StateChange(object sender, EventArgs e)
        {
            Dispatcher.Invoke(new Action((
                delegate
                {
                    lbTestResultTesting.Visibility = Visibility.Hidden;
                    lbTestResultStop.Visibility = Visibility.Hidden;
                    lbTestResultPause.Visibility = Visibility.Hidden;
                    lbTestResultGood.Visibility = Visibility.Hidden;
                    lbTestResultFail.Visibility = Visibility.Hidden;
                    lbTestBusy.Visibility = Visibility.Hidden;
                    lbTestResultWait.Visibility = Visibility.Hidden;
                    lbTestReady.Visibility = Visibility.Hidden;
                }

                )));
            switch (Program.TestState)
            {
                case Program.RunTestState.WAIT:
                    Dispatcher.Invoke(new Action(delegate
                    {
                        lbTestResultWait.Visibility = Visibility.Visible;
                        Storyboard sb = (Storyboard)TryFindResource("LabelSlide");
                        if (sb != null) sb.Begin(lbTestResultWait);
                    }), DispatcherPriority.Send);
                    break;

                case Program.RunTestState.TESTTING:
                    Dispatcher.Invoke(new Action(delegate
                    {
                        lbTestResultTesting.Visibility = Visibility.Visible;
                    }), DispatcherPriority.Send);
                    break;

                case Program.RunTestState.PAUSE:
                    Dispatcher.Invoke(new Action(delegate
                    {
                        lbTestResultPause.Visibility = Visibility.Visible;
                    }), DispatcherPriority.Send);
                    break;

                case Program.RunTestState.STOP:
                    Dispatcher.Invoke(new Action(delegate
                    {
                        lbTestResultStop.Visibility = Visibility.Visible;
                    }), DispatcherPriority.Send);
                    break;

                case Program.RunTestState.GOOD:
                    Dispatcher.Invoke(new Action(delegate
                    {
                        lbTestResultGood.Visibility = Visibility.Visible;
                        Storyboard sb = (Storyboard)TryFindResource("LabelSlide");
                        if (sb != null) sb.Begin(lbTestResultGood);
                    }), DispatcherPriority.Send);
                    break;

                case Program.RunTestState.FAIL:
                    Dispatcher.Invoke(new Action(delegate
                    {
                        lbTestResultFail.Visibility = Visibility.Visible;
                        Storyboard sb = (Storyboard)TryFindResource("LabelSlide");
                        if (sb != null) sb.Begin(lbTestResultFail);
                    }), DispatcherPriority.Send);
                    break;

                case Program.RunTestState.BUSY:
                    Dispatcher.Invoke(new Action(delegate
                    {
                        lbTestBusy.Visibility = Visibility.Visible;
                    }), DispatcherPriority.Send);
                    break;

                case Program.RunTestState.READY:
                    Dispatcher.Invoke(new Action(delegate
                    {
                        lbTestReady.Visibility = Visibility.Visible;
                        Storyboard sb = (Storyboard)TryFindResource("LabelSlide");
                        if (sb != null) sb.Begin(lbTestReady);
                    }), DispatcherPriority.Send);
                    break;

                case Program.RunTestState.DONE:

                    //EscapTimer.Stop();
                    break;

                default:
                    break;
            }
        }

        private void Program_TestRunFinish(object sender, EventArgs e)
        {
            graphResult.Dispatcher.Invoke(
                new Action(() =>
                {
                    int pass = 0;
                    int fail = 0;
                    foreach (var item in Program.Boards)
                    {
                        if (!item.UserSkip && item.Result == "OK")
                        {
                            pass++;
                        }

                        if (!item.UserSkip && item.Result == "FAIL")
                        {
                            fail++;
                        }
                    }
                    graphResult.Pass += pass;
                    graphResult.Fail += fail;
                    graphResult.Draw();

                    string saveData = $"{graphResult.Pass.ToString()},{graphResult.Fail.ToString()}";

                    File.WriteAllText(appDir, saveData);

                    // Stop vision timers when test finishes
                    DisableLive();

                    //// Stress test: auto-restart
                    //if (chkStressTest.IsChecked == true)
                    //{
                    //    Task.Run(async () =>
                    //    {
                    //        // Wait until state machine returns to READY
                    //        while (Program.TestState != Program.RunTestState.READY)
                    //            await Task.Delay(100);

                    //        Dispatcher.BeginInvoke(new Action(() =>
                    //        {
                    //            if (chkStressTest.IsChecked == true)
                    //            {
                    //                Program.IsTestting = true;
                    //                btAutoRun.Content = "STOP";
                    //            }
                    //        }));
                    //    });
                    //}
                }));
        }

        private void ResetQtyBtn_Click(object sender, RoutedEventArgs e)
        {
            ResetQty();
        }

        private void ResetQty()
        {
            graphResult.Pass = 0;
            graphResult.Fail = 0;
            File.WriteAllText(appDir, "0,0");
        }

        // Correct a miscounted FAIL: take one off the counter. Clamped at 0 and persisted the same way the
        // after-test update does, so a restart reads back what is on screen.
        private void MinusFailQtyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (graphResult.Fail <= 0) return;
            graphResult.Fail -= 1;
            graphResult.Draw();
            File.WriteAllText(appDir, $"{graphResult.Pass.ToString()},{graphResult.Fail.ToString()}");
        }

        private void btAutoRun_Click(object sender, RoutedEventArgs e)
        {
            if (Program.IsTestting)
            {
                Program.TestState = Program.RunTestState.STOP;
                Program.IsTestting = false;
                btAutoRun.Content = "RUN";
            }
            else if (Program.TestState == Program.RunTestState.READY)
            {
                Program.IsTestting = true;
                btAutoRun.Content = "STOP";
            }
        }

        private void waitCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            //if (Program.IsTestting) return;
            //DependencyObject dep = (DependencyObject)e.OriginalSource;
            //while ((dep != null) && !(dep is DataGridColumnHeader))
            //{
            //    dep = VisualTreeHelper.GetParent(dep);
            //}

            //if (dep == null)
            //    return;
            //if (dep is DataGridColumnHeader)
            //{
            //    DataGridColumnHeader columnHeader = dep as DataGridColumnHeader;
            //    columnHeader.Background = new SolidColorBrush(Color.FromRgb(21, 21, 21));
            //    Console.WriteLine(columnHeader.Content);
            //    string columnEnable = columnHeader.Content.ToString();
            //    switch (columnEnable)
            //    {
            //        case "A":
            //            if (Program.Boards.Count >= 1) Program.Boards[0].UserSkip = false;
            //            break;
            //        case "B":
            //            if (Program.Boards.Count >= 2) Program.Boards[1].UserSkip = false;
            //            break;
            //        case "C":
            //            if (Program.Boards.Count >= 3) Program.Boards[2].UserSkip = false;
            //            break;
            //        case "D":
            //            if (Program.Boards.Count >= 4) Program.Boards[3].UserSkip = false;
            //            break;
            //        default:
            //            break;
            //    }
            //}
        }

        private void waitCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            //if (Program.IsTestting) return;
            //DependencyObject dep = (DependencyObject)e.OriginalSource;
            //while ((dep != null) && !(dep is DataGridColumnHeader))
            //{
            //    dep = VisualTreeHelper.GetParent(dep);
            //}

            //if (dep == null)
            //    return;
            //if (dep is DataGridColumnHeader)
            //{
            //    DataGridColumnHeader columnHeader = dep as DataGridColumnHeader;
            //    columnHeader.Background = new SolidColorBrush(Colors.Gray);
            //    Console.WriteLine(columnHeader.Content);
            //    string columnEnable = columnHeader.Content.ToString();
            //    switch (columnEnable)
            //    {
            //        case "A":
            //            if (Program.Boards.Count >= 1) Program.Boards[0].UserSkip = true;
            //            break;
            //        case "B":
            //            if (Program.Boards.Count >= 2) Program.Boards[1].UserSkip = true;
            //            break;
            //        case "C":
            //            if (Program.Boards.Count >= 3) Program.Boards[2].UserSkip = true;
            //            break;
            //        case "D":
            //            if (Program.Boards.Count >= 4) Program.Boards[3].UserSkip = true;
            //            break;
            //        default:
            //            break;
            //    }
            //}
        }

        private void GetImageSampleTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (Interlocked.CompareExchange(ref _fndProcessing, 1, 0) != 0) return;
            try
            {
                var lastFrameToTest = Program.Capture?.LastMatFrame;
                if (lastFrameToTest == null) return;
                Program.VisionTester.Models.GetFNDSampleImage(lastFrameToTest);
                Program.VisionTester.Models.GetGLEDSampleImage(lastFrameToTest);
                Program.VisionTester.Models.GetLEDSampleImage(lastFrameToTest);
            }
            finally { Interlocked.Exchange(ref _fndProcessing, 0); }
        }

        private void GetLCDSampleTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (Interlocked.CompareExchange(ref _lcdProcessing, 1, 0) != 0) return;
            try
            {
                var lastFrameToTest = Program.Capture?.LastMatFrame;
                if (lastFrameToTest == null) return;
                Program.VisionTester.Models.GetLCDSampleImage(lastFrameToTest, ocr);
            }
            finally { Interlocked.Exchange(ref _lcdProcessing, 0); }
        }

        public void EnableLive()
        {
            GetFNDImageSampleTimer.Start();
            GetLCDSampleTimer.Start();
        }

        public void DisableLive()
        {
            GetFNDImageSampleTimer.Stop();
            GetLCDSampleTimer.Stop();

        }

        private void ResultPannelHolder_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            Program.ResultPanel.Visibility = Visibility.Hidden;
        }

        private void waitCheckbox_Click(object sender, RoutedEventArgs e)
        {
            if (Program.IsTestting) return;
            DependencyObject dep = (DependencyObject)e.OriginalSource;
            while ((dep != null) && !(dep is DataGridColumnHeader))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep == null)
                return;
            if (dep is DataGridColumnHeader && (sender as CheckBox).IsChecked == true)
            {
                DataGridColumnHeader columnHeader = dep as DataGridColumnHeader;
                columnHeader.Background = new SolidColorBrush(Color.FromRgb(198, 198, 198));
                Console.WriteLine(columnHeader.Content);
                string columnEnable = columnHeader.Content.ToString();
                switch (columnEnable)
                {
                    case "A":
                        if (Program.Boards.Count >= 1) Program.Boards[0].UserSkip = false;
                        break;

                    case "B":
                        if (Program.Boards.Count >= 2) Program.Boards[1].UserSkip = false;
                        break;

                    case "C":
                        if (Program.Boards.Count >= 3) Program.Boards[2].UserSkip = false;
                        break;

                    case "D":
                        if (Program.Boards.Count >= 4) Program.Boards[3].UserSkip = false;
                        break;

                    default:
                        break;
                }
            }

            if (dep is DataGridColumnHeader && (sender as CheckBox).IsChecked == false)
            {
                DataGridColumnHeader columnHeader = dep as DataGridColumnHeader;
                columnHeader.Background = new SolidColorBrush(Color.FromRgb(182, 182, 182));
                Console.WriteLine(columnHeader.Content);
                string columnEnable = columnHeader.Content.ToString();
                switch (columnEnable)
                {
                    case "A":
                        if (Program.Boards.Count >= 1) Program.Boards[0].UserSkip = true;
                        break;

                    case "B":
                        if (Program.Boards.Count >= 2) Program.Boards[1].UserSkip = true;
                        break;

                    case "C":
                        if (Program.Boards.Count >= 3) Program.Boards[2].UserSkip = true;
                        break;

                    case "D":
                        if (Program.Boards.Count >= 4) Program.Boards[3].UserSkip = true;
                        break;

                    default:
                        break;
                }
            }
        }
    }
}