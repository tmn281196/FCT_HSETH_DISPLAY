using Camera;
using Camera.VisionTest;
using VTMBase;
using VTMProgram;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
using System.Windows.Navigation;
using System.Windows.Shapes;
using Timer = System.Timers.Timer;

namespace VTMTester
{
    /// <summary>
    /// Interaction logic for ManualPage.xaml
    /// </summary>
    public partial class ManualPage : Page
    {
        private bool manualTestOn;
        private int _fndProcessing = 0;
        private int _lcdProcessing = 0;

        public CancellationTokenSource _shutDown = new CancellationTokenSource();
        private Program _Program;

        public Program Program
        {
            get { return _Program; }
            set
            {
                if (value != null || value != _Program)
                {
                    _Program = value;
                    TestModel = Program.TestModel;
                    DMM_Holder.Child = Program._DMM;
                    MuxControl_Holder.Child = Program.MuxCard;
                    RelayControl_Holder.Child = Program.Relay;
                    SolenoidControl_Holder.Child = Program.Solenoid;
                    LevelViewer_Holder.Child = Program.Level;
                    SYSTEM_Holder.Child = Program.System;
                    StepDetailHolder.Child = Program.StepViewer;
                    AttachVisionTester();
                    Program.StepTestChange += Program_StepTestChange;
                    Program.TestRunFinish += Program_TestRunFinish;

                    UUT1Com.Content = Program.UUTs[0].LogBox;
                    UUT2Com.Content = Program.UUTs[1].LogBox;
                    UUT3Com.Content = Program.UUTs[2].LogBox;
                    UUT4Com.Content = Program.UUTs[3].LogBox;
                }
            }
        }

        private void Program_TestRunFinish(object sender, EventArgs e)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                btTest.IsChecked = false;
                btStop.IsChecked = true;
                btLoop.IsChecked = false;
                //DisableLive();
            }));
        }

        private Model _TestModel;

        public Model TestModel
        {
            get { return _TestModel; }
            set
            {
                if (value != null || value != _TestModel)
                {
                    _TestModel = value;
                    Program.MuxCard.Card = _TestModel.MuxCard;
                    Program.Level.Card = _TestModel.LevelCard;
                    dgModelSteps.ItemsSource = _TestModel.Steps;

                    dtttSite1.Visibility = TestModel.Layout.PCB_Count >= 1 ? Visibility.Visible : Visibility.Collapsed;
                    dtttSite2.Visibility = TestModel.Layout.PCB_Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
                    dtttSite3.Visibility = TestModel.Layout.PCB_Count >= 3 ? Visibility.Visible : Visibility.Collapsed;
                    dtttSite4.Visibility = TestModel.Layout.PCB_Count >= 4 ? Visibility.Visible : Visibility.Collapsed;

                    Program.VisionTester.Models = TestModel.VisionModels;
                    AttachVisionTester();

                    // Camera is NOT touched on model load - the sliders are filled from the model by ApplyModelSettings
                    // (called from OpenModel / auto-load / post-save), and the camera is written only on manual/test start.
                    cbbTxnaming_Manual.ItemsSource = _TestModel.Naming.TxDatas.Select(x => x.Name).ToList();
                }
            }
        }

        private Timer GetFNDImageSampleTimer = new Timer
        {
            Interval = 100,
        };

        private Timer GetLCDSampleTimer = new Timer
        {
            Interval = 100,
        };

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
        private OCR ocr;
     
       
        public ManualPage()
        {
            ocr = new OCR();    // OCR engine is [ThreadStatic] (see OCR.cs): this page's GetLCDSampleTimer thread runs OCR
                                // directly + owns its own predictor - built + run on one thread, no queue => no ONEDNN/NCHW.

            InitializeComponent();
            GetFNDImageSampleTimer.Elapsed += GetImageSampleTimer_Elapsed;
            GetLCDSampleTimer.Elapsed += GetLCDSampleTimer_Elapsed;
        }

        // Move the shared Program.VisionTester into Manual's holder, detaching it from Auto's holder first (a WPF
        // element has one parent). Called on model load and on page-show, so the visible page owns the tester.
        public void AttachVisionTester()
        {
            if (Program == null || Program.VisionTester == null || VisionTest_Holder == null) return;
            var vt = Program.VisionTester;
            var old = vt.Parent as System.Windows.Controls.Border;
            if (old != null && old != VisionTest_Holder) old.Child = null;
            if (VisionTest_Holder.Child != vt) VisionTest_Holder.Child = vt;
        }

        private void dgModelSteps_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (dgModelSteps.SelectedItem != null)
            {
                Program.StepViewer.StepToGet = (Step)dgModelSteps.SelectedItem;
            }
        }

        private void Program_StepTestChange(object sender, EventArgs e)
        {
            //var s =
            if (!this._shutDown.IsCancellationRequested)
            {
                pgbTestProgress.Dispatcher.Invoke(new Action(() =>
                {
                    var progressPercent = Math.Round((((int)sender + 1) / (double)TestModel.Steps.Count) * 100.0, 2);
                    pgbTestProgress.Value = (int)sender > 0 ? pgbTestProgress.Value < progressPercent ? progressPercent : pgbTestProgress.Value : progressPercent;
                    dgModelSteps.SelectedIndex = (int)sender;
                    dgModelSteps.ScrollIntoView(dgModelSteps.SelectedItem);
                }));

                Program.VisionTester.Dispatcher.Invoke(new Action(() =>
                {
                    var currentStep = (dgModelSteps.SelectedItem as Step);
                    UpdateLcdRoi(currentStep);
                    UpdateFndRoi(currentStep);
                    UpdateLedRoi(currentStep);
                }));
            }
        }


        private void dgModelSteps_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dgModelSteps.SelectedItem != null)
            {
                manualTestOn = true;
                var step = (Step)dgModelSteps.SelectedItem;
                Program.currentStep = step;
                UpdateLcdRoi(step);
                UpdateFndRoi(step);
                UpdateLedRoi(step);
                Program.RunStep();
            }
        }

        private void btTest_Checked(object sender, RoutedEventArgs e)
        {
            btStop.IsChecked = false;
            if (!Program.IsTestting)
            {
                Program.RUN_MANUAL_TEST();
            }
            else
            {
                Program.TestState = Program.RunTestState.MANUALTEST;
            }
        }

        private void btTest_Unchecked(object sender, RoutedEventArgs e)
        {
            if (Program.IsTestting)
            {
                Program.TestState = Program.RunTestState.PAUSE;
            }
        }

        private void btStop_Checked(object sender, RoutedEventArgs e)
        {
            btTest.IsChecked = false;
            btLoop.IsChecked = false;
            Program.ManualLoop = false;
            Program.IsTestting = false;
            if (Program.IsTestting)
            {
                Program.TestState = Program.RunTestState.STOP;
            }
        }

        private void btLoop_Checked(object sender, RoutedEventArgs e)
        {
            btStop.IsChecked = false;
            Program.ManualLoop = true;
            if (!Program.IsTestting)
            {
                Program.RUN_MANUAL_TEST();
            }
            else
            {
                Program.TestState = Program.RunTestState.MANUALTEST;
            }
        }

        private void btLoop_Unchecked(object sender, RoutedEventArgs e)
        {
            // Stop looping; current pass will finish then stop normally.
            Program.ManualLoop = false;
        }

        private void UpdateLedRoi(Step currentStep)
        {
            //this updates the actual ROI logic

            if (currentStep.CMD == CMDs.LED.ToString())
            {
                var testState = Program.TestState;

                var ledList = Program.VisionTester.Models.LED;
                ledList[0].LEDs = currentStep.LedList;

                foreach (var led in ledList[0].LEDs)
                {
                    led.SetPosition();
                }

                if (testState == Program.RunTestState.MANUALTEST || manualTestOn)
                {
                    Program.VisionTester.LedFunctionUpdate();
                }

                var lastFrameToTest = camera.LastMatFrame;
                if (lastFrameToTest == null)
                {
                    return;
                }
                Program.VisionTester.Models.GetLEDSampleImage(lastFrameToTest);
                manualTestOn = false;
            }
        }

        private void UpdateLcdRoi(Step currentStep)
        {
            if (currentStep.CMD == CMDs.LCD.ToString())
            {
                // A null LCDRoiValue means "this step has no LCD ROI" - skip it rather than reading .Value,
                // which would throw mid-test. See AutoPage.ApplyLcdRoi.
                var lcdList = Program.VisionTester.Models.LCDs;
                // Show a board's LCD only if that board exists (PCB_Count) AND this step uses it - so the 3
                // non-existent-board LCDs stay hidden on the 1-board machine even if the step carries LCDRoiValue1..3.
                int boardCount = TestModel.Layout.PCB_Count;
                lcdList[0].Visibility = (boardCount > 0 && currentStep.LCDRoiValue0.HasValue) ? Visibility.Visible : Visibility.Collapsed;
                lcdList[1].Visibility = (boardCount > 1 && currentStep.LCDRoiValue1.HasValue) ? Visibility.Visible : Visibility.Collapsed;
                lcdList[2].Visibility = (boardCount > 2 && currentStep.LCDRoiValue2.HasValue) ? Visibility.Visible : Visibility.Collapsed;
                lcdList[3].Visibility = (boardCount > 3 && currentStep.LCDRoiValue3.HasValue) ? Visibility.Visible : Visibility.Collapsed;
                if (currentStep.LCDRoiValue0.HasValue) lcdList[0].Rect = currentStep.LCDRoiValue0.Value;
                if (currentStep.LCDRoiValue1.HasValue) lcdList[1].Rect = currentStep.LCDRoiValue1.Value;
                if (currentStep.LCDRoiValue2.HasValue) lcdList[2].Rect = currentStep.LCDRoiValue2.Value;
                if (currentStep.LCDRoiValue3.HasValue) lcdList[3].Rect = currentStep.LCDRoiValue3.Value;
                Program.VisionTester.LcdFunctionUpdate();
                manualTestOn = false;
            }
        }

        private void UpdateFndRoi(Step currentStep)
        {
            // HasFndStore as well as the cmd check - see AutoPage.UpdateFndRoi for why.
            if (currentStep.CMD == CMDs.FND.ToString() && currentStep.HasFndStore)
            {
                int index_char = 0;
                foreach (var FNDchar in Program.VisionTester.Models.FNDs)
                {
                    if (TestModel.Layout.PCB_Count >= 1)
                    {
                        for (int index_led = 0; index_led < 7; index_led++)
                        {
                            FNDchar[0].PointSegments.LEDs[index_led].X = currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].X;
                            FNDchar[0].PointSegments.LEDs[index_led].Y = currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Y;
                            FNDchar[0].PointSegments.LEDs[index_led].Dir = currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Dir;
                            FNDchar[0].PointSegments.LEDs[index_led].ON = currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].ON;
                            FNDchar[0].PointSegments.LEDs[index_led].OFF = currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].OFF;
                            FNDchar[0].PointSegments.LEDs[index_led].Thresh = currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Thresh;
                            FNDchar[0].PointSegments.LEDs[index_led].Use = currentStep.FNDsBoard0[index_char].PointSegments.LEDs[index_led].Use;
                        }

                        FNDchar[0].Rect = currentStep.RectFNDsBoard0[index_char];
                        FNDchar[0].Use = currentStep.UseFNDsBoard0[index_char];
                    }

                    index_char++;
                }

                Program.VisionTester.FndFunctionUpdate();
                manualTestOn = false;
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

        private void GetImageSampleTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (Interlocked.CompareExchange(ref _fndProcessing, 1, 0) != 0) return;
            try
            {
                var lastFrameToTest = camera.LastMatFrame;
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
                var lastFrameToTest = camera.LastMatFrame;
                if (lastFrameToTest == null) return;
                Program.VisionTester.Models.GetLCDSampleImage(lastFrameToTest, ocr);
            }
            finally { Interlocked.Exchange(ref _lcdProcessing, 0); }
        }


        private void ButtonReset_Click(object sender, RoutedEventArgs e)
        {
            // Clear all steps' measured values + results, then jump back to the first step.
            if (Program?.TestModel?.Steps != null)
            {
                foreach (var step in Program.TestModel.Steps)
                {
                    step.ValueGet1 = "";
                    step.ValueGet2 = "";
                    step.ValueGet3 = "";
                    step.ValueGet4 = "";
                    step.Result1 = Step.DontCare;
                    step.Result2 = Step.DontCare;
                    step.Result3 = Step.DontCare;
                    step.Result4 = Step.DontCare;
                }
            }
            if (dgModelSteps.Items.Count > 1) dgModelSteps.SelectedIndex = 0;
        }

        private void btNextStep_Click(object sender, RoutedEventArgs e)
        {
            if (dgModelSteps.SelectedIndex > -1 && dgModelSteps.SelectedIndex + 1 < dgModelSteps.Items.Count)
            {
                manualTestOn = true;
                var currentStep = Program.TestModel.Steps[dgModelSteps.SelectedIndex + 1];
                Program.currentStep = currentStep;
                UpdateLcdRoi(currentStep);
                UpdateLedRoi(currentStep);
                UpdateFndRoi(currentStep);

                dgModelSteps.SelectedIndex = dgModelSteps.SelectedIndex + 1;
                Program.RunStep();
            }
        }

        private void cbbTxnaming_Mainual_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbbTxnaming_Manual.SelectedItem != null)
            {
                var txData = TestModel.Naming.TxDatas.Where(o => o.Name == (string)cbbTxnaming_Manual.SelectedItem).First();
                if (txData != null)
                {
                    var data = cbbUUTconfig_Manual.Text == "P1" ? TestModel.P1_Config.GetFrame(txData.Data) : TestModel.P2_Config.GetFrame(txData.Data);
                    string dataStr = "";
                    foreach (var item in data)
                    {
                        dataStr += item.ToString("X2") + " ";
                    }
                    lbTxData.Content = dataStr;
                }
            }
        }

        private void ButtonSendUUT_Click(object sender, RoutedEventArgs e)
        {
            if (cbbTxnaming_Manual.SelectedItem != null)
            {
                var txData = TestModel.Naming.TxDatas.Where(o => o.Name == (string)cbbTxnaming_Manual.SelectedItem).First();
                foreach (var item in Program.UUTs)
                {
                    if (cbbUUTconfig_Manual.Text == "P1" && item.Config != TestModel.P1_Config)
                    {
                        item.Config = TestModel.P1_Config;
                    }
                    else if (cbbUUTconfig_Manual.Text == "P2")
                    {
                        item.Config = TestModel.P1_Config;
                    }
                }
                if (txData != null)
                {
                    if (TestModel.Layout.PCB_Count >= 1) Program.UUTs[0].Send(txData);
                    if (TestModel.Layout.PCB_Count >= 2) Program.UUTs[1].Send(txData);
                    if (TestModel.Layout.PCB_Count >= 3) Program.UUTs[2].Send(txData);
                    if (TestModel.Layout.PCB_Count >= 4) Program.UUTs[3].Send(txData);
                }
            }
        }
    }
}