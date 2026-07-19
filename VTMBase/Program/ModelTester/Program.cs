using VTMBase;
using Utility;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Controls;
using Controls.DeviceControl;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using OpenCvSharp;
using System.Runtime.Remoting.Channels;
using Controls.DeviceControl.Camera;
using System.Windows.Media.Media3D;
using System.Windows.Documents;
using System.Reflection;
using static Controls.DeviceControl.DMM;
using System.Text;
using static OpenCvSharp.ML.DTrees;
using Controls.DeviceControl.Camera;
using System.Linq.Expressions;
using System.CodeDom.Compiler;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using OpenCvSharp.Flann;
using System.Drawing;

namespace VTMBase
{
    public partial class Program
    {
        public bool IsloadModel = false;

        private Model testModel = new Model();

        public Model TestModel
        {
            get { return testModel; }
            set
            {
                TestState = RunTestState.STOP;
                if (value != testModel)
                {
                    testModel = value;
                    SetBoards();
                    IsloadModel = true;
                }
                TestState = RunTestState.WAIT;
            }
        }

        public FolderMap AppFolder = new FolderMap();

        public event EventHandler StepTestChange;

        public event EventHandler AutoPageStepTestChange;

        public event EventHandler TestRunFinish;

        public event EventHandler StateChange;

        public event EventHandler EscapTimeChange;

        public event EventHandler TesttingStateChange;

        // Serializes the run's terminal transition against MachineIO_OnCancleRequest, which fires on the serial RX
        // thread. Whoever takes it first wins: a cancel makes STOP stick (the result is then rejected), and a
        // concluded run makes the cancel a no-op. Without it both threads can read TESTTING and overwrite each other.
        private readonly object _stateLock = new object();

        // --- Shared test-loop helpers (extracted to remove copy-paste between the auto and manual state machines) ---

        // Per-site step-result column selectors (Result1..Result4), indexed by site.
        private static readonly Func<Step, string>[] _siteResultSelectors =
            { s => s.Result1, s => s.Result2, s => s.Result3, s => s.Result4 };

        // Compute each active site's overall Result from its step-result column: SKIP when the site's skip flag is
        // set, else FAIL if any step logged Step.Ng, else OK. useUserSkip picks UserSkip (final verdict) vs Skip
        // (per-run). Replaces five unrolled "if (Boards.Count >= n)" blocks that differed only by the skip flag.
        private void ApplyBoardResults(IEnumerable<Step> steps, bool useUserSkip)
        {
            for (int i = 0; i < Boards.Count && i < 4; i++)
            {
                bool skip = useUserSkip ? Boards[i].UserSkip : Boards[i].Skip;
                Boards[i].Result = skip
                    ? "SKIP"
                    : steps.Select(_siteResultSelectors[i]).Contains(Step.Ng) ? "FAIL" : "OK";
            }
        }

        // Close the end-of-test timestamp on every active (non-user-skipped) site.
        private void StampEndTest()
        {
            foreach (var item in Boards)
                if (!item.UserSkip) item.EndTest = DateTime.Now;
        }

        // Open the start-of-test timestamp on every active (non-skipped) site.
        private void StampStartTest()
        {
            foreach (var item in Boards)
                if (!item.Skip) item.StartTest = DateTime.Now;
        }

        // Shared fail-stop handling for both the auto and manual test loops: a failed resistance step (with
        // FailResistanceStopAll) forces STOP; any failed step (with FailStopAll) forces FAIL. Writes IsTestting/
        // TestState unlocked - exactly as the two inline copies did (the concluding block still uses _stateLock).
        private void ApplyFailStopFlags(Step stepTest, bool isPass)
        {
            if (appSetting.Operations.FailResistanceStopAll && stepTest.cmd == CMDs.RES && !isPass)
            {
                Debug.Write("Resistance step failed - force stop all sites", Debug.ContentType.Error);
                IsTestting = false;
                TestState = RunTestState.STOP;
            }
            if (appSetting.Operations.FailStopAll && !isPass)
            {
                Debug.Write("Step failed - force stop all sites", Debug.ContentType.Error);
                IsTestting = false;
                TestState = RunTestState.FAIL;
            }
        }

        private bool _IsTestting;

        public bool IsTestting
        {
            get { return _IsTestting; }
            set
            {
                if (value != _IsTestting)
                {
                    _IsTestting = value;
                    TesttingStateChange?.Invoke(value, null);
                }
            }
        }

        public int StepTesting = 0;

        private int FailReTestStep = 0;

        private double _EscapTime;

        public double EscapTime
        {
            get { return _EscapTime; }
            set
            {
                _EscapTime = value;
                EscapTimeChange?.Invoke(_EscapTime, null);
            }
        }

        private System.Timers.Timer EscapTimer = new System.Timers.Timer()
        {
            Interval = 100,
            Enabled = true,
        };

        public enum RunTestState
        {
            WAIT,
            READY,
            TESTTING,
            MANUALTEST,
            STOP,
            PAUSE,
            GOOD,
            FAIL,
            BUSY,
            DONE,
        }

        private RunTestState testState;

        public RunTestState TestState
        {
            get { return testState; }
            set
            {
                if (value != testState)
                {
                    DiagLog.Write("STATE", $"{testState}→{value} IsTestting={IsTestting}");
                    testState = value;
                    StateChange?.Invoke(value, null);
                    switch (testState)
                    {
                        case RunTestState.WAIT:
                            if (TestModel.BarcodeOption.UseBarcodeInput)
                            {
                                Debug.Write("SCANNER:WAITING FOR BARCODE", Debug.ContentType.Warning);
                            }
                            EscapTimer.Stop();
                            EscapTime = 0;
                            break;

                        case RunTestState.READY:
                            Debug.Write("Ready", Debug.ContentType.Notify, 15);
                            EscapTimer.Stop();
                            break;

                        case RunTestState.TESTTING:
                            Debug.Write("Begin", Debug.ContentType.Warning, 20);
                            EscapTime = 0;
                            EscapTimer.Start();
                            break;

                        case RunTestState.MANUALTEST:
                            EscapTimer.Stop();
                            EscapTime = 0;
                            break;

                        case RunTestState.STOP:
                            Debug.Write("End: STOP", Debug.ContentType.Warning, 15);
                            EscapTimer.Stop();
                            EscapTime = 0;
                            break;

                        case RunTestState.PAUSE:
                            break;

                        case RunTestState.GOOD:
                            Debug.Write("End: PASS", Debug.ContentType.Notify, 15);
                            EscapTimer.Stop();
                            break;

                        case RunTestState.FAIL:
                            Debug.Write("End: FAIL", Debug.ContentType.Error, 15);
                            EscapTimer.Stop();
                            EscapTime = 0;
                            break;

                        case RunTestState.BUSY:
                            EscapTimer.Stop();
                            EscapTime = 0;
                            break;

                        case RunTestState.DONE:
                            EscapTimer.Stop();
                            EscapTime = 0;
                            break;

                        default:
                            EscapTimer.Stop();
                            EscapTime = 0;
                            break;
                    }
                }
            }
        }

        public enum PageActive
        {
            AutoPage,
            ManualPage,
            ModelPage,
            VistionPage
        }

        public PageActive pageActive;

        public async void START()
        {
            TestState = RunTestState.BUSY;
            await Task.Run(ProgramState);
        }

        // Pull in each site's scanned-ahead barcode (BarcodeNext) if it doesn't already hold one. A scan-ahead
        // counts as "already scanned", so the run can go straight to READY instead of asking the operator to scan
        // again. Only fills empty sites, so a fresh scan is never overwritten.
        // Returns true once every non-skipped site holds a valid barcode.
        private bool LoadScannedAheadBarcodes()
        {
            bool ready = true;
            foreach (var item in Boards)
            {
                if (item.Skip) continue;
                if (!item.BarcodeReady && item.BarcodeNext != "")
                {
                    // Two lines: the conveyor step (slot 1 takes from slot 2), then the barcode now current in slot 1.
                    Debug.Write(String.Format("SCANNER:BUFFER 1 {0} BUFFER 2", (char)0x2190), Debug.ContentType.Notify);
                    Debug.Write(String.Format("SCANNER:CURRENT BARCODE  {0}", item.BarcodeNext), Debug.ContentType.Notify);
                    item.Barcode = item.BarcodeNext;
                    item.BarcodeIsFake = item.BarcodeNextIsFake;   // the flag rides along with the barcode
                    item.BarcodeNext = "";
                    item.BarcodeNextIsFake = false;
                }
                ready &= item.BarcodeReady;
            }
            return ready;
        }

        private async Task ProgramState()
        {
            while (true)
            {
                switch (TestState)
                {
                    case RunTestState.WAIT:
                        //TestModel.BarcodeOption.UseBarcodeInput
                        if (TestModel.BarcodeOption.UseBarcodeInput)
                        {
                            foreach (var item in Boards)
                            {
                                item.Skip = item.UserSkip;
                            }
                            // Take the scan-ahead here, not only at the end of a run: this way EVERY path into WAIT
                            // (GOOD / FAIL / STOP) picks it up, and a barcode scanned ahead means WAIT flips straight
                            // to READY instead of asking the operator to scan again.
                            bool Realdy = LoadScannedAheadBarcodes();

                            // Realdy
                            if (Realdy)
                            {
                                System.System_Board.MachineIO.BUZZER = false;
                                // No MainUP here: CLUP is a momentary push, not a level. Only a PASS raises the jig.
                                System.System_Board.PowerRelease();
                                Relay.Card.Release();
                                Solenoid.Card.Release();
                                MuxCard.Card.ReleaseChannels();
                                TestState = RunTestState.READY;
                            }
                            else if (IsTestting)
                            {
                                IsTestting = false;
                                Debug.Write("No barcode.", Debug.ContentType.Error, 30);
                                await Task.Delay(500);
                                System.System_Board.MachineIO.BUZZER = false;
                                // Pressed down with no barcode loaded: there is nothing to test, so pulse the jig
                                // back up instead of leaving the operator holding it down. Same momentary push as
                                // the PASS raise - assert, give it time to lift, release.
                                System.System_Board.MachineIO.MainUP = true;
                                System.System_Board.SendControl();
                                await Task.Delay(appSetting.Operations.TestPressUpTime);
                                System.System_Board.MachineIO.MainUP = false;
                                System.System_Board.SendControl();
                            }
                        }
                        else
                        {
                            // No MainUP here either - only a PASS raises the jig.
                            System.System_Board.PowerRelease();
                            Relay.Card.Release();
                            Solenoid.Card.Release();
                            MuxCard.Card.ReleaseChannels();
                            TestState = RunTestState.READY;
                        }
                        break;

                    case RunTestState.TESTTING:
                        //Clear boar detail

                        foreach (var item in Boards)
                        {
                            item.BoardDetail = "";
                            item.Skip = item.UserSkip;
                        }
                        //Delay before start
                        System.System_Board.PowerRelease();
                        Relay.Card.Release();
                        Solenoid.Card.Release();
                        MuxCard.Card.ReleaseChannels();
                        await Task.Delay(appSetting.Operations.StartDelaytime);

                        // Cleaning steps and set start parametter to boards
                        TestModel.CleanSteps();
                        IsTestting = true;
                        StepTesting = 0;
                        var Steps = TestModel.Steps;
                        StampStartTest();

                        //Discharge
                        if (TestModel.Discharge.CheckBeforeTest || appSetting.ETCSetting.UseDischargeTestStart)
                        {
                            if (!DisCharge() && appSetting.ETCSetting.UseDischargeError)
                            {
                                TestState = RunTestState.FAIL;
                                IsTestting = false;
                            }
                        }
                        //Start Test
                        while (IsTestting)
                        {
                            //Test done without END command
                            if (StepTesting >= Steps.Count)
                            {
                                bool TestOK = true;
                                ApplyBoardResults(Steps, useUserSkip: true);

                                StampEndTest();

                                System.System_Board.MachineIO.ADSC = true;
                                System.System_Board.MachineIO.BDSC = true;
                                TestOK = Boards.Select(x => x.Result).Contains("FAIL");   // true = has a fail

                                // Conclude atomically against a cancel on the serial RX thread. If a STOP already
                                // landed, it WINS: the run was cancelled, so its result (even a FAIL) is rejected
                                // and the STOP state/outputs stand. Otherwise conclude here, which in turn makes
                                // any later cancel a no-op.
                                bool concluded;
                                lock (_stateLock)
                                {
                                    concluded = TestState == RunTestState.TESTTING;
                                    if (concluded) TestState = TestOK ? RunTestState.FAIL : RunTestState.GOOD;
                                    IsTestting = false;
                                }
                                if (!concluded) break;   // stopped -> keep STOP, drop the result

                                // Outputs only AFTER the state is concluded: raising the cylinder drops SS_DOWN, and
                                // a cancel arriving while the state still read TESTTING would overwrite the result.
                                // PASS: raise. NG: keep the cylinder down, so it gets no raise at all.
                                if (!TestOK) System.System_Board.MachineIO.MainUP = true;
                                System.System_Board.MachineIO.LPG = true;
                                System.System_Board.SendControl();          // CLUP:1 (+ lamp) goes out immediately
                                ResultPanel.ShowResult(Boards.ToList());

                                if (!TestOK)
                                {
                                    // CLUP is a MOMENTARY push, not a level: hold it long enough for the jig to
                                    // lift, then release. Holding longer than needed is harmless (the board's
                                    // interlock drops CLUP by itself once SDOWN reports the jig is up).
                                    await Task.Delay(appSetting.Operations.TestPressUpTime);
                                    System.System_Board.MachineIO.MainUP = false;
                                    System.System_Board.SendControl();      // CLUP:0
                                }
                                break;
                            }
                            else
                            {
                                var stepTest = Steps[StepTesting];
                                if (stepTest != null)
                                {
                                    StepTestChange?.Invoke(StepTesting, null);
                                    if (stepTest.cmd != CMDs.NON && Steps[StepTesting].cmd != CMDs.END && !stepTest.Skip)
                                    {
                                        bool IsPass = RUN_FUNCTION_TEST(stepTest);

                                        //Test pass and ejump
                                        if (!IsPass && stepTest.E_Jump != 0)
                                        {
                                            FailReTestStep = stepTest.E_Jump - 1;
                                            int StepResetErr = stepTest.No - 1;
                                            for (int i = 0; i < appSetting.Operations.ErrorJumpCount; i++)
                                            {
                                                for (int stepRetest = FailReTestStep; stepRetest <= StepResetErr; stepRetest++)
                                                {
                                                    StepTesting = stepRetest;
                                                    StepTestChange?.Invoke(StepTesting, null);
                                                    stepTest = Steps[stepRetest];
                                                    IsPass = RUN_FUNCTION_TEST(stepTest);
                                                    if (IsPass && stepRetest == StepResetErr)
                                                    {
                                                        break;
                                                    }
                                                    if (!IsTestting || TestState != RunTestState.TESTTING)
                                                    {
                                                        break;
                                                    }
                                                    await Task.Delay(100);
                                                }
                                                if (IsPass)
                                                {
                                                    break;
                                                }
                                                if (!IsTestting || TestState != RunTestState.TESTTING)
                                                {
                                                    break;
                                                }
                                            }
                                        }

                                        ApplyFailStopFlags(stepTest, IsPass);

                                        //Skip fail Step
                                        if (appSetting.Operations.FailStopPCB && !IsPass)
                                        {
                                            ApplyBoardResults(Steps, useUserSkip: false);

                                            bool TestOK = true;

                                            ApplyBoardResults(Steps, useUserSkip: true);

                                            TestOK = Boards.Select(x => x.Result).Contains("OK");
                                            if (!TestOK)
                                            {
                                                StampEndTest();

                                                System.System_Board.MachineIO.ADSC = false;
                                                System.System_Board.MachineIO.BDSC = false;
                                                System.System_Board.MachineIO.MainUP = false;
                                                System.System_Board.MachineIO.LPG = false;
                                                System.System_Board.SendControl();
                                                TestState = RunTestState.FAIL;
                                                ResultPanel.ShowResult(Boards.ToList());
                                                IsTestting = false;
                                                break;
                                            }
                                            else
                                            {
                                                if (Boards.Count >= 1) if (!Boards[0].Skip) Boards[0].Skip = Boards[0].Result == "FAIL";
                                                if (Boards.Count >= 2) if (!Boards[1].Skip) Boards[1].Skip = Boards[1].Result == "FAIL";
                                                if (Boards.Count >= 3) if (!Boards[2].Skip) Boards[2].Skip = Boards[2].Result == "FAIL";
                                                if (Boards.Count >= 4) if (!Boards[3].Skip) Boards[3].Skip = Boards[3].Result == "FAIL";

                                                Debug.Write("Step failed - skip sites:", Debug.ContentType.Warning);
                                                if (Boards.Count >= 1) if (Boards[0].Skip) Debug.Write("Site A", Debug.ContentType.Warning);
                                                if (Boards.Count >= 2) if (Boards[1].Skip) Debug.Write("Site B", Debug.ContentType.Warning);
                                                if (Boards.Count >= 3) if (Boards[2].Skip) Debug.Write("Site C", Debug.ContentType.Warning);
                                                if (Boards.Count >= 4) if (Boards[3].Skip) Debug.Write("Site D", Debug.ContentType.Warning);
                                            }
                                        }
                                    }

                                    if (stepTest.cmd == CMDs.END)
                                    {
                                        bool TestOK = true;
                                        ApplyBoardResults(Steps, useUserSkip: true);

                                        StampEndTest();

                                        System.System_Board.MachineIO.ADSC = true;
                                        System.System_Board.MachineIO.BDSC = true;

                                        System.System_Board.SendControl();
                                        TestOK = Boards.Select(x => x.Result).Contains("FAIL");
                                        TestState = TestOK ? RunTestState.FAIL : RunTestState.GOOD;
                                        ResultPanel.ShowResult(Boards.ToList());
                                        IsTestting = false;
                                        break;
                                    }

                                    if (stepTest.cmd == CMDs.UCN)
                                    {
                                        if (Boards.Count >= 1) if (!Boards[0].Skip && CommandDescriptions.CommandRemark_Version.Contains(stepTest.Remark)) Boards[0].BoardDetail += stepTest.Remark + ": " + stepTest.ValueGet1 + " ";
                                        if (Boards.Count >= 2) if (!Boards[1].Skip && CommandDescriptions.CommandRemark_Version.Contains(stepTest.Remark)) Boards[1].BoardDetail += stepTest.Remark + ": " + stepTest.ValueGet2 + " ";
                                        if (Boards.Count >= 3) if (!Boards[2].Skip && CommandDescriptions.CommandRemark_Version.Contains(stepTest.Remark)) Boards[2].BoardDetail += stepTest.Remark + ": " + stepTest.ValueGet3 + " ";
                                        if (Boards.Count >= 4) if (!Boards[3].Skip && CommandDescriptions.CommandRemark_Version.Contains(stepTest.Remark)) Boards[3].BoardDetail += stepTest.Remark + ": " + stepTest.ValueGet4 + " ";
                                    }

                                    await Task.Delay(10); // delay for data binding
                                }
                                StepTesting++;
                            }
                        }
                        break;

                    case RunTestState.MANUALTEST:
                        StepTesting = 0;
                        Steps = TestModel.Steps;
                        foreach (var item in Boards)
                        {
                            item.Skip = item.UserSkip;
                        }
                        //Start Test
                        while (IsTestting)
                        {
                            while (TestState == RunTestState.PAUSE)
                            {
                                await Task.Delay(500);
                            }

                            //Test done without END command
                            if (StepTesting >= Steps.Count)
                            {
                                //Infinite manual test: loop back to step 1 instead of stopping
                                if (ManualLoop)
                                {
                                    StepTesting = 0;
                                    await Task.Delay(10);
                                    continue;
                                }

                                System.System_Board.MachineIO.ADSC = true;
                                System.System_Board.MachineIO.BDSC = true;
                                System.System_Board.SendControl();
                                await Task.Delay(1000);

                                System.System_Board.MachineIO.ADSC = false;
                                System.System_Board.MachineIO.BDSC = false;
                                System.System_Board.SendControl();
                                IsTestting = false;
                                TestRunFinish?.Invoke(null, null);
                                break;
                            }
                            else
                            {
                                var lastStep = StepTesting;
                                var stepTest = Steps[StepTesting];
                                if (stepTest != null)
                                {
                                    StepTestChange?.Invoke(StepTesting, null);
                                    if (stepTest.cmd != CMDs.NON && !stepTest.Skip)
                                    {
                                        bool IsPass = RUN_FUNCTION_TEST(stepTest);

                                        //Test pass and ejump
                                        if (!IsPass && stepTest.E_Jump != 0)
                                        {
                                            FailReTestStep = stepTest.E_Jump - 1;
                                            int StepResetErr = stepTest.No - 1;
                                            for (int i = 0; i < appSetting.Operations.ErrorJumpCount; i++)
                                            {
                                                for (int stepRetest = FailReTestStep; stepRetest <= StepResetErr; stepRetest++)
                                                {
                                                    while (TestState == RunTestState.PAUSE)
                                                    {
                                                        await Task.Delay(500);
                                                    }

                                                    StepTesting = stepRetest;
                                                    StepTestChange?.Invoke(StepTesting, null);
                                                    stepTest = Steps[stepRetest];
                                                    IsPass = RUN_FUNCTION_TEST(stepTest);
                                                    if (IsPass && stepRetest == StepResetErr)
                                                    {
                                                        StepTesting = lastStep;
                                                        break;
                                                    }
                                                    if (!IsTestting)
                                                    {
                                                        StepTesting = lastStep;
                                                        break;
                                                    }
                                                    await Task.Delay(100);
                                                }
                                                if (!IsTestting)
                                                {
                                                    StepTesting = lastStep;
                                                    break;
                                                }
                                                StepTesting = lastStep;
                                            }
                                        }

                                        ApplyFailStopFlags(stepTest, IsPass);

                                        //Skip fail Step
                                        if (appSetting.Operations.FailStopPCB && !IsPass)
                                        {
                                            ApplyBoardResults(Steps, useUserSkip: false);

                                            if (Boards.Count >= 1) if (!Boards[0].Skip) Boards[0].Skip = Boards[0].Result != "OK";
                                            if (Boards.Count >= 2) if (!Boards[1].Skip) Boards[1].Skip = Boards[1].Result != "OK";
                                            if (Boards.Count >= 3) if (!Boards[2].Skip) Boards[2].Skip = Boards[2].Result != "OK";
                                            if (Boards.Count >= 4) if (!Boards[3].Skip) Boards[3].Skip = Boards[3].Result != "OK";
                                            Debug.Write("Step failed - skip sites:", Debug.ContentType.Warning);
                                            if (Boards.Count >= 1) if (Boards[0].Skip) Debug.Write("Site A", Debug.ContentType.Warning);
                                            if (Boards.Count >= 2) if (Boards[1].Skip) Debug.Write("Site B", Debug.ContentType.Warning);
                                            if (Boards.Count >= 3) if (Boards[2].Skip) Debug.Write("Site C", Debug.ContentType.Warning);
                                            if (Boards.Count >= 4) if (Boards[3].Skip) Debug.Write("Site D", Debug.ContentType.Warning);
                                        }

                                        if (!IsTestting)
                                        {
                                            StepTesting = lastStep;
                                            break;
                                        }
                                    }

                                    if (stepTest.cmd == CMDs.END)
                                    {
                                        //Infinite manual test: loop back to step 1 instead of stopping
                                        if (ManualLoop)
                                        {
                                            StepTesting = 0;
                                            await Task.Delay(10);
                                            continue;
                                        }

                                        System.System_Board.MachineIO.ADSC = true;
                                        System.System_Board.MachineIO.BDSC = true;
                                        System.System_Board.SendControl();

                                        await Task.Delay(1000);

                                        System.System_Board.MachineIO.ADSC = false;
                                        System.System_Board.MachineIO.BDSC = false;
                                        System.System_Board.SendControl();
                                        IsTestting = false;
                                        TestRunFinish?.Invoke(null, null);
                                        break;
                                    }

                                    await Task.Delay(10); // delay for data binding
                                }
                                StepTesting++;
                            }
                        }
                        break;

                    case RunTestState.PAUSE:
                        await Task.Delay(100);
                        break;

                    case RunTestState.STOP:

                        IsTestting = false;
                        StepTesting = 0;
                        StepTestChange?.Invoke(StepTesting, null);
                        System.System_Board.MachineIO.ADSC = true;
                        System.System_Board.MachineIO.BDSC = true;
                        System.System_Board.MachineIO.LPR = true;
                        System.System_Board.SendControl();
                        await Task.Delay(2000);
                        System.System_Board.MachineIO.ADSC = false;
                        System.System_Board.MachineIO.BDSC = false;
                        System.System_Board.MachineIO.MainUP = false;
                        System.System_Board.MachineIO.LPR = true;
                        System.System_Board.SendControl();
                        await Task.Delay(1000);

                        if (TestModel.BarcodeOption.UseBarcodeInput)
                        {
                            TestState = RunTestState.WAIT;
                        }
                        else
                        {
                            TestState = RunTestState.READY;
                        }
                        foreach (var item in Boards)
                        {
                            item.Barcode = "";
                            item.Result = "";
                            item.Skip = item.UserSkip;
                        }
                        break;

                    case RunTestState.GOOD:
                        TestRunFinish?.Invoke("", null);
                        if (Boards.Count >= 1)
                        {
                            if (!Boards[0].Skip && Boards[0].Result == "OK")
                            {
                                if (TestModel.BarcodeOption.UseBarcodeInput)
                                {
                                    //Printer.GT800.SendStringToPrinter(Printer.QRcode.GenerateCode("A", Boards[0].Barcode, Boards[0].StartTest, Boards[0].EndTest,
                                    //    TestModel.Steps.Select(x => x.IMQSCode).ToList(), TestModel.Steps.Select(x => x.Min).ToList(), TestModel.Steps.Select(x => x.Max).ToList(),
                                    //    TestModel.Steps.Select(x => x.ValueGet1).ToList(), TestModel.Steps.Where(x => x.Remark == "MAIN VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet1,
                                    //    TestModel.Steps.Where(x => x.Remark == "SUB VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet1, out string barcodeOut));
                                    //Boards[0].QRout = barcodeOut;
                                    //Debug.Write("Board A: GOOD - qr printed:" + Boards[0].QRout, Debug.ContentType.Notify);
                                }
                            }
                        }
                        if (Boards.Count >= 2)
                        {
                            if (!Boards[1].Skip && Boards[1].Result == "OK")
                            {
                                if (TestModel.BarcodeOption.UseBarcodeInput)
                                {
                                    //Printer.GT800.SendStringToPrinter(Printer.QRcode.GenerateCode("B", Boards[1].Barcode, Boards[1].StartTest, Boards[1].EndTest,
                                    //    TestModel.Steps.Select(x => x.IMQSCode).ToList(), TestModel.Steps.Select(x => x.Min).ToList(), TestModel.Steps.Select(x => x.Max).ToList(),
                                    //    TestModel.Steps.Select(x => x.ValueGet2).ToList(), TestModel.Steps.Where(x => x.Remark == "MAIN VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet2,
                                    //    TestModel.Steps.Where(x => x.Remark == "SUB VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet2, out string barcodeOut));
                                    //Boards[1].QRout = barcodeOut;
                                    //Debug.Write("Board B: GOOD - qr printed:" + Boards[1].QRout, Debug.ContentType.Notify);
                                }
                            }
                        }
                        if (Boards.Count >= 3)
                        {
                            if (!Boards[2].Skip && Boards[2].Result == "OK")
                            {
                                if (TestModel.BarcodeOption.UseBarcodeInput)
                                {
                                    //Printer.GT800.SendStringToPrinter(Printer.QRcode.GenerateCode("C", Boards[2].Barcode, Boards[2].StartTest, Boards[2].EndTest,
                                    //    TestModel.Steps.Select(x => x.IMQSCode).ToList(), TestModel.Steps.Select(x => x.Min).ToList(), TestModel.Steps.Select(x => x.Max).ToList(),
                                    //    TestModel.Steps.Select(x => x.ValueGet3).ToList(), TestModel.Steps.Where(x => x.Remark == "MAIN VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet3,
                                    //    TestModel.Steps.Where(x => x.Remark == "SUB VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet3, out string barcodeOut));
                                    //Boards[2].QRout = barcodeOut;
                                    //Debug.Write("Board C: GOOD - qr printed:" + Boards[2].QRout, Debug.ContentType.Notify);
                                }
                            }
                        }
                        if (Boards.Count >= 4)
                        {
                            if (!Boards[3].Skip && Boards[3].Result == "OK")
                            {
                                if (TestModel.BarcodeOption.UseBarcodeInput)
                                {
                                    //Printer.GT800.SendStringToPrinter(Printer.QRcode.GenerateCode("D", Boards[3].Barcode, Boards[3].StartTest, Boards[3].EndTest,
                                    //    TestModel.Steps.Select(x => x.IMQSCode).ToList(), TestModel.Steps.Select(x => x.Min).ToList(), TestModel.Steps.Select(x => x.Max).ToList(),
                                    //    TestModel.Steps.Select(x => x.ValueGet4).ToList(), TestModel.Steps.Where(x => x.Remark == "MAIN VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet4,
                                    //    TestModel.Steps.Where(x => x.Remark == "SUB VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet4, out string barcodeOut));
                                    //Boards[3].QRout = barcodeOut;
                                    //Debug.Write("Board D: GOOD - qr printed:" + Boards[3].QRout, Debug.ContentType.Notify);
                                }
                            }
                        }

                        foreach (var board in Boards)
                        {
                            if (!board.UserSkip)
                            {
                                board.TestStep = TestModel.Steps.ToList();
                                if (appSetting.Operations.SaveFailPCB)
                                {
                                    bool is_final_result_fail = false;

                                    if (board.TestStep != null)
                                    {
                                        if (board.SiteName == "A")
                                        {
                                            is_final_result_fail = !board.TestStep.All(step => step.Result1 == Step.Ok || step.Skip == true);
                                        }
                                        if (board.SiteName == "B")
                                        {
                                            is_final_result_fail = !board.TestStep.All(step => step.Result2 == Step.Ok || step.Skip == true);
                                        }
                                        if (board.SiteName == "C")
                                        {
                                            is_final_result_fail = !board.TestStep.All(step => step.Result3 == Step.Ok || step.Skip == true);
                                        }
                                        if (board.SiteName == "D")
                                        {
                                            is_final_result_fail = !board.TestStep.All(step => step.Result4 == Step.Ok || step.Skip == true);
                                        }
                                    }
                                    //AppFolder.SaveHistory(item);

                                    AppFolder.SaveLogFile(is_final_result_fail, board, StepTesting, appSetting.Operations.ExportLog && !board.BarcodeIsFake);
                                }
                                else
                                {
                                    if (board.Result == "OK")
                                    {
                                        AppFolder.SaveHistory(board);
                                    }
                                }
                            }
                        }
                        Relay.Card.Release();
                        Solenoid.Card.Release();
                        MuxCard.Card.ReleaseChannels();
                        System.System_Board.MachineIO.ADSC = true;
                        System.System_Board.MachineIO.BDSC = true;
                        System.System_Board.MachineIO.LPG = true;
                        System.System_Board.MachineIO.LPY = false;
                        System.System_Board.SendControl();
                        await Task.Delay(1000);
                        System.System_Board.MachineIO.ADSC = false;
                        System.System_Board.MachineIO.BDSC = false;
                        // No MainUP here: the test-done block already pulsed CLUP up on PASS. Raising again from
                        // GOOD only fired a second, pointless CLUP:1 a second later.
                        System.System_Board.SendControl();
                        await Task.Delay(1000);
                        foreach (var item in Boards)
                        {
                            item.Barcode = "";
                            item.Skip = item.UserSkip;
                        }
                        if (TestModel.BarcodeOption.UseBarcodeInput)
                        {
                            // Straight to READY if the operator already scanned ahead; otherwise WAIT for a scan.
                            // (WAIT re-runs this too, so a scan-ahead is never stranded whichever way we got there.)
                            TestState = LoadScannedAheadBarcodes() ? RunTestState.READY : RunTestState.WAIT;
                        }
                        else
                        {
                            TestState = RunTestState.READY;
                        }
                        break;

                    case RunTestState.FAIL:
                        TestRunFinish?.Invoke("", null);
                        if (Printer.QRcode.TestPCBPassPrint)
                        {
                            if (Boards.Count >= 1)
                            {
                                if (!Boards[0].Skip && Boards[0].Result == "OK")
                                {
                                    if (TestModel.BarcodeOption.UseBarcodeInput)
                                    {
                                        //Printer.GT800.SendStringToPrinter(Printer.QRcode.GenerateCode("A", Boards[0].Barcode, Boards[0].StartTest, Boards[0].EndTest,
                                        //    TestModel.Steps.Select(x => x.IMQSCode).ToList(), TestModel.Steps.Select(x => x.Min).ToList(), TestModel.Steps.Select(x => x.Max).ToList(),
                                        //    TestModel.Steps.Select(x => x.ValueGet1).ToList(), TestModel.Steps.Where(x => x.Remark == "MAIN VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet1,
                                        //    TestModel.Steps.Where(x => x.Remark == "SUB VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet1, out string barcodeOut));
                                        //Boards[0].QRout = barcodeOut;
                                        //Debug.Write("\t\tBoard A: GOOD - qr printed:" + Boards[0].QRout, Debug.ContentType.Notify);
                                    }
                                }
                                else
                                {
                                    // Per-board result line removed: it just duplicates the "End: FAIL" summary below.
                                    //Debug.Write(String.Format("Board A: {0}", Boards[0].Result), Debug.ContentType.Error);
                                }
                            }
                            if (Boards.Count >= 2)
                            {
                                if (!Boards[1].Skip && Boards[1].Result == "OK")
                                {
                                    if (TestModel.BarcodeOption.UseBarcodeInput)
                                    {
                                        //Printer.GT800.SendStringToPrinter(Printer.QRcode.GenerateCode("B", Boards[1].Barcode, Boards[1].StartTest, Boards[1].EndTest,
                                        //    TestModel.Steps.Select(x => x.IMQSCode).ToList(), TestModel.Steps.Select(x => x.Min).ToList(), TestModel.Steps.Select(x => x.Max).ToList(),
                                        //    TestModel.Steps.Select(x => x.ValueGet2).ToList(), TestModel.Steps.Where(x => x.Remark == "MAIN VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet2,
                                        //    TestModel.Steps.Where(x => x.Remark == "SUB VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet2, out string barcodeOut));
                                        //Boards[1].QRout = barcodeOut;
                                        //Debug.Write("Board B: GOOD - qr printed:" + Boards[1].QRout, Debug.ContentType.Notify);
                                    }
                                }
                                else
                                {
                                    //Debug.Write(String.Format("Board B: {0}", Boards[1].Result), Debug.ContentType.Error);
                                }
                            }
                            if (Boards.Count >= 3)
                            {
                                if (!Boards[2].Skip && Boards[2].Result == "OK")
                                {
                                    if (TestModel.BarcodeOption.UseBarcodeInput)
                                    {
                                        //Printer.GT800.SendStringToPrinter(Printer.QRcode.GenerateCode("C", Boards[2].Barcode, Boards[2].StartTest, Boards[2].EndTest,
                                        //    TestModel.Steps.Select(x => x.IMQSCode).ToList(), TestModel.Steps.Select(x => x.Min).ToList(), TestModel.Steps.Select(x => x.Max).ToList(),
                                        //    TestModel.Steps.Select(x => x.ValueGet3).ToList(), TestModel.Steps.Where(x => x.Remark == "MAIN VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet3,
                                        //    TestModel.Steps.Where(x => x.Remark == "SUB VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet3, out string barcodeOut));
                                        //Boards[2].QRout = barcodeOut;
                                        //Debug.Write("Board C: GOOD - qr printed:" + Boards[2].QRout, Debug.ContentType.Notify);
                                    }
                                }
                                else
                                {
                                    //Debug.Write(String.Format("Board C: {0}", Boards[2].Result), Debug.ContentType.Error);
                                }
                                if (Boards.Count >= 4)
                                {
                                    if (!Boards[3].Skip && Boards[3].Result == "OK")
                                    {
                                        if (TestModel.BarcodeOption.UseBarcodeInput)
                                        {
                                            //Printer.GT800.SendStringToPrinter(Printer.QRcode.GenerateCode("D", Boards[3].Barcode, Boards[3].StartTest, Boards[3].EndTest,
                                            //    TestModel.Steps.Select(x => x.IMQSCode).ToList(), TestModel.Steps.Select(x => x.Min).ToList(), TestModel.Steps.Select(x => x.Max).ToList(),
                                            //    TestModel.Steps.Select(x => x.ValueGet4).ToList(), TestModel.Steps.Where(x => x.Remark == "MAIN VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet4,
                                            //    TestModel.Steps.Where(x => x.Remark == "SUB VERSION").DefaultIfEmpty(new Step()).FirstOrDefault().ValueGet4, out string barcodeOut));
                                            //Boards[3].QRout = barcodeOut;
                                            //Debug.Write("Board D: GOOD - qr printed:" + Boards[3].QRout, Debug.ContentType.Notify);
                                        }
                                    }
                                    else
                                    {
                                        //Debug.Write(String.Format("Board D: {0}", Boards[3].Result), Debug.ContentType.Error);
                                    }
                                }
                            }

                            foreach (var board in Boards)
                            {
                                if (!board.UserSkip)
                                {
                                    board.TestStep = TestModel.Steps.ToList();
                                    if (appSetting.Operations.SaveFailPCB)
                                    {
                                        bool is_final_result_fail = false;

                                        if (board.TestStep != null)
                                        {
                                            if (board.SiteName == "A")
                                            {
                                                is_final_result_fail = !board.TestStep.All(step => step.Result1 == Step.Ok || step.Skip == true);
                                            }
                                            if (board.SiteName == "B")
                                            {
                                                is_final_result_fail = !board.TestStep.All(step => step.Result2 == Step.Ok || step.Skip == true);
                                            }
                                            if (board.SiteName == "C")
                                            {
                                                is_final_result_fail = !board.TestStep.All(step => step.Result3 == Step.Ok || step.Skip == true);
                                            }
                                            if (board.SiteName == "D")
                                            {
                                                is_final_result_fail = !board.TestStep.All(step => step.Result4 == Step.Ok || step.Skip == true);
                                            }
                                        }

                                        //AppFolder.SaveHistory(item);
                                        AppFolder.SaveLogFile(is_final_result_fail, board, StepTesting, appSetting.Operations.ExportLog && !board.BarcodeIsFake);
                                    }
                                    else
                                    {
                                        if (board.Result == "OK")
                                        {
                                            AppFolder.SaveHistory(board);
                                        }
                                    }
                                }
                            }
                        }
                        Relay.Card.Release();
                        Solenoid.Card.Release();
                        MuxCard.Card.ReleaseChannels();
                        System.System_Board.MachineIO.ADSC = false;
                        System.System_Board.MachineIO.BDSC = false;
                        System.System_Board.MachineIO.LPR = true;
                        System.System_Board.MachineIO.MainUP = false;
                        System.System_Board.MachineIO.BUZZER = true;
                        System.System_Board.MachineIO.LPY = false;
                        System.System_Board.SendControl();
                        await Task.Delay(1000);
                        System.System_Board.MachineIO.ADSC = false;
                        System.System_Board.MachineIO.BDSC = false;
                        System.System_Board.MachineIO.MainUP = false;
                        System.System_Board.SendControl();
                        await Task.Delay(2000);
                        System.System_Board.MachineIO.BUZZER = false;
                        // NG: do NOT reset up - keep the cylinder down. Only a PASS raises it (MainUP=true).
                        System.System_Board.MachineIO.MainUP = false;
                        System.System_Board.SendControl();
                        foreach (var item in Boards)
                        {
                            item.Barcode = "";
                            item.Result = "";
                            item.Skip = item.UserSkip;
                        }
                        if (TestModel.BarcodeOption.UseBarcodeInput)
                        {
                            // Straight to READY if the operator already scanned ahead; otherwise WAIT for a scan.
                            // (WAIT re-runs this too, so a scan-ahead is never stranded whichever way we got there.)
                            TestState = LoadScannedAheadBarcodes() ? RunTestState.READY : RunTestState.WAIT;
                        }
                        else
                        {
                            TestState = RunTestState.READY;
                        }
                        break;

                    case RunTestState.READY:
                        if (IsTestting)
                        {
                            //TestModel.BarcodeOption.UseBarcodeInput
                            if (TestModel.BarcodeOption.UseBarcodeInput)
                            {
                                bool Realdy = true;
                                if (Boards.Count >= 1) if (!Boards[0].Skip) Realdy &= Boards[0].BarcodeReady;
                                if (Boards.Count >= 2) if (!Boards[1].Skip) Realdy &= Boards[1].BarcodeReady;
                                if (Boards.Count >= 3) if (!Boards[2].Skip) Realdy &= Boards[2].BarcodeReady;
                                if (Boards.Count >= 4) if (!Boards[3].Skip) Realdy &= Boards[3].BarcodeReady;
                                //Realdy
                                if (Realdy)
                                {
                                    TestState = RunTestState.TESTTING;
                                    System.System_Board.MachineIO.BUZZER = false;
                                    System.System_Board.MachineIO.LPY = true;
                                    // me SYSTEM.System_Board.MachineIO.MainDOWN = false;
                                    System.System_Board.SendControl();
                                }
                                else
                                {
                                    IsTestting = false;
                                    TestState = RunTestState.WAIT;
                                }
                            }
                            else
                            {
                                // no barcode input

                                TestState = RunTestState.TESTTING;
                                System.System_Board.MachineIO.BUZZER = false;
                                System.System_Board.MachineIO.LPY = true;
                                System.System_Board.SendControl();
                            }
                        }
                        else if (appSetting.Operations.UseRetryUpdown)
                        {
                            System.System_Board.MachineIO.BUZZER = false;
                            System.System_Board.MachineIO.MainUP = true;
                            System.System_Board.SendControl();
                            await Task.Delay(appSetting.Operations.TestPressUpTime);
                            System.System_Board.MachineIO.MainUP = false;
                            System.System_Board.SendControl();
                            await Task.Delay(appSetting.Operations.TestPressUpTime + 2000);
                        }
                        break;

                    default:
                        break;
                }
                // Re-sync of system-board inputs is now handled by the firmware itself (v2 protocol): it streams
                // input changes on-change and re-sends periodically (polling flag). No PC-side poll needed.
                await Task.Delay(500);
            }
        }

        private void EscapTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            EscapTime += 0.1;
        }

        // When true, MANUALTEST loops back to step 0 instead of stopping at end / END command.
        public bool ManualLoop { get; set; } = false;

        public void RUN_MANUAL_TEST()
        {
            if (!IsTestting)
            {
                TestState = RunTestState.MANUALTEST;
                IsTestting = true;
            }
        }

        public async void Run_Manual_Test()
        {
            if (TestModel.Steps.Count < 2)
            {
                return;
            }
            TestModel.CleanSteps();
            for (int i = 0; i < TestModel.Steps.Count; i++)
            {
                if (TestState == RunTestState.STOP)
                {
                    TestState = RunTestState.DONE;
                    break;
                }
                var stepTest = TestModel.Steps[i];
                if (stepTest != null)
                {
                    StepTesting = i;
                    if (stepTest.cmd != CMDs.NON || !stepTest.Skip)
                    {
                        StepTestChange?.Invoke(i, null);
                        RUN_FUNCTION_TEST(stepTest);
                        await Task.Delay(10); // delay for data binding
                    }
                }
                while (TestState == RunTestState.PAUSE)
                {
                    Task.Delay(100).Wait();
                }
            }
            TestState = RunTestState.DONE;
            TestRunFinish?.Invoke(null, null);
        }

        public void ResetTest()
        {
            TestState = RunTestState.WAIT;
            // No MainUP here: CLUP is a momentary push and only a PASS raises the jig.
            System.System_Board.SendControl();
            TestModel.CleanSteps();
            Relay.Card.Release();
            MuxCard.Card.ReleaseChannels();
            Solenoid.Card.Release();
        }

        public Step currentStep = new Step();

        public async void RunStep()
        {
            if (!FunctionTesting)
            {
                await Task.Run(RunFunctionsTest);
            }
        }

        private bool FunctionTesting = false;

        public async void RunFunctionsTest()
        {
            FunctionTesting = true;
            try
            {
                currentStep.ValueGet1 = "";
                currentStep.ValueGet2 = "";
                currentStep.ValueGet3 = "";
                currentStep.ValueGet4 = "";
                RUN_FUNCTION_TEST(currentStep);
            }
            catch (Exception err)
            {
                Debug.Write(string.Format("{0} : {1}", currentStep.TestContent, err.StackTrace), Debug.ContentType.Error);
            }
            await Task.Delay(10);
            FunctionTesting = false;
        }

        public bool RUN_FUNCTION_TEST(Step step)
        {
            if (step != null && !step.Skip && step.cmd != CMDs.NON && step.cmd != CMDs.END)
                Debug.Write("BEGIN STEP: " + step.No, Debug.ContentType.Log);

            step.Result1 = Step.DontCare;
            step.Result2 = Step.DontCare;
            step.Result3 = Step.DontCare;
            step.Result4 = Step.DontCare;

            step.ValueGet1 = "";
            step.ValueGet2 = "";
            step.ValueGet3 = "";
            step.ValueGet4 = "";

            bool isSkipAll = Boards.Where(x => x.Skip).Count() == Boards.Count;
            if (isSkipAll) return false;

            if (!step.Skip)

                switch (step.cmd)
                {
                    case CMDs.NON:
                        break;

                    case CMDs.PWR:
                        PWR(step);
                        break;

                    case CMDs.DLY:
                        DLY(step);
                        break;

                    case CMDs.GEN:
                        GEN(step);
                        break;

                    case CMDs.BUZ:
                        BUZ(step);
                        break;

                    case CMDs.RLY:
                        RLY_SYSTEM_BOARD(step);
                        //RLY_RELAY_BOARD(step);

                        Task.Delay(50).Wait();
                        break;

                    case CMDs.KEY:
                        KEY(step);
                        Task.Delay(50).Wait();
                        break;

                    case CMDs.MAK:
                        break;

                    case CMDs.DIS:
                        DIS(step);
                        break;

                    case CMDs.END:
                        END(step);
                        break;

                    case CMDs.ACV:
                        ACV(step);
                        break;

                    case CMDs.DCV:
                        DCV(step);
                        break;

                    case CMDs.FRQ:
                        FREQ(step);
                        break;

                    case CMDs.RES:
                        RES(step);
                        break;

                    case CMDs.URD:
                        //URD(step, PCB_SKIP_CHECK); update late
                        break;

                    case CMDs.UTN:
                        UTN(step);
                        break;

                    case CMDs.UTX:
                        UTX(step);
                        break;

                    case CMDs.UCN:
                        UCN(step);
                        break;

                    //case CMDs.UCP:
                    //    break;

                    case CMDs.STL:
                        STL(step);
                        break;

                    case CMDs.EDL:
                        EDL(step);
                        break;

                    case CMDs.LCC:
                        LCC(step);
                        break;

                    case CMDs.LEC:
                        LEC(step);
                        break;

                    case CMDs.LSQ:
                        LSQ(step);
                        break;

                    case CMDs.LTM:
                        LTM(step);
                        break;

                    case CMDs.CAL:
                        break;

                    case CMDs.GLED:
                        ReadGLED(step);
                        break;

                    case CMDs.FND:
                        ReadFND(step);
                        break;

                    case CMDs.LED:
                        ReadLED(step);
                        break;

                    case CMDs.LCD:
                        ReadLCD(step);
                        break;

                    case CMDs.PCB:
                        PCB(step);
                        break;

                    case CMDs.SEV:
                        SEV(step);
                        Task.Delay(1000).Wait();
                        break;

                    case CMDs.CAM:
                        CAM(step);
                        Task.Delay(1000).Wait();
                        break;

                    case CMDs.MOT:
                        MOT(step);
                        break;

                    case CMDs.SND:
                        SND(step);
                        break;

                    default:
                        break;
                }
            return StepTestResult(step);
        }

        #region Functions Code

        public void GEN(Step step)
        {
            if (!Double.TryParse(step.Condition1, out double frequency))
            {
                FunctionsParameterError("Condition", step);
                return;
            }

            List<string> Channels = step.Oper.Split('/').ToList();
            List<int> ChannelsInt = new List<int>();
            foreach (var Channel in Channels)
            {
                if (!Int32.TryParse(Channel, out int channel))
                {
                    FunctionsParameterError("Oper", step);
                    return;
                }
                else
                {
                    if (channel == 0 || channel > 4)
                    {
                        FunctionsParameterError("Oper", step);
                        return;
                    }
                    else
                    {
                        ChannelsInt.Add(channel);
                    }
                }
            }

            if (!System.System_Board.SerialPort.Port.IsOpen)
            {
                FunctionsParameterError("Sys", step);
                return;
            }

            if (!System.System_Board.GEN((int)frequency, ChannelsInt))
            {
                FunctionsParameterError("Sys", step);
            }
            else
            {
                if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = "exe";
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = "exe";
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = "exe";
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = "exe";
            }
        }

        public void BUZ(Step step)
        {
            if (Boards.Count > 2)
            {
                FunctionsParameterError("Site number", step);
                return;
            }
            if (!BoardExtension.SerialPort.Port.IsOpen)
            {
                FunctionsParameterError("Sys", step);
                return;
            }

            string function_type = step.Oper;

            if (!(function_type == "START" || function_type == "READ"))
            {
                FunctionsParameterError("Oper", step);
                return;
            }

            if (function_type == "START")
            {
                double sampling_rate;
                if (!Double.TryParse(step.Condition1, out sampling_rate))
                {
                    FunctionsParameterError("Condition1", step);
                    return;
                }

                System.StartRecordMic(sampling_rate);

                if (Boards.Count >= 1)
                {
                    step.ValueGet1 = "exe";
                    step.Result1 = Step.Ok;
                }
                if (Boards.Count >= 2)
                {
                    step.ValueGet1 = "exe";
                    step.Result2 = Step.Ok;
                }
            }
            else
            {
                System.StopRecordMic();

                double min;
                if (!Double.TryParse(step.Min, out min))
                {
                    if (step.Min == "")
                    {
                        min = 0;
                    }
                    else
                    {
                        FunctionsParameterError("Min", step);
                        return;
                    }
                }

                double max;
                if (!Double.TryParse(step.Max, out max))
                {
                    if (step.Max == "")
                    {
                        max = Double.MaxValue;
                    }
                    else
                    {
                        FunctionsParameterError("Max", step);
                        return;
                    }
                }

                if (Boards.Count >= 1)
                {
                    if (!Boards[0].Skip)
                    {
                        List<int> samples = System.System_Board.MachineIO.SamplesMicA;

                        step.ValueGet1 = samples.Max().ToString();
                        if (samples.Max() <= max & samples.Max() >= min)
                        {
                            step.Result1 = Step.Ok;
                        }
                        else
                        {
                            step.Result1 = Step.Ng;
                        }

                        return;
                    }
                }
                if (Boards.Count >= 2)
                {
                    if (!Boards[1].Skip)
                    {
                        List<int> samples = System.System_Board.MachineIO.SamplesMicB;

                        step.ValueGet2 = samples.Max().ToString();
                        if (samples.Max() <= max & samples.Max() >= min)
                        {
                            step.Result2 = Step.Ok;
                        }
                        else
                        {
                            step.Result2 = Step.Ng;
                        }
                        return;
                    }
                }
            }
        }

        public void SEV(Step step)
        {
            if (!BoardExtension.SerialPort.Port.IsOpen)
            {
                switch (Boards.Count)
                {
                    case 1:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }
                        break;

                    case 2:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Sys";
                            step.Result3 = Step.Ng;
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Sys";
                            step.Result3 = Step.Ng;
                        }
                        if (!Boards[3].Skip)
                        {
                            step.ValueGet4 = "Sys";
                            step.Result4 = Step.Ng;
                        }
                        break;

                    default:
                        break;
                }
                return;
            }

            bool result = false;
            string stringCompare = string.Empty;

            if (int.TryParse(step.Condition2, out int scanTime))
            {
                DateTime start = DateTime.Now;
                while (DateTime.Now.Subtract(start).TotalMilliseconds < scanTime)
                {
                    BoardExtension.SevenSegment.DigitalRead();
                    BoardExtension.SevenSegment.ParseDigit();

                    if (step.Condition1 == "String")
                    {
                        stringCompare = string.Empty;

                        #region Digit 0

                        StringBuilder binaryString_digit0 = new StringBuilder();
                        foreach (bool b in BoardExtension.SevenSegment.Digit0)
                        {
                            binaryString_digit0.Append(b ? "1" : "0");
                        }
                        SegementCharacter segchar_digit0 = FND.SEG_LOOKUP.Where(seg => seg.digitString == binaryString_digit0.ToString()).ToList().FirstOrDefault();
                        stringCompare = (segchar_digit0 == null ? "" : segchar_digit0.character.ToString()) + stringCompare;

                        #endregion Digit 0

                        #region Digit 1

                        StringBuilder binaryString_digit1 = new StringBuilder();
                        foreach (bool b in BoardExtension.SevenSegment.Digit1)
                        {
                            binaryString_digit1.Append(b ? "1" : "0");
                        }

                        SegementCharacter segchar_digit1 = FND.SEG_LOOKUP.Where(seg => seg.digitString == binaryString_digit1.ToString()).ToList().FirstOrDefault();
                        stringCompare = (segchar_digit1 == null ? "" : segchar_digit1.character.ToString()) + stringCompare;

                        #endregion Digit 1

                        #region colon, dot, [empty]

                        string Sign = string.Empty;
                        if (BoardExtension.SevenSegment.Sign.All(item => item == true))
                        {
                            Sign = ":";
                        }
                        else if (BoardExtension.SevenSegment.Sign.Any(item => item == true))
                        {
                            Sign = ".";
                        }
                        stringCompare = Sign + stringCompare;

                        #endregion colon, dot, [empty]

                        #region Digit 2

                        StringBuilder binaryString_digit2 = new StringBuilder();
                        foreach (bool b in BoardExtension.SevenSegment.Digit2)
                        {
                            binaryString_digit2.Append(b ? "1" : "0");
                        }
                        SegementCharacter segchar_digit2 = FND.SEG_LOOKUP.Where(seg => seg.digitString == binaryString_digit2.ToString()).ToList().FirstOrDefault();
                        stringCompare = (segchar_digit2 == null ? "" : segchar_digit2.character.ToString()) + stringCompare;

                        #endregion Digit 2

                        #region Digit 3

                        StringBuilder binaryString_digit3 = new StringBuilder();
                        foreach (bool b in BoardExtension.SevenSegment.Digit3)
                        {
                            binaryString_digit3.Append(b ? "1" : "0");
                        }

                        SegementCharacter segchar_digit3 = FND.SEG_LOOKUP.Where(seg => seg.digitString == binaryString_digit3.ToString()).ToList().FirstOrDefault();
                        stringCompare = (segchar_digit3 == null ? "" : segchar_digit3.character.ToString()) + stringCompare;

                        #endregion Digit 3

                        Console.WriteLine($"CDS string: {stringCompare}");

                        result = stringCompare.Equals(step.Spect);
                        step.ValueGet1 = stringCompare;

                        if (result)
                        {
                            break;
                        }

                        Task.Delay(50);
                    }
                    else
                    {
                        result = true;

                        List<String> indexsIconString = new List<string>();
                        if (step.Oper.Contains("/"))
                        {
                            indexsIconString = step.Oper.Split('/').ToList();
                        }
                        else
                        {
                            indexsIconString.Add(step.Oper);
                        }
                        List<int> indexsIcon = new List<int>();

                        try
                        {
                            foreach (var item in indexsIconString)
                            {
                                if (item.Contains('~'))
                                {
                                    int startIndex = Convert.ToInt32(item.Split('~')[0]);
                                    int endIndex = Convert.ToInt32(item.Split('~')[1]);
                                    for (int i = startIndex; i <= endIndex; i++)
                                    {
                                        indexsIcon.Add(i - 1);
                                    }
                                }
                                else
                                {
                                    indexsIcon.Add(Convert.ToInt32(item) - 1);
                                }
                            }

                            step.ValueGet1 = "OFF:";
                            foreach (int i in indexsIcon)
                            {
                                bool value_icon = BoardExtension.SevenSegment.Icons[i];

                                result &= value_icon;

                                if (!value_icon)
                                {
                                    step.ValueGet1 += " " + (i + 1).ToString();
                                }
                            }
                        }
                        catch
                        {
                            FunctionsParameterError("Oper", step);
                            return;
                        }

                        if (result)
                        {
                            break;
                        }
                        Task.Delay(50);
                    }
                }
            }
            else
            {
                FunctionsParameterError("Condition2", step);
                return;
            }

            if (result)
            {
                step.ValueGet1 = "OK";
                step.Result1 = Step.Ok;
            }
            else
            {
                step.Result1 = Step.Ng;
            }

            return;
        }

        public void CAM(Step step)
        {
            if (Capture == null)
            {
                FunctionsParameterError("no cam", step);
                return;
            }

            Controls.DeviceControl.Camera.CameraControl.VideoProperties properties;

            if (Enum.TryParse<Controls.DeviceControl.Camera.CameraControl.VideoProperties>(step.Condition1, out properties))
            {
                if (properties == Controls.DeviceControl.Camera.CameraControl.VideoProperties.Reset)
                {
                    Capture?.SetParammeter(TestModel.CameraSetting);
                    return;
                }

                int value = 0;

                if (Int32.TryParse(step.Oper, out value))
                {
                    Capture?.SetParammeter(properties, value, true);
                }
                else
                {                                                                                                                                                                                                                                               
                    FunctionsParameterError("Oper", step);
                }
            }
            else
            {
                FunctionsParameterError("condition", step);
                return;
            }

            if (step.ValueGet1 == "condition" || step.ValueGet1 == "Oper")
            {
                step.ValueGet1 = "OFF";
                step.Result1 = Step.Ng;
            }
            else
            {
                step.ValueGet1 = "ON";
                step.Result1 = Step.Ok;
            }
        }

        public void UTN(Step step)
        {
            TxData txData = new TxData();
            txData = TestModel.Naming.TxDatas.Where(x => x.Name == step.Condition1).DefaultIfEmpty(null).FirstOrDefault();
            if (txData == null)
            {
                FunctionsParameterError("Naming", step);
                return;
            }
            foreach (var item in UUTs)
            {
                if (step.Oper == "P1" && item.Config != TestModel.P1_Config)
                {
                    item.Config = TestModel.P1_Config;
                }
                else if (step.Oper == "P2")
                {
                    item.Config = TestModel.P2_Config;
                }
            }

            var startTime = DateTime.Now;
            Int32 delay = 10;
            Int32 limittime = 10;
            int tryCount = 1;
            Int32.TryParse(step.Count, out delay);
            Int32.TryParse(step.Condition2, out limittime);
            Int32.TryParse(step.Min, out tryCount);

            switch (step.Mode)
            {
                case "NORMAL":
                    UTN_NORMAL(step, txData);
                    break;

                case "SEND-R":
                    var listTask1 = new List<Task<bool>>();

                    if (Boards.Count >= 1) if (!Boards[0].Skip) listTask1.Add(UTN_SEND_R(step, UUTs[0], 1, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 2) if (!Boards[1].Skip) listTask1.Add(UTN_SEND_R(step, UUTs[1], 2, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 3) if (!Boards[2].Skip) listTask1.Add(UTN_SEND_R(step, UUTs[2], 3, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 4) if (!Boards[3].Skip) listTask1.Add(UTN_SEND_R(step, UUTs[3], 4, txData, delay, limittime, tryCount));

                    try
                    {
                        // Wait for all the tasks to finish.
                        Task.WaitAll(listTask1.ToArray());

                        // We should never get to this point
                        Console.WriteLine("WaitAll() has not thrown exceptions. THIS WAS NOT EXPECTED.");
                    }
                    catch (AggregateException e)
                    {
                        Console.WriteLine("\nThe following exceptions have been thrown by WaitAll(): (THIS WAS EXPECTED)");
                        for (int j = 0; j < e.InnerExceptions.Count; j++)
                        {
                            Console.WriteLine("\n-------------------------------------------------\n{0}", e.InnerExceptions[j].ToString());
                        }
                    }
                    break;

                case "SEND_R":
                    var listTask2 = new List<Task<bool>>();

                    if (Boards.Count >= 1) if (!Boards[0].Skip) listTask2.Add(UTN_SEND_R(step, UUTs[0], 1, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 2) if (!Boards[1].Skip) listTask2.Add(UTN_SEND_R(step, UUTs[1], 2, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 3) if (!Boards[2].Skip) listTask2.Add(UTN_SEND_R(step, UUTs[2], 3, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 4) if (!Boards[3].Skip) listTask2.Add(UTN_SEND_R(step, UUTs[3], 4, txData, delay, limittime, tryCount));

                    try
                    {
                        // Wait for all the tasks to finish.
                        Task.WaitAll(listTask2.ToArray());

                        // We should never get to this point
                        Console.WriteLine("WaitAll() has not thrown exceptions. THIS WAS NOT EXPECTED.");
                    }
                    catch (AggregateException e)
                    {
                        Console.WriteLine("\nThe following exceptions have been thrown by WaitAll(): (THIS WAS EXPECTED)");
                        for (int j = 0; j < e.InnerExceptions.Count; j++)
                        {
                            Console.WriteLine("\n-------------------------------------------------\n{0}", e.InnerExceptions[j].ToString());
                        }
                    }
                    break;

                case "TIMER":
                    UTN_SendTimer(step, txData);
                    break;

                default:
                    break;
            }
        }

        public void UTX(Step step)
        {
            string txData = step.Condition1;
            if (txData == null || txData.Length < 2)
            {
                FunctionsParameterError("Condition", step);
                return;
            }
            foreach (var item in UUTs)
            {
                if (step.Oper == "P1" && item.Config != TestModel.P1_Config)
                {
                    item.Config = TestModel.P1_Config;
                }
                else if (step.Oper == "P2")
                {
                    item.Config = TestModel.P2_Config;
                }
            }

            var startTime = DateTime.Now;
            Int32 delay = 10;
            Int32 limittime = 10;
            int tryCount = 1;
            Int32.TryParse(step.Count, out delay);
            Int32.TryParse(step.Condition2, out limittime);
            Int32.TryParse(step.Min, out tryCount);

            switch (step.Mode)
            {
                case "NORMAL":
                    UTN_NORMAL(step, txData);
                    break;

                case "SEND-R":
                    var listTask1 = new List<Task<bool>>();

                    if (Boards.Count >= 1) if (!Boards[0].Skip) listTask1.Add(UTN_SEND_R(step, UUTs[0], 1, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 2) if (!Boards[1].Skip) listTask1.Add(UTN_SEND_R(step, UUTs[1], 2, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 3) if (!Boards[2].Skip) listTask1.Add(UTN_SEND_R(step, UUTs[2], 3, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 4) if (!Boards[3].Skip) listTask1.Add(UTN_SEND_R(step, UUTs[3], 4, txData, delay, limittime, tryCount));

                    try
                    {
                        // Wait for all the tasks to finish.
                        Task.WaitAll(listTask1.ToArray());

                        // We should never get to this point
                        Console.WriteLine("WaitAll() has not thrown exceptions. THIS WAS NOT EXPECTED.");
                    }
                    catch (AggregateException e)
                    {
                        Console.WriteLine("\nThe following exceptions have been thrown by WaitAll(): (THIS WAS EXPECTED)");
                        for (int j = 0; j < e.InnerExceptions.Count; j++)
                        {
                            Console.WriteLine("\n-------------------------------------------------\n{0}", e.InnerExceptions[j].ToString());
                        }
                    }
                    break;

                case "SEND_R":
                    var listTask2 = new List<Task<bool>>();

                    if (Boards.Count >= 1) if (!Boards[0].Skip) listTask2.Add(UTN_SEND_R(step, UUTs[0], 1, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 2) if (!Boards[1].Skip) listTask2.Add(UTN_SEND_R(step, UUTs[1], 2, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 3) if (!Boards[2].Skip) listTask2.Add(UTN_SEND_R(step, UUTs[2], 3, txData, delay, limittime, tryCount));
                    if (Boards.Count >= 4) if (!Boards[3].Skip) listTask2.Add(UTN_SEND_R(step, UUTs[3], 4, txData, delay, limittime, tryCount));

                    try
                    {
                        // Wait for all the tasks to finish.
                        Task.WaitAll(listTask2.ToArray());

                        // We should never get to this point
                        Console.WriteLine("WaitAll() has not thrown exceptions. THIS WAS NOT EXPECTED.");
                    }
                    catch (AggregateException e)
                    {
                        Console.WriteLine("\nThe following exceptions have been thrown by WaitAll(): (THIS WAS EXPECTED)");
                        for (int j = 0; j < e.InnerExceptions.Count; j++)
                        {
                            Console.WriteLine("\n-------------------------------------------------\n{0}", e.InnerExceptions[j].ToString());
                        }
                    }
                    break;

                case "TIMER":
                    UTN_SendTimer(step, txData);
                    break;

                default:
                    break;
            }
        }

        private void UTN_NORMAL(Step step, TxData txData)
        {
            if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result1 = UUTs[0].Send(txData) ? Step.Ok : Step.Ng;
            if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result2 = UUTs[1].Send(txData) ? Step.Ok : Step.Ng;
            if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result3 = UUTs[2].Send(txData) ? Step.Ok : Step.Ng;
            if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result4 = UUTs[3].Send(txData) ? Step.Ok : Step.Ng;

            if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = step.Result1 == Step.Ok ? Step.Ok : "Tx";
            if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = step.Result2 == Step.Ok ? Step.Ok : "Tx";
            if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = step.Result3 == Step.Ok ? Step.Ok : "Tx";
            if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = step.Result4 == Step.Ok ? Step.Ok : "Tx";
        }

        private void UTN_NORMAL(Step step, string txData)
        {
            if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result1 = UUTs[0].Send(txData) ? Step.Ok : Step.Ng;
            if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result2 = UUTs[1].Send(txData) ? Step.Ok : Step.Ng;
            if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result3 = UUTs[2].Send(txData) ? Step.Ok : Step.Ng;
            if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result4 = UUTs[3].Send(txData) ? Step.Ok : Step.Ng;

            if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = step.Result1 == Step.Ok ? Step.Ok : "Tx";
            if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = step.Result2 == Step.Ok ? Step.Ok : "Tx";
            if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = step.Result3 == Step.Ok ? Step.Ok : "Tx";
            if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = step.Result4 == Step.Ok ? Step.Ok : "Tx";
        }

        private void UTN_SendTimer(Step step, TxData txData)
        {
            if (int.TryParse(step.Count, out int time))
            {
                if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = UUTs[0].SendTimer(txData, time) ? Step.Ok : "Sys";
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = UUTs[1].SendTimer(txData, time) ? Step.Ok : "Sys";
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = UUTs[2].SendTimer(txData, time) ? Step.Ok : "Sys";
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = UUTs[3].SendTimer(txData, time) ? Step.Ok : "Sys";
            }
            else
            {
                if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = "Set time";
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = "Set time";
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = "Set time";
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = "Set time";
            }
        }

        private void UTN_SendTimer(Step step, string txData)
        {
            if (int.TryParse(step.Count, out int time))
            {
                if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = UUTs[0].SendTimer(txData, time) ? Step.Ok : "Sys";
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = UUTs[1].SendTimer(txData, time) ? Step.Ok : "Sys";
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = UUTs[2].SendTimer(txData, time) ? Step.Ok : "Sys";
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = UUTs[3].SendTimer(txData, time) ? Step.Ok : "Sys";
            }
            else
            {
                if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = "Set time";
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = "Set time";
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = "Set time";
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = "Set time";
            }
        }

        private async Task<bool> UTN_SEND_R(Step step, UUTPort UUT, int boardIndex, TxData txData, int DelayTime, int limitTime, int tryCount)
        {
            var start = DateTime.Now;
            for (int i = 0; i < tryCount; i++)
            {
                if (DateTime.Now.Subtract(start).TotalMilliseconds > limitTime)
                {
                    if (UUT.HaveBuffer())
                    {
                        SetValue(step, boardIndex, "OK");
                        return true;
                    }
                    else
                    {
                        SetValue(step, boardIndex, "Timeout", true);
                        return false;
                    }
                }
                else
                {
                    var sendOK = UUT.Send(txData);
                    if (!sendOK)
                    {
                        SetValue(step, boardIndex, "Tx", true);
                    }
                    await Task.Delay(DelayTime);
                    if (UUT.HaveBuffer())
                    {
                        SetValue(step, boardIndex, "OK");
                        return true;
                    }
                    else
                    {
                        SetValue(step, boardIndex, "Rx", true);
                    }
                }
            }
            return false;
        }

        private async Task<bool> UTN_SEND_R(Step step, UUTPort UUT, int boardIndex, string txData, int DelayTime, int limitTime, int tryCount)
        {
            var start = DateTime.Now;
            while (true)
            {
                var sendOK = UUT.Send(txData);
                if (sendOK)
                {
                    SetValue(step, boardIndex, "OK");
                }
                else
                {
                    SetValue(step, boardIndex, "Tx", true);
                }

                await Task.Delay(DelayTime);

                if (UUT.HaveBuffer())
                {
                    SetValue(step, boardIndex, "OK");
                    return true;
                }
                else
                {
                    SetValue(step, boardIndex, "Rx", true);
                    if (DateTime.Now.Subtract(start).TotalMilliseconds < limitTime)
                        return false;
                }
            }
        }

        private void SetValue(Step step, int Index, string value, bool IsFail = false)
        {
            switch (Index)
            {
                case 1:
                    if (Boards.Count > 0)
                    {
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = value;
                            if (IsFail)
                            {
                                step.Result1 = Step.Ng;
                            }
                            else
                            {
                                step.Result1 = Step.DontCare;
                            }
                        }
                    }
                    break;

                case 2:
                    if (Boards.Count > 1)
                    {
                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = value;
                            if (IsFail)
                            {
                                step.Result2 = Step.Ng;
                            }
                            else
                            {
                                step.Result2 = Step.DontCare;
                            }
                        }
                    }
                    break;

                case 3:
                    if (Boards.Count > 2)
                    {
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = value;
                            if (IsFail)
                            {
                                step.Result3 = Step.Ng;
                            }
                            else
                            {
                                step.Result3 = Step.DontCare;
                            }
                        }
                    }
                    break;

                case 4:
                    if (Boards.Count > 3)
                    {
                        if (!Boards[3].Skip)
                        {
                            step.ValueGet4 = value;
                            if (IsFail)
                            {
                                step.Result4 = Step.Ng;
                            }
                            else
                            {
                                step.Result4 = Step.DontCare;
                            }
                        }
                    }
                    break;

                default:
                    break;
            }
        }

        public void UCN(Step step)
        {
            RxData rxData = new RxData();
            rxData = TestModel.Naming.RxDatas.Where(x => x.Name == step.Condition1).DefaultIfEmpty(null).FirstOrDefault();

            if (rxData != null)
            {
                if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = UUTs[0].CheckBufferString(rxData);
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = UUTs[1].CheckBufferString(rxData);
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = UUTs[2].CheckBufferString(rxData);
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = UUTs[3].CheckBufferString(rxData);

                if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result1 = step.ValueGet1 == step.Spect ? Step.Ok : Step.Ng;
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result2 = step.ValueGet2 == step.Spect ? Step.Ok : Step.Ng;
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result3 = step.ValueGet3 == step.Spect ? Step.Ok : Step.Ng;
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result4 = step.ValueGet4 == step.Spect ? Step.Ok : Step.Ng;
            }
            else
            {
                if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result1 = Step.Ng;
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result2 = Step.Ng;
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result3 = Step.Ng;
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result4 = Step.Ng;
            }
        }

        public void ReadLCD(Step step)
        {

            step.Result = true;
            if (int.TryParse(step.Condition2, out int scanTime))
            {
                DateTime start = DateTime.Now;
                while (DateTime.Now.Subtract(start).TotalMilliseconds < scanTime)
                {

                    step.ValueGet1 = VisionTester.Models.LCDs[0].DetectedString;


                    step.Result1 = VisionTester.Models.LCDs[0].DetectedString == step.Oper ? Step.Ok : Step.Ng;


                    step.Result = true;

                    if (!Boards[0].Skip)
                    {
                        step.Result &= (step.Result1 == Step.Ok);
                    }



                    if (step.Result)
                        break;
                    Task.Delay(10).Wait();
                }
            }
            else
            {
                if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = VisionTester.Models.LCDs[0].DetectedString;


                if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result1 = step.ValueGet1 == step.Oper ? Step.Ok : Step.Ng;


                if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result &= (step.Result1 == Step.Ok);

            }
        }

        public void ReadFND(Step step)
        {
            //step.Result = true;

            //var random1 = new Random();
            //var random2 = new Random();
            //var random3 = new Random();
            //var random4 = new Random();

            //step.Result1 = random1.Next(10) < 4 ? Step.Ok : Step.Ng;
            //step.Result2 = random2.Next(10) < 4 ? Step.Ok : Step.Ng;
            //step.Result3 = random3.Next(10) < 4 ? Step.Ok : Step.Ng;
            //step.Result4 = random4.Next(10) < 4 ? Step.Ok : Step.Ng;

            //if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result &= (step.Result1 == Step.Ok);
            //if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result &= (step.Result2 == Step.Ok);
            //if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result &= (step.Result3 == Step.Ok);
            //if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result &= (step.Result4 == Step.Ok);

            //return;

            if (int.TryParse(step.Condition2, out int scanTime))
            {
                DateTime start = DateTime.Now;
                while (DateTime.Now.Subtract(start).TotalMilliseconds < scanTime)
                {
                    step.Result = true;

                    string DetectedString_Board0 = string.Empty;
                    string DetectedString_Board1 = string.Empty;
                    string DetectedString_Board2 = string.Empty;
                    string DetectedString_Board3 = string.Empty;

                    foreach (var fnds_char in VisionTester.Models.FNDs)
                    {
                        DetectedString_Board0 += fnds_char[0].DetectedString;
                        DetectedString_Board1 += fnds_char[1].DetectedString;
                        DetectedString_Board2 += fnds_char[2].DetectedString;
                        DetectedString_Board3 += fnds_char[3].DetectedString;
                    }

                    if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = step.Result1 != Step.Ok ? DetectedString_Board0 : step.ValueGet1;
                    if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = step.Result2 != Step.Ok ? DetectedString_Board1 : step.ValueGet2;
                    if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = step.Result3 != Step.Ok ? DetectedString_Board2 : step.ValueGet3;
                    if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = step.Result4 != Step.Ok ? DetectedString_Board3 : step.ValueGet4;

                    if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result1 = step.ValueGet1 == step.Oper ? Step.Ok : Step.Ng;
                    if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result2 = step.ValueGet2 == step.Oper ? Step.Ok : Step.Ng;
                    if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result3 = step.ValueGet3 == step.Oper ? Step.Ok : Step.Ng;
                    if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result4 = step.ValueGet4 == step.Oper ? Step.Ok : Step.Ng;

                    if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result &= (step.Result1 == Step.Ok);
                    if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result &= (step.Result2 == Step.Ok);
                    if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result &= (step.Result3 == Step.Ok);
                    if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result &= (step.Result4 == Step.Ok);
                    if (step.Result)
                        break;
                    Task.Delay(200).Wait();
                }
            }
            else
            {
                Console.WriteLine();

                string DetectedString_Board0 = string.Empty;
                string DetectedString_Board1 = string.Empty;
                string DetectedString_Board2 = string.Empty;
                string DetectedString_Board3 = string.Empty;

                foreach (var fnds_char in VisionTester.Models.FNDs)
                {
                    DetectedString_Board0 += fnds_char[0].DetectedString;
                    DetectedString_Board1 += fnds_char[1].DetectedString;
                    DetectedString_Board2 += fnds_char[2].DetectedString;
                    DetectedString_Board3 += fnds_char[3].DetectedString;
                }

                if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = DetectedString_Board0;
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = DetectedString_Board1;
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = DetectedString_Board2;
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = DetectedString_Board3;

                if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result1 = step.ValueGet1 == step.Oper ? Step.Ok : Step.Ng;
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result2 = step.ValueGet2 == step.Oper ? Step.Ok : Step.Ng;
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result3 = step.ValueGet3 == step.Oper ? Step.Ok : Step.Ng;
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result4 = step.ValueGet4 == step.Oper ? Step.Ok : Step.Ng;

                if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result &= (step.Result1 == Step.Ok);
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result &= (step.Result2 == Step.Ok);
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result &= (step.Result3 == Step.Ok);
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result &= (step.Result4 == Step.Ok);
            }
        }

        public void ReadLED(Step step)
        {
            //VisionTester.Dispatcher.Invoke(new Action(() =>
            //{
            //    var ledList = VisionTester.Models.LED;
            //    ledList[0].LEDs = currentStep.LedList;

            //    foreach (var led in ledList[0].LEDs)
            //    {
            //        led.SetPosition();
            //    }
            //    VisionTester.LedFunctionUpdate();

            //    var lastFrameToTest = Capture.LastMatFrame;
            //    if (lastFrameToTest == null)
            //    {
            //        return;
            //    }
            //    VisionTester.Models.GetLEDSampleImage(lastFrameToTest);
            //}));

            if (int.TryParse(step.Condition2, out int scanTime))
            {
                step.Result = true;
                DateTime start = DateTime.Now;
                while (DateTime.Now.Subtract(start).TotalMilliseconds < scanTime)
                {
                    step.Result = true;
                    if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = VisionTester.Models.LED[0].CalculatorOutputString;
                    if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = VisionTester.Models.LED[1].CalculatorOutputString;
                    if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = VisionTester.Models.LED[2].CalculatorOutputString;
                    if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = VisionTester.Models.LED[3].CalculatorOutputString;

                    if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result1 = step.ValueGet1 == step.Oper ? Step.Ok : Step.Ng;
                    if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result2 = step.ValueGet2 == step.Oper ? Step.Ok : Step.Ng;
                    if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result3 = step.ValueGet3 == step.Oper ? Step.Ok : Step.Ng;
                    if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result4 = step.ValueGet4 == step.Oper ? Step.Ok : Step.Ng;

                    if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result &= (step.Result1 == Step.Ok);
                    if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result &= (step.Result2 == Step.Ok);
                    if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result &= (step.Result3 == Step.Ok);
                    if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result &= (step.Result4 == Step.Ok);

                    if (step.Result)
                        break;
                    Task.Delay(300).Wait();
                }
            }
            else
            {
                step.Result = true;
                if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = VisionTester.Models.LED[0].CalculatorOutputString;
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = VisionTester.Models.LED[1].CalculatorOutputString;
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = VisionTester.Models.LED[2].CalculatorOutputString;
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = VisionTester.Models.LED[3].CalculatorOutputString;

                if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result1 = step.ValueGet1 == step.Oper ? Step.Ok : Step.Ng;
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result2 = step.ValueGet2 == step.Oper ? Step.Ok : Step.Ng;
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result3 = step.ValueGet3 == step.Oper ? Step.Ok : Step.Ng;
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result4 = step.ValueGet4 == step.Oper ? Step.Ok : Step.Ng;

                if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result &= (step.Result1 == Step.Ok);
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result &= (step.Result2 == Step.Ok);
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result &= (step.Result3 == Step.Ok);
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result &= (step.Result4 == Step.Ok);
            }


            if (step.Result1 == string.Empty)
            {
                step.ValueGet1 = "";
                step.Result1 = Step.Ng;
            }

        }

        public void ReadGLED(Step step)
        {
            if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = VisionTester.Models.GLED[0].CalculatorOutputString;
            if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = VisionTester.Models.GLED[1].CalculatorOutputString;
            if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = VisionTester.Models.GLED[2].CalculatorOutputString;
            if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = VisionTester.Models.GLED[3].CalculatorOutputString;

            if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result1 = step.ValueGet1 == step.Oper ? Step.Ok : Step.Ng;
            if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result2 = step.ValueGet2 == step.Oper ? Step.Ok : Step.Ng;
            if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result3 = step.ValueGet3 == step.Oper ? Step.Ok : Step.Ng;
            if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result4 = step.ValueGet4 == step.Oper ? Step.Ok : Step.Ng;

            if (Boards.Count >= 1) if (!Boards[0].Skip) step.Result &= (step.Result1 == Step.Ok);
            if (Boards.Count >= 2) if (!Boards[1].Skip) step.Result &= (step.Result2 == Step.Ok);
            if (Boards.Count >= 3) if (!Boards[2].Skip) step.Result &= (step.Result3 == Step.Ok);
            if (Boards.Count >= 4) if (!Boards[3].Skip) step.Result &= (step.Result4 == Step.Ok);
        }

        public void RLY_SYSTEM_BOARD(Step step)
        {
            System.System_Board.DoorLockControl(step.Oper == "ON");

            if (Int32.TryParse(step.Condition2, out int pressDelayTime))
            {
                if (pressDelayTime > 0)
                {
                    System.System_Board.DoorLockControl(false);
                }
            }

            if (Int32.TryParse(step.Count, out int delayTime))
            {
                Task.Delay(delayTime).Wait();
            }

            step.ValueGet1 = "exe";
            step.Result1 = Step.Ok;
        }

        public void RLY_RELAY_BOARD(Step step)
        {
            List<String> Channel = new List<string>();
            if (step.Condition1.Contains("/"))
            {
                Channel = step.Condition1.Split('/').ToList();
            }
            else
            {
                Channel.Add(step.Condition1);
            }
            List<int> numberChannel = new List<int>();
            foreach (var item in Channel)
            {
                if (item.Contains('~'))
                {
                    int startChannel = Convert.ToInt32(item.Split('~')[0]);
                    int endChannel = Convert.ToInt32(item.Split('~')[1]);
                    for (int i = startChannel; i <= endChannel; i++)
                    {
                        numberChannel.Add(i - 1);
                    }
                }
                else
                {
                    numberChannel.Add(Convert.ToInt32(item) - 1);
                }
            }

            bool SetOK = Relay.SetChannels(numberChannel, step.Oper == "ON");
            switch (Boards.Count)
            {
                case 1:
                    if (!Boards[0].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet1 = "exe";
                            step.Result1 = Step.Ok;
                        }
                    }
                    break;

                case 2:
                    if (!Boards[0].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet1 = "exe";
                            step.Result1 = Step.Ok;
                        }
                    }

                    if (!Boards[1].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet2 = "exe";
                            step.Result2 = Step.Ok;
                        }
                    }
                    break;

                case 3:
                    if (!Boards[0].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet1 = "exe";
                            step.Result1 = Step.Ok;
                        }
                    }

                    if (!Boards[1].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet2 = "exe";
                            step.Result2 = Step.Ok;
                        }
                    }
                    if (!Boards[2].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet3 = "Sys";
                            step.Result3 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet3 = "exe";
                            step.Result3 = Step.Ok;
                        }
                    }
                    break;

                case 4:
                    if (!Boards[0].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet1 = "exe";
                            step.Result1 = Step.Ok;
                        }
                    }

                    if (!Boards[1].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet2 = "exe";
                            step.Result2 = Step.Ok;
                        }
                    }
                    if (!Boards[2].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet3 = "Sys";
                            step.Result3 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet3 = "exe";
                            step.Result3 = Step.Ok;
                        }
                    }
                    if (!Boards[3].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet4 = "Sys";
                            step.Result4 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet4 = "exe";
                            step.Result4 = Step.Ok;
                        }
                    }
                    break;

                default:
                    break;
            }
            if (Int32.TryParse(step.Condition2, out int pressDelayTime))
            {
                if (pressDelayTime > 0)
                {
                    Task.Delay(pressDelayTime).Wait();
                    SetOK = Relay.SetChannels(numberChannel, false);
                    if (!SetOK)
                    {
                        FunctionsParameterError("Sys", step);
                    }
                }
            }

            if (Int32.TryParse(step.Count, out int delayTime))
            {
                Task.Delay(delayTime).Wait();
            }
        }

        // Fires when a CMDs.SND step starts. SoundPage (diagnostic mode) uses it to feed config into the draft.
        public event EventHandler<Step> SoundStepStarted;

        // Sound processing command. ROIs are global on the model (TestModel.SoundConfig).
        // step.Oper = START / STOP / CHECK ; for CHECK, step.Condition2 lists the ROI indices to verify
        // (1-based, e.g. "1/3/5" or "1~3"). Empty Condition2 = check all global ROIs.
        public void SND(Step step)
        {
            SoundStepStarted?.Invoke(this, step);
            var mode = (step.Oper ?? "").Trim().ToUpperInvariant();
            var cfg = TestModel?.SoundConfig;
            int roiCount = cfg?.Rois?.Count ?? 0;
            DiagLog.Write("SND", $"Mode={mode} FFT={cfg?.FftSize} globalROIs={roiCount} Cond2={step.Condition2}");

            try
            {
                switch (mode)
                {
                    case "START":
                        if (SoundTester != null)
                        {
                            SoundTester.MicrophoneId = appSetting?.Communication?.MicrophoneId ?? "";
                            SoundTester.Start();
                        }
                        SetSoundStepPass(step);
                        break;

                    case "STOP":
                        SoundTester?.Stop();
                        SetSoundStepPass(step);
                        break;

                    case "CHECK":
                    default:
                        {
                            // Condition2 picks which global ROIs to verify (empty = all). See SelectRoisByCondition.
                            var subset = SelectRoisByCondition(cfg, step.Condition2);
                            bool pass = SoundTester != null && SoundTester.Check(cfg, subset);
                            SetSoundStepResult(step, pass);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                DiagLog.Write("SND", "Exception: " + ex.Message);
                SetSoundStepResult(step, false);
            }

            if (Int32.TryParse(step.Count, out int delayTime))
            {
                Task.Delay(delayTime).Wait();
            }
        }

        // Parse a Condition2 ROI-index string ("1/3/5" or "1~3", 1-based) into the matching global ROIs.
        // Empty/whitespace -> all ROIs.
        public static System.Collections.Generic.List<VTMBase.SoundRoi> SelectRoisByCondition(VTMBase.SoundStepConfig cfg, string condition2)
        {
            var all = cfg?.Rois;
            if (all == null || all.Count == 0) return new System.Collections.Generic.List<VTMBase.SoundRoi>();
            if (string.IsNullOrWhiteSpace(condition2)) return new System.Collections.Generic.List<VTMBase.SoundRoi>(all);

            var picked = new System.Collections.Generic.List<VTMBase.SoundRoi>();
            foreach (var part in condition2.Split('/'))
            {
                var token = part.Trim();
                if (token.Length == 0) continue;
                if (token.Contains("~"))
                {
                    var se = token.Split('~');
                    if (se.Length == 2 && int.TryParse(se[0], out int a) && int.TryParse(se[1], out int b))
                    {
                        if (a > b) { int t = a; a = b; b = t; }
                        for (int idx = a; idx <= b; idx++)
                            if (idx >= 1 && idx <= all.Count && !picked.Contains(all[idx - 1])) picked.Add(all[idx - 1]);
                    }
                }
                else if (int.TryParse(token, out int one))
                {
                    if (one >= 1 && one <= all.Count && !picked.Contains(all[one - 1])) picked.Add(all[one - 1]);
                }
            }
            return picked;
        }

        private void SetSoundStepPass(Step step)
        {
            for (int i = 0; i < Boards.Count && i < 4; i++)
            {
                if (Boards[i].Skip) continue;
                if (i == 0) { step.ValueGet1 = "OK"; step.Result1 = Step.Ok; }
                if (i == 1) { step.ValueGet2 = "OK"; step.Result2 = Step.Ok; }
                if (i == 2) { step.ValueGet3 = "OK"; step.Result3 = Step.Ok; }
                if (i == 3) { step.ValueGet4 = "OK"; step.Result4 = Step.Ok; }
            }
        }

        private void SetSoundStepResult(Step step, bool pass)
        {
            var v = pass ? "PASS" : "FAIL";
            var r = pass ? Step.Ok : Step.Ng;
            for (int i = 0; i < Boards.Count && i < 4; i++)
            {
                if (Boards[i].Skip) continue;
                if (i == 0) { step.ValueGet1 = v; step.Result1 = r; }
                if (i == 1) { step.ValueGet2 = v; step.Result2 = r; }
                if (i == 2) { step.ValueGet3 = v; step.Result3 = r; }
                if (i == 3) { step.ValueGet4 = v; step.Result4 = r; }
            }
        }

        public void KEY(Step step)
        {
            List<String> Channel = new List<string>();
            if (step.Condition1 == null)
            {
                FunctionsParameterError("Condition 1", step);
                return;
            }
            if (step.Condition1.Contains("/"))
            {
                Channel = step.Condition1.Split('/').ToList();
            }
            else
            {
                Channel.Add(step.Condition1);
            }
            List<int> numberChannel = new List<int>();
            foreach (var item in Channel)
            {
                if (item.Contains('~'))
                {
                    if (!int.TryParse(item.Split('~')[0], out int startChannel))
                    {
                        FunctionsParameterError("Condition start", step);
                        return;
                    }
                    if (!int.TryParse(item.Split('~')[1], out int endChannel))
                    {
                        FunctionsParameterError("Condition end", step);
                        return;
                    }
                    for (int i = startChannel; i <= endChannel; i++)
                    {
                        numberChannel.Add(i);
                    }
                }
                else
                {
                    if (int.TryParse(item, out int channelNumber))
                    {
                        numberChannel.Add(Convert.ToInt32(channelNumber));
                    }
                    else
                    {
                        FunctionsParameterError("Condition format", step);
                        return;
                    }
                }
            }

            DiagLog.Write("KEY", $"Ch=[{string.Join(",", numberChannel)}] Oper={step.Oper} Cond2={step.Condition2}");
            bool SetOK = Solenoid.SetChannels2(numberChannel, step.Oper == "ON");
            DiagLog.Write("KEY", $"SetChannels2 result={SetOK}");
            switch (Boards.Count)
            {
                case 1:
                    if (!Boards[0].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet1 = "ON";
                            step.Result1 = Step.Ok;
                        }
                    }
                    break;

                case 2:
                    if (!Boards[0].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet1 = "ON";
                            step.Result1 = Step.Ok;
                        }
                    }

                    if (!Boards[1].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet2 = "ON";
                            step.Result2 = Step.Ok;
                        }
                    }
                    break;

                case 3:
                    if (!Boards[0].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet1 = "ON";
                            step.Result1 = Step.Ok;
                        }
                    }

                    if (!Boards[1].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet2 = "ON";
                            step.Result2 = Step.Ok;
                        }
                    }
                    if (!Boards[2].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet3 = "Sys";
                            step.Result3 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet3 = "ON";
                            step.Result3 = Step.Ok;
                        }
                    }
                    break;

                case 4:
                    if (!Boards[0].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet1 = "ON";
                            step.Result1 = Step.Ok;
                        }
                    }

                    if (!Boards[1].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet2 = "ON";
                            step.Result2 = Step.Ok;
                        }
                    }
                    if (!Boards[2].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet3 = "Sys";
                            step.Result3 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet3 = "ON";
                            step.Result3 = Step.Ok;
                        }
                    }
                    if (!Boards[3].Skip)
                    {
                        if (!SetOK)
                        {
                            step.ValueGet4 = "Sys";
                            step.Result4 = Step.Ng;
                        }
                        else
                        {
                            step.ValueGet4 = "ON";
                            step.Result4 = Step.Ok;
                        }
                    }
                    break;

                default:
                    break;
            }
            if (Int32.TryParse(step.Condition2, out int pressDelayTime))
            {
                if (pressDelayTime > 0)
                {
                    Task.Delay(pressDelayTime).Wait();
                    DiagLog.Write("KEY", $"Cond2 auto-off after {pressDelayTime}ms Ch=[{string.Join(",", numberChannel)}]");
                    SetOK = Solenoid.SetChannels2(numberChannel, false);
                    DiagLog.Write("KEY", $"Cond2 auto-off result={SetOK}");
                    if (!SetOK)
                    {
                        FunctionsParameterError("Sys", step);
                    }
                }
            }

            if (Int32.TryParse(step.Count, out int delayTime))
            {
                Task.Delay(delayTime).Wait();
            }
        }

        public void RES(Step step)
        {
            DMM.DMM_Rate rate;
            DMM.DMM_RES_Range range;
            try
            {
                range = (DMM.DMM_RES_Range)Enum.Parse(typeof(DMM.DMM_RES_Range), step.Oper, true);
            }
            catch (Exception)
            {
                switch (Boards.Count)
                {
                    case 1:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }
                        break;

                    case 2:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Oper";
                            step.Result2 = Step.Ng;
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Oper";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Oper";
                            step.Result3 = Step.Ng;
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Oper";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Oper";
                            step.Result3 = Step.Ng;
                        }
                        if (!Boards[3].Skip)
                        {
                            step.ValueGet4 = "Oper";
                            step.Result4 = Step.Ng;
                        }
                        break;

                    default:
                        break;
                }

                return;
            }
            try
            {
                rate = (DMM.DMM_Rate)Enum.Parse(typeof(DMM.DMM_Rate), step.Condition2, true);
                Console.WriteLine(rate.ToString());
            }
            catch (Exception)
            {
                switch (Boards.Count)
                {
                    case 1:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }
                        break;

                    case 2:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Condition";
                            step.Result3 = Step.Ng;
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Condition";
                            step.Result3 = Step.Ng;
                        }
                        if (!Boards[3].Skip)
                        {
                            step.ValueGet4 = "Condition";
                            step.Result4 = Step.Ng;
                        }
                        break;

                    default:
                        break;
                }

                return;
            }

            _DMM.SetModeRES(range, rate);
            DMM_BOARD_TEST(step);
        }

        public void ACV(Step step)
        {
            DMM.DMM_Rate rate;
            DMM.DMM_ACV_Range range;

            try
            {
                range = (DMM.DMM_ACV_Range)Enum.Parse(typeof(DMM.DMM_ACV_Range), step.Oper, true);
                Console.WriteLine(range.ToString());
            }
            catch (Exception)
            {
                FunctionsParameterError("Oper", step);

                return;
            }
            try
            {
                rate = (DMM.DMM_Rate)Enum.Parse(typeof(DMM.DMM_Rate), step.Condition2, true);
                Console.WriteLine(rate.ToString());
            }
            catch (Exception)
            {
                FunctionsParameterError("Condition", step);

                return;
            }

            _DMM.SetModeAC(range, rate);

            DMM_BOARD_TEST(step);
        }

        public void DCV(Step step)
        {
            DMM.DMM_Rate rate;
            DMM.DMM_DCV_Range range;

            try
            {
                range = (DMM.DMM_DCV_Range)Enum.Parse(typeof(DMM.DMM_DCV_Range), step.Oper, true);
                Console.WriteLine(range.ToString());
            }
            catch (Exception)
            {
                switch (Boards.Count)
                {
                    case 1:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }
                        break;

                    case 2:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Oper";
                            step.Result2 = Step.Ng;
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Oper";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Oper";
                            step.Result3 = Step.Ng;
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Oper";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Oper";
                            step.Result3 = Step.Ng;
                        }
                        if (!Boards[3].Skip)
                        {
                            step.ValueGet4 = "Oper";
                            step.Result4 = Step.Ng;
                        }
                        break;

                    default:
                        break;
                }

                return;
            }

            try
            {
                rate = (DMM.DMM_Rate)Enum.Parse(typeof(DMM.DMM_Rate), step.Condition2, true);
                Console.WriteLine(rate.ToString());
            }
            catch (Exception)
            {
                switch (Boards.Count)
                {
                    case 1:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }
                        break;

                    case 2:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Condition";
                            step.Result3 = Step.Ng;
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Condition";
                            step.Result3 = Step.Ng;
                        }
                        if (!Boards[3].Skip)
                        {
                            step.ValueGet4 = "Condition";
                            step.Result4 = Step.Ng;
                        }
                        break;

                    default:
                        break;
                }

                return;
            }

            _DMM.SetModeDC(range, rate);
            DMM_BOARD_TEST(step);
        }

        public void FREQ(Step step)
        {
            DMM.DMM_Rate rate;
            DMM.DMM_ACV_Range range;

            try
            {
                range = (DMM.DMM_ACV_Range)Enum.Parse(typeof(DMM.DMM_ACV_Range), step.Oper, true);
                Console.WriteLine(range.ToString());
            }
            catch (Exception)
            {
                switch (Boards.Count)
                {
                    case 1:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }
                        break;

                    case 2:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Oper";
                            step.Result2 = Step.Ng;
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Oper";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Oper";
                            step.Result3 = Step.Ng;
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Oper";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Oper";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Oper";
                            step.Result3 = Step.Ng;
                        }
                        if (!Boards[3].Skip)
                        {
                            step.ValueGet4 = "Oper";
                            step.Result4 = Step.Ng;
                        }
                        break;

                    default:
                        break;
                }

                return;
            }
            try
            {
                rate = (DMM.DMM_Rate)Enum.Parse(typeof(DMM.DMM_Rate), step.Condition2, true);
                Console.WriteLine(rate.ToString());
            }
            catch (Exception)
            {
                switch (Boards.Count)
                {
                    case 1:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }
                        break;

                    case 2:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Condition";
                            step.Result3 = Step.Ng;
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Condition";
                            step.Result3 = Step.Ng;
                        }
                        if (!Boards[3].Skip)
                        {
                            step.ValueGet4 = "Condition";
                            step.Result4 = Step.Ng;
                        }
                        break;

                    default:
                        break;
                }

                return;
            }

            _DMM.SetModeFREQ(range, rate);
            DMM_BOARD_TEST(step);
        }

        public void DIODE(Step step)
        {
            DMM.DMM_Rate rate;

            try
            {
                rate = (DMM.DMM_Rate)Enum.Parse(typeof(DMM.DMM_Rate), step.Condition2, true);
                Console.WriteLine(rate.ToString());
            }
            catch (Exception)
            {
                switch (Boards.Count)
                {
                    case 1:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }
                        break;

                    case 2:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Condition";
                            step.Result3 = Step.Ng;
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Condition";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Condition";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Condition";
                            step.Result3 = Step.Ng;
                        }
                        if (!Boards[3].Skip)
                        {
                            step.ValueGet4 = "Condition";
                            step.Result4 = Step.Ng;
                        }
                        break;

                    default:
                        break;
                }

                return;
            }

            _DMM.SetModeDiode(rate);

            DMM_BOARD_TEST(step);
        }

        private void DMM_BOARD_TEST(Step step)
        {
            if (!_DMM.DMM1.SerialPort.Port.IsOpen & !_DMM.DMM2.SerialPort.Port.IsOpen)
            {
                switch (Boards.Count)
                {
                    case 1:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }
                        break;

                    case 2:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Sys";
                            step.Result3 = Step.Ng;
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip)
                        {
                            step.ValueGet1 = "Sys";
                            step.Result1 = Step.Ng;
                        }

                        if (!Boards[1].Skip)
                        {
                            step.ValueGet2 = "Sys";
                            step.Result2 = Step.Ng;
                        }
                        if (!Boards[2].Skip)
                        {
                            step.ValueGet3 = "Sys";
                            step.Result3 = Step.Ng;
                        }
                        if (!Boards[3].Skip)
                        {
                            step.ValueGet4 = "Sys";
                            step.Result4 = Step.Ng;
                        }
                        break;

                    default:
                        break;
                }
                return;
            }
            Task.Delay(100).Wait();

            bool IsMux2WhenTest1Board = false;
            switch (Boards.Count)
            {
                case 1:
                    if (Boards[0].Skip) return;
                    if (!SetBoardMux(step.Condition1, 1, out IsMux2WhenTest1Board))
                    {
                        step.ValueGet1 = "condition1";
                        step.Result1 = Step.Ng;
                        return;
                    }
                    DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                    ReadDMMAndCompareBoard1(step, IsMux2WhenTest1Board);
                    break;

                case 2:
                    if (Boards[0].Skip && Boards[1].Skip) return;

                    if (!Boards[0].Skip)
                    {
                        if (!SetBoardMux(step.Condition1, 1, out _))
                        {
                            step.ValueGet1 = "condition1";
                            step.Result1 = Step.Ng;
                        }
                    }
                    if (!Boards[1].Skip)
                    {
                        if (!SetBoardMux(step.Condition1, 2, out _))
                        {
                            step.ValueGet2 = "condition1";
                            step.Result2 = Step.Ng;
                        }
                    }

                    if (step.Result1 != Step.Ng || step.Result2 != Step.Ng)
                        DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);

                    if (!Boards[0].Skip && !Boards[1].Skip)
                    {
                        if (!(step.Result1 == Step.Ng || step.Result2 == Step.Ng))
                            ReadDMMAndCompareBoard12(step);
                        else if (step.Result1 != Step.Ng)
                            ReadDMMAndCompareBoard1(step, false);
                        else if (step.Result2 != Step.Ng)
                            ReadDMMAndCompareBoard2(step);
                    }
                    else if (!Boards[0].Skip)
                    {
                        if (step.Result1 != Step.Ng)
                            ReadDMMAndCompareBoard1(step, false);
                    }
                    else if (!Boards[1].Skip)
                    {
                        if (step.Result2 != Step.Ng)
                            ReadDMMAndCompareBoard2(step);
                    }

                    break;

                case 3:
                    if (Boards[0].Skip && Boards[1].Skip && Boards[2].Skip) return;
                    if (!Boards[0].Skip)
                    {
                        if (!SetBoardMux(step.Condition1, 1, out _))
                        {
                            step.ValueGet1 = "condition1";
                            step.Result1 = Step.Ng;
                        }
                    }
                    if (!Boards[1].Skip)
                    {
                        if (!SetBoardMux(step.Condition1, 2, out _))
                        {
                            step.ValueGet2 = "condition1";
                            step.Result2 = Step.Ng;
                        }
                    }

                    if (!Boards[0].Skip || !Boards[1].Skip)
                    {
                        if (step.Result1 != Step.Ng || step.Result2 != Step.Ng)
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                    }

                    if (!Boards[0].Skip && !Boards[1].Skip)
                    {
                        if (!(step.Result1 == Step.Ng || step.Result2 == Step.Ng))
                            ReadDMMAndCompareBoard12(step);
                        else if (step.Result1 != Step.Ng)
                            ReadDMMAndCompareBoard1(step, false);
                        else if (step.Result2 != Step.Ng)
                            ReadDMMAndCompareBoard2(step);
                    }
                    else if (!Boards[0].Skip)
                    {
                        if (step.Result1 != Step.Ng)
                            ReadDMMAndCompareBoard1(step, false);
                    }
                    else if (!Boards[1].Skip)
                    {
                        if (step.Result2 != Step.Ng)
                            ReadDMMAndCompareBoard2(step);
                    }

                    if (!Boards[2].Skip)
                    {
                        if (!SetBoardMux(step.Condition1, 2, out _))
                        {
                            step.ValueGet3 = "condition1";
                            step.Result3 = Step.Ng;
                        }
                        else
                        {
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                        }
                    }

                    if (!Boards[2].Skip)
                        if (step.Result3 != Step.Ng)
                            ReadDMMAndCompareBoard3(step);
                        else
                            goto Out;

                    break;

                case 4:
                    if (Boards[0].Skip && Boards[1].Skip && Boards[2].Skip && Boards[3].Skip) return;
                    if (!Boards[0].Skip)
                    {
                        if (!SetBoardMux(step.Condition1, 1, out _))
                        {
                            step.ValueGet1 = "condition1";
                            step.Result1 = Step.Ng;
                        }
                    }
                    if (!Boards[1].Skip)
                    {
                        if (!SetBoardMux(step.Condition1, 2, out _))
                        {
                            step.ValueGet2 = "condition1";
                            step.Result2 = Step.Ng;
                        }
                    }

                    if (!Boards[0].Skip || !Boards[1].Skip)
                    {
                        if (!(step.Result1 == Step.Ng || step.Result2 == Step.Ng))
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                    }

                    if (!Boards[0].Skip && !Boards[1].Skip)
                    {
                        if (!(step.Result1 == Step.Ng || step.Result2 == Step.Ng))
                            ReadDMMAndCompareBoard12(step);
                        else if (step.Result1 != Step.Ng)
                            ReadDMMAndCompareBoard1(step, false);
                        else if (step.Result2 != Step.Ng)
                            ReadDMMAndCompareBoard2(step);
                    }
                    else if (!Boards[0].Skip)
                    {
                        if (step.Result1 != Step.Ng)
                            ReadDMMAndCompareBoard1(step, false);
                    }
                    else if (!Boards[1].Skip)
                    {
                        if (step.Result2 != Step.Ng)
                            ReadDMMAndCompareBoard2(step);
                    }

                    if (!Boards[2].Skip)
                    {
                        if (!SetBoardMux(step.Condition1, 3, out _))
                        {
                            step.ValueGet3 = "condition1";
                            step.Result3 = Step.Ng;
                        }
                    }
                    if (!Boards[3].Skip)
                    {
                        if (!SetBoardMux(step.Condition1, 4, out _))
                        {
                            step.ValueGet4 = "condition1";
                            step.Result4 = Step.Ng;
                        }
                    }

                    if (!Boards[2].Skip || !Boards[3].Skip)
                    {
                        if (!(step.Result3 == Step.Ng || step.Result4 == Step.Ng))
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                    }

                    if (!Boards[2].Skip && !Boards[3].Skip)
                    {
                        if (!(step.Result3 == Step.Ng || step.Result4 == Step.Ng))
                            ReadDMMAndCompareBoard34(step);
                        else if (step.Result3 != Step.Ng)
                            ReadDMMAndCompareBoard3(step);
                        else if (step.Result4 != Step.Ng)
                            ReadDMMAndCompareBoard4(step);
                    }
                    else if (!Boards[2].Skip)
                    {
                        if (step.Result3 != Step.Ng)
                            ReadDMMAndCompareBoard3(step);
                    }
                    else if (!Boards[3].Skip)
                    {
                        if (step.Result4 != Step.Ng)
                            ReadDMMAndCompareBoard4(step);
                    }
                    break;

                default:
                    break;
            }
        Out:
            Task.Delay(appSetting.ETCSetting.DelayDMMRead).Wait();
            MuxCard.Card.ReleaseChannels();
        }

        private bool ReadDMMAndCompareBoard1(Step step, bool ReadByDmm2)
        {
            var retry = Int32.Parse(step.Count);
            retry = retry < 2 ? 1 : retry;

            if (step.Mode != "Spect")
            {
                _DMM.DMM1.RequestValues(retry);
                _DMM.DMM2.RequestValues(retry);
            }

            double minValue = 0;
            double maxValue = 0;

            if (!Double.TryParse(step.Min, out minValue))
            {
                step.ValueGet1 = "Min";

                step.Result1 = Step.Ng;

                return false;
            }

            if (!Double.TryParse(step.Max, out maxValue))
            {
                if (step.Max == "L")
                {
                    maxValue = Double.MaxValue;
                }
                else
                {
                    step.ValueGet1 = "Max";

                    step.Result1 = Step.Ng;

                    return false;
                }
            }

            if (step.Result1 == Step.Ng) return false;

            switch (step.Mode)
            {
                case "SPEC":
                    for (int i = 0; i < retry; i++)
                    {
                        if (step.Result1 != Step.Ok & Boards.Count > 0)
                        {
                            if (ReadByDmm2) _DMM.DMM2.GetValue();
                            else _DMM.DMM1.GetValue();
                            step.ValueGet1 = _DMM.DMM1.LastStringValue;
                            if (_DMM.DMM1.LastDoubleValue <= maxValue & _DMM.DMM1.LastDoubleValue >= minValue)
                            {
                                step.Result1 = Step.Ok;
                                break;
                            }
                            else
                            {
                                step.Result1 = Step.Ng;
                            }
                        }
                        Task.Delay(appSetting.ETCSetting.DelayDMMRead).Wait();
                    }
                    break;

                case "CONT":
                    for (int i = 0; i < 40; i++)
                    {
                        if (ReadByDmm2) _DMM.DMM2.GetValue(i, 1);
                        else _DMM.DMM1.GetValue(i, 1);
                        Task.Delay(appSetting.ETCSetting.DelayDMMRead).Wait();
                    }
                    var DMM1_PassValues = _DMM.DMM1.ValuesCount1.Where(x => (x >= minValue && x <= maxValue)).ToList();
                    if (DMM1_PassValues.Count == retry)
                    {
                        step.ValueGet1 = _DMM.DMM1.LastStringValue;
                        step.Result1 = Step.Ng;
                    }
                    else
                    {
                        step.Result1 = Step.Ok;
                        step.ValueGet1 = _DMM.DMM1.GetStringValue(DMM1_PassValues[0]);
                    }

                    break;

                case "MIN":
                    for (int i = 0; i < retry; i++)
                    {
                        if (ReadByDmm2) _DMM.DMM2.GetValue(i, 1);
                        else _DMM.DMM1.GetValue(i, 1);
                    }

                    step.Result1 = Step.Ok;

                    step.ValueGet1 = _DMM.DMM1.GetStringValue(_DMM.DMM1.MIN_1);

                    if (_DMM.DMM1.MIN_1 <= maxValue && _DMM.DMM1.MIN_1 >= minValue)
                    {
                        step.Result1 = Step.Ng;
                    }
                    break;

                case "AVR":
                    for (int i = 0; i < retry; i++)
                    {
                        if (ReadByDmm2) _DMM.DMM2.GetValue(i, 1);
                        else _DMM.DMM1.GetValue(i, 1);
                    }
                    step.Result1 = Step.Ok;

                    step.ValueGet1 = _DMM.DMM1.GetStringValue(_DMM.DMM1.AVR_1);

                    if (_DMM.DMM1.AVR_1 > maxValue || _DMM.DMM1.AVR_1 < minValue)
                    {
                        step.Result1 = Step.Ng;
                    }
                    step.Result2 = Step.Ng;

                    break;

                case "MAX":
                    for (int i = 0; i < retry; i++)
                    {
                        if (ReadByDmm2) _DMM.DMM2.GetValue(i, 1);
                        else _DMM.DMM1.GetValue(i, 1);
                    }

                    step.Result1 = Step.Ok;

                    step.ValueGet1 = _DMM.DMM1.GetStringValue(_DMM.DMM1.MAX_1);
                    if (_DMM.DMM1.MAX_1 <= maxValue && _DMM.DMM1.MAX_1 >= minValue)
                    {
                        step.Result1 = Step.Ng;
                    }
                    break;

                default:
                    break;
            }

            return StepTestResult(step);
        }

        private bool ReadDMMAndCompareBoard2(Step step)
        {
            var retry = Int32.Parse(step.Count);
            retry = retry < 2 ? 1 : retry;

            if (step.Mode != "Spect")
            {
                _DMM.DMM2.RequestValues(retry);
            }

            double minValue = 0;
            double maxValue = 0;

            if (!Double.TryParse(step.Min, out minValue))
            {
                step.ValueGet2 = "Min";

                step.Result2 = Step.Ng;

                return false;
            }

            if (!Double.TryParse(step.Max, out maxValue))
            {
                if (step.Max == "L")
                {
                    maxValue = Double.MaxValue;
                }
                else
                {
                    step.ValueGet2 = "Max";

                    step.Result2 = Step.Ng;

                    return false;
                }
            }

            if (step.Result2 == Step.Ng) return false;

            switch (step.Mode)
            {
                case "SPEC":
                    for (int i = 0; i < retry; i++)
                    {
                        if (step.Result2 != Step.Ok & Boards.Count > 1)
                        {
                            _DMM.DMM2.GetValue();
                            step.ValueGet2 = _DMM.DMM2.LastStringValue;
                            if (_DMM.DMM2.LastDoubleValue <= maxValue & _DMM.DMM2.LastDoubleValue >= minValue)
                            {
                                step.Result2 = Step.Ok;
                                break;
                            }
                            else
                            {
                                step.Result2 = Step.Ng;
                            }
                        }
                        Task.Delay(appSetting.ETCSetting.DelayDMMRead).Wait();
                    }
                    break;

                case "CONT":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM2.GetValue(i, 1);
                        Task.Delay(appSetting.ETCSetting.DelayDMMRead).Wait();
                    }
                    var DMM2_PassValues = _DMM.DMM2.ValuesCount1.Where(x => (x >= minValue && x <= maxValue)).ToList();

                    if (DMM2_PassValues.Count == 0)
                    {
                        step.ValueGet2 = _DMM.DMM2.LastStringValue;
                        step.Result2 = Step.Ng;
                    }
                    else
                    {
                        step.ValueGet2 = _DMM.DMM2.GetStringValue(DMM2_PassValues[0]);
                        step.Result2 = Step.Ok;
                    }

                    break;

                case "MIN":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM2.GetValue(i, 1);
                    }

                    step.Result2 = Step.Ok;

                    step.ValueGet2 = _DMM.DMM2.GetStringValue(_DMM.DMM2.MIN_1);

                    if (_DMM.DMM2.MIN_1 <= maxValue && _DMM.DMM2.MIN_1 >= minValue)
                    {
                        step.Result2 = Step.Ng;
                    }
                    break;

                case "AVR":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM2.GetValue(i, 1);
                    }
                    step.Result2 = Step.Ok;
                    step.ValueGet2 = _DMM.DMM2.GetStringValue(_DMM.DMM2.AVR_1);

                    if (_DMM.DMM2.AVR_1 > maxValue || _DMM.DMM2.AVR_1 < minValue)
                    {
                        step.Result2 = Step.Ng;
                    }
                    break;

                case "MAX":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM2.GetValue(i, 1);
                    }

                    step.Result2 = Step.Ok;

                    step.ValueGet2 = _DMM.DMM2.GetStringValue(_DMM.DMM2.MAX_1);

                    if (_DMM.DMM2.MAX_1 <= maxValue && _DMM.DMM2.MAX_1 >= minValue)
                    {
                        step.Result2 = Step.Ng;
                    }
                    break;

                default:
                    break;
            }

            return StepTestResult(step);
        }

        private bool ReadDMMAndCompareBoard3(Step step)
        {
            var retry = Int32.Parse(step.Count);
            retry = retry < 2 ? 2 : retry;

            if (step.Mode != "Spect")
            {
                _DMM.DMM1.RequestValues(retry);
            }

            double minValue = 0;
            double maxValue = 0;

            if (!Double.TryParse(step.Min, out minValue))
            {
                step.ValueGet3 = "Min";

                step.Result3 = Step.Ng;

                return false;
            }

            if (!Double.TryParse(step.Max, out maxValue))
            {
                if (step.Max == "L")
                {
                    maxValue = Double.MaxValue;
                }
                else
                {
                    step.ValueGet3 = "Max";

                    step.Result3 = Step.Ng;

                    return false;
                }
            }

            if (step.Result3 == Step.Ng) return false;

            switch (step.Mode)
            {
                case "SPEC":
                    for (int i = 0; i < retry; i++)
                    {
                        if (step.Result3 != Step.Ok & Boards.Count > 0)
                        {
                            _DMM.DMM1.GetValue();
                            step.ValueGet3 = _DMM.DMM1.LastStringValue;
                            if (_DMM.DMM1.LastDoubleValue <= maxValue & _DMM.DMM1.LastDoubleValue >= minValue)
                            {
                                step.Result3 = Step.Ok;
                                break;
                            }
                            else
                            {
                                step.Result3 = Step.Ng;
                            }
                        }
                        Task.Delay(appSetting.ETCSetting.DelayDMMRead).Wait();
                    }
                    break;

                case "CONT":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                    }
                    var DMM1_PassValues = _DMM.DMM1.ValuesCount1.Where(x => (x >= minValue && x <= maxValue)).ToList();
                    if (DMM1_PassValues.Count == 0)
                    {
                        step.ValueGet3 = _DMM.DMM1.LastStringValue;
                        step.Result3 = Step.Ng;
                    }
                    else
                    {
                        step.Result3 = Step.Ok;
                        step.ValueGet3 = _DMM.DMM1.GetStringValue(DMM1_PassValues[0]);
                    }

                    break;

                case "MIN":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                    }

                    step.Result3 = Step.Ok;

                    step.ValueGet3 = _DMM.DMM1.GetStringValue(_DMM.DMM1.MIN_1);

                    if (_DMM.DMM1.MIN_1 <= maxValue && _DMM.DMM1.MIN_1 >= minValue)
                    {
                        step.Result3 = Step.Ng;
                    }
                    break;

                case "AVR":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                    }
                    step.Result3 = Step.Ok;

                    step.ValueGet3 = _DMM.DMM1.GetStringValue(_DMM.DMM1.AVR_1);

                    if (_DMM.DMM1.AVR_1 > maxValue || _DMM.DMM1.AVR_1 < minValue)
                    {
                        step.Result3 = Step.Ng;
                    }
                    break;

                case "MAX":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                    }

                    step.Result3 = Step.Ok;

                    step.ValueGet3 = _DMM.DMM1.GetStringValue(_DMM.DMM1.MAX_1);
                    if (_DMM.DMM1.MAX_1 <= maxValue && _DMM.DMM1.MAX_1 >= minValue)
                    {
                        step.Result3 = Step.Ng;
                    }
                    break;

                default:
                    break;
            }

            return StepTestResult(step);
        }

        private bool ReadDMMAndCompareBoard4(Step step)
        {
            var retry = Int32.Parse(step.Count);
            retry = retry < 2 ? 2 : retry;

            if (step.Mode != "Spect")
            {
                _DMM.DMM1.RequestValues(retry);
                _DMM.DMM2.RequestValues(retry);
            }

            double minValue = 0;
            double maxValue = 0;

            if (!Double.TryParse(step.Min, out minValue))
            {
                step.ValueGet4 = "Min";

                step.Result4 = Step.Ng;

                return false;
            }

            if (!Double.TryParse(step.Max, out maxValue))
            {
                if (step.Max == "L")
                {
                    maxValue = Double.MaxValue;
                }
                else
                {
                    step.ValueGet4 = "Max";

                    step.Result4 = Step.Ng;

                    return false;
                }
            }

            if (step.Result4 == Step.Ng) return false;

            switch (step.Mode)
            {
                case "SPEC":
                    for (int i = 0; i < retry; i++)
                    {
                        if (step.Result4 != Step.Ok & Boards.Count > 1)
                        {
                            _DMM.DMM2.GetValue();
                            step.ValueGet4 = _DMM.DMM2.LastStringValue;
                            if (_DMM.DMM2.LastDoubleValue <= maxValue & _DMM.DMM2.LastDoubleValue >= minValue)
                            {
                                step.Result4 = Step.Ok;
                            }
                            else
                            {
                                step.Result4 = Step.Ng;
                            }
                        }
                    }
                    break;

                case "CONT":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM2.GetValue(i, 1);
                    }
                    var DMM2_PassValues = _DMM.DMM2.ValuesCount1.Where(x => (x >= minValue && x <= maxValue)).ToList();

                    if (DMM2_PassValues.Count == 0)
                    {
                        step.ValueGet4 = _DMM.DMM2.LastStringValue;
                        step.Result4 = Step.Ng;
                    }
                    else
                    {
                        step.Result4 = Step.Ok;
                        step.ValueGet4 = _DMM.DMM2.GetStringValue(DMM2_PassValues[0]);
                    }

                    break;

                case "MIN":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM2.GetValue(i, 1);
                    }

                    step.Result4 = Step.Ok;

                    step.ValueGet4 = _DMM.DMM2.GetStringValue(_DMM.DMM2.MIN_1);

                    if (_DMM.DMM2.MIN_1 <= maxValue && _DMM.DMM2.MIN_1 >= minValue)
                    {
                        step.Result4 = Step.Ng;
                    }
                    break;

                case "AVR":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM2.GetValue(i, 1);
                    }
                    step.Result4 = Step.Ok;

                    step.ValueGet4 = _DMM.DMM2.GetStringValue(_DMM.DMM2.AVR_1);

                    if (_DMM.DMM2.AVR_1 > maxValue || _DMM.DMM2.AVR_1 < minValue)
                    {
                        step.Result4 = Step.Ng;
                    }
                    break;

                case "MAX":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM2.GetValue(i, 1);
                    }

                    step.Result4 = Step.Ok;

                    step.ValueGet4 = _DMM.DMM2.GetStringValue(_DMM.DMM2.MAX_1);

                    if (_DMM.DMM2.MAX_1 <= maxValue && _DMM.DMM2.MAX_1 >= minValue)
                    {
                        step.Result4 = Step.Ng;
                    }
                    break;

                default:
                    break;
            }

            return StepTestResult(step);
        }

        private bool ReadDMMAndCompareBoard12(Step step)
        {
            var retry = Int32.Parse(step.Count);
            retry = retry < 2 ? 1 : retry;

            if (step.Mode != "Spect")
            {
                _DMM.DMM1.RequestValues(retry);
                _DMM.DMM2.RequestValues(retry);
            }

            double minValue = 0;
            double maxValue = 0;

            if (!Double.TryParse(step.Min, out minValue))
            {
                step.ValueGet1 = "Min";
                step.ValueGet2 = "Min";
                step.ValueGet3 = "Min";
                step.ValueGet4 = "Min";

                step.Result1 = Step.Ng;
                step.Result2 = Step.Ng;
                step.Result3 = Step.Ng;
                step.Result4 = Step.Ng;

                return false;
            }

            if (!Double.TryParse(step.Max, out maxValue))
            {
                if (step.Max == "L")
                {
                    maxValue = Double.MaxValue;
                }
                else
                {
                    step.ValueGet1 = "Max";
                    step.ValueGet2 = "Max";
                    step.ValueGet3 = "Max";
                    step.ValueGet4 = "Max";

                    step.Result1 = Step.Ng;
                    step.Result2 = Step.Ng;
                    step.Result3 = Step.Ng;
                    step.Result4 = Step.Ng;

                    return false;
                }
            }

            switch (step.Mode)
            {
                case "SPEC":
                    for (int i = 0; i < retry; i++)
                    {
                        if (step.Result1 != Step.Ok & Boards.Count > 0)
                        {
                            _DMM.DMM1.GetValue();
                            step.ValueGet1 = _DMM.DMM1.LastStringValue;
                            if (_DMM.DMM1.LastDoubleValue <= maxValue & _DMM.DMM1.LastDoubleValue >= minValue)
                            {
                                step.Result1 = Step.Ok;
                            }
                            else
                            {
                                step.Result1 = Step.Ng;
                            }
                        }
                        if (step.Result2 != Step.Ok & Boards.Count > 1)
                        {
                            _DMM.DMM2.GetValue();
                            step.ValueGet2 = _DMM.DMM2.LastStringValue;
                            if (_DMM.DMM2.LastDoubleValue <= maxValue & _DMM.DMM2.LastDoubleValue >= minValue)
                            {
                                step.Result2 = Step.Ok;
                            }
                            else
                            {
                                step.Result2 = Step.Ng;
                            }
                        }
                        if (step.Result1 == Step.Ok && step.Result2 == Step.Ok)
                        {
                            break;
                        }
                        Task.Delay(appSetting.ETCSetting.DelayDMMRead).Wait();
                    }
                    break;

                case "CONT":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                        _DMM.DMM2.GetValue(i, 1);
                        Task.Delay(appSetting.ETCSetting.DelayDMMRead).Wait();
                    }
                    var DMM1_PassValues = _DMM.DMM1.ValuesCount1.Where(x => (x >= minValue && x <= maxValue)).ToList();
                    var DMM2_PassValues = _DMM.DMM2.ValuesCount1.Where(x => (x >= minValue && x <= maxValue)).ToList();
                    if (DMM1_PassValues.Count == 0)
                    {
                        step.ValueGet1 = _DMM.DMM1.LastStringValue;
                        step.Result1 = Step.Ng;
                    }
                    else
                    {
                        step.Result1 = Step.Ok;
                        step.ValueGet1 = _DMM.DMM1.GetStringValue(DMM1_PassValues[0]);
                    }

                    if (DMM2_PassValues.Count == 0)
                    {
                        step.ValueGet2 = _DMM.DMM2.LastStringValue;
                        step.Result2 = Step.Ng;
                    }
                    else
                    {
                        step.Result2 = Step.Ok;
                        step.ValueGet2 = _DMM.DMM2.GetStringValue(DMM2_PassValues[0]);
                    }

                    break;

                case "MIN":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                        _DMM.DMM2.GetValue(i, 1);
                    }

                    step.Result1 = Step.Ok;
                    step.Result2 = Step.Ok;

                    step.ValueGet1 = _DMM.DMM1.GetStringValue(_DMM.DMM1.MIN_1);
                    step.ValueGet2 = _DMM.DMM2.GetStringValue(_DMM.DMM2.MIN_1);

                    if (_DMM.DMM1.MIN_1 <= maxValue && _DMM.DMM1.MIN_1 >= minValue)
                    {
                        step.Result1 = Step.Ng;
                    }
                    if (_DMM.DMM2.MIN_1 <= maxValue && _DMM.DMM2.MIN_1 >= minValue)
                    {
                        step.Result2 = Step.Ng;
                    }
                    break;

                case "AVR":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                        _DMM.DMM2.GetValue(i, 1);
                    }
                    step.Result1 = Step.Ok;
                    step.Result2 = Step.Ok;

                    step.ValueGet1 = _DMM.DMM1.GetStringValue(_DMM.DMM1.AVR_1);
                    step.ValueGet2 = _DMM.DMM2.GetStringValue(_DMM.DMM2.AVR_1);

                    if (_DMM.DMM1.AVR_1 > maxValue || _DMM.DMM1.AVR_1 < minValue)
                    {
                        step.Result1 = Step.Ng;
                    }
                    if (_DMM.DMM2.AVR_1 > maxValue || _DMM.DMM2.AVR_1 < minValue)
                    {
                        step.Result2 = Step.Ng;
                    }
                    break;

                case "MAX":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                        _DMM.DMM2.GetValue(i, 1);
                    }

                    step.Result1 = Step.Ok;
                    step.Result2 = Step.Ok;

                    step.ValueGet1 = _DMM.DMM1.GetStringValue(_DMM.DMM1.MAX_1);
                    step.ValueGet2 = _DMM.DMM2.GetStringValue(_DMM.DMM2.MAX_1);
                    if (_DMM.DMM1.MAX_1 <= maxValue && _DMM.DMM1.MAX_1 >= minValue)
                    {
                        step.Result1 = Step.Ng;
                    }
                    if (_DMM.DMM2.MAX_1 <= maxValue && _DMM.DMM2.MAX_1 >= minValue)
                    {
                        step.Result2 = Step.Ng;
                    }
                    break;

                default:
                    break;
            }

            return StepTestResult(step);
        }

        private bool ReadDMMAndCompareBoard34(Step step)
        {
            var retry = Int32.Parse(step.Count);
            retry = retry < 2 ? 2 : retry;

            _DMM.DMM1.RequestValues(retry);
            _DMM.DMM2.RequestValues(retry);

            double minValue = 0;
            double maxValue = 0;

            if (!Double.TryParse(step.Min, out minValue))
            {
                step.ValueGet3 = "Min";
                step.ValueGet4 = "Min";

                step.Result3 = Step.Ng;
                step.Result4 = Step.Ng;

                return false;
            }

            if (!Double.TryParse(step.Max, out maxValue))
            {
                if (step.Max == "L")
                {
                    maxValue = Double.MaxValue;
                }
                else
                {
                    step.ValueGet3 = "Max";
                    step.ValueGet4 = "Max";

                    step.Result3 = Step.Ng;
                    step.Result4 = Step.Ng;

                    return false;
                }
            }

            step.Result3 = Step.DontCare;
            step.Result4 = Step.DontCare;

            switch (step.Mode)
            {
                case "SPEC":
                    for (int i = 0; i < retry; i++)
                    {
                        if (step.Result3 != Step.Ok & Boards.Count > 0)
                        {
                            _DMM.DMM1.GetValue();
                            step.ValueGet3 = _DMM.DMM1.LastStringValue;
                            if (_DMM.DMM1.LastDoubleValue <= maxValue & _DMM.DMM1.LastDoubleValue >= minValue)
                            {
                                step.Result3 = Step.Ok;
                            }
                            else
                            {
                                step.Result3 = Step.Ng;
                            }
                        }
                        if (step.Result4 != Step.Ok & Boards.Count > 1)
                        {
                            _DMM.DMM2.GetValue();
                            step.ValueGet4 = _DMM.DMM2.LastStringValue;
                            if (_DMM.DMM2.LastDoubleValue <= maxValue & _DMM.DMM2.LastDoubleValue >= minValue)
                            {
                                step.Result4 = Step.Ok;
                            }
                            else
                            {
                                step.Result4 = Step.Ng;
                            }
                        }
                    }
                    break;

                case "CONT":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                        _DMM.DMM2.GetValue(i, 1);
                    }
                    var DMM1_PassValues = _DMM.DMM1.ValuesCount1.Where(x => (x >= minValue && x <= maxValue)).ToList();
                    var DMM2_PassValues = _DMM.DMM2.ValuesCount1.Where(x => (x >= minValue && x <= maxValue)).ToList();
                    if (DMM1_PassValues.Count == 0)
                    {
                        step.ValueGet3 = _DMM.DMM1.LastStringValue;
                        step.Result3 = Step.Ng;
                    }
                    else
                    {
                        step.Result3 = Step.Ok;
                        step.ValueGet3 = _DMM.DMM1.GetStringValue(DMM1_PassValues[0]);
                    }

                    if (DMM2_PassValues.Count == 0)
                    {
                        step.ValueGet4 = _DMM.DMM2.LastStringValue;
                        step.Result4 = Step.Ng;
                    }
                    else
                    {
                        step.Result4 = Step.Ok;
                        step.ValueGet4 = _DMM.DMM2.GetStringValue(DMM2_PassValues[0]);
                    }

                    break;

                case "MIN":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                        _DMM.DMM2.GetValue(i, 1);
                    }

                    step.Result3 = Step.Ok;
                    step.Result4 = Step.Ok;

                    step.ValueGet3 = _DMM.DMM1.GetStringValue(_DMM.DMM1.MIN_1);
                    step.ValueGet4 = _DMM.DMM2.GetStringValue(_DMM.DMM2.MIN_1);

                    if (_DMM.DMM1.MIN_1 <= maxValue && _DMM.DMM1.MIN_1 >= minValue)
                    {
                        step.Result3 = Step.Ng;
                    }
                    if (_DMM.DMM2.MIN_1 <= maxValue && _DMM.DMM2.MIN_1 >= minValue)
                    {
                        step.Result4 = Step.Ng;
                    }
                    break;

                case "AVR":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                        _DMM.DMM2.GetValue(i, 1);
                    }
                    step.Result3 = Step.Ok;
                    step.Result4 = Step.Ok;

                    step.ValueGet2 = _DMM.DMM1.GetStringValue(_DMM.DMM1.AVR_1);
                    step.ValueGet4 = _DMM.DMM2.GetStringValue(_DMM.DMM2.AVR_1);

                    if (_DMM.DMM1.AVR_1 > maxValue || _DMM.DMM1.AVR_1 < minValue)
                    {
                        step.Result3 = Step.Ng;
                    }
                    if (_DMM.DMM2.AVR_1 > maxValue || _DMM.DMM2.AVR_1 < minValue)
                    {
                        step.Result4 = Step.Ng;
                    }
                    break;

                case "MAX":
                    for (int i = 0; i < retry; i++)
                    {
                        _DMM.DMM1.GetValue(i, 1);
                        _DMM.DMM2.GetValue(i, 1);
                    }

                    step.Result3 = Step.Ok;
                    step.Result4 = Step.Ok;

                    step.ValueGet2 = _DMM.DMM1.GetStringValue(_DMM.DMM1.MAX_1);
                    step.ValueGet4 = _DMM.DMM2.GetStringValue(_DMM.DMM2.MAX_1);
                    if (_DMM.DMM1.MAX_1 <= maxValue && _DMM.DMM1.MAX_1 >= minValue)
                    {
                        step.Result3 = Step.Ng;
                    }
                    if (_DMM.DMM2.MAX_1 <= maxValue && _DMM.DMM2.MAX_1 >= minValue)
                    {
                        step.Result4 = Step.Ng;
                    }
                    break;

                default:
                    break;
            }

            return StepTestResult(step);
        }

        private void DelayAfterMuxSellect(DMM.DMM_Mode mode, DMM.DMM_Rate rate)
        {
            switch (mode)
            {
                case DMM.DMM_Mode.NONE:
                    break;

                case DMM.DMM_Mode.DCV:
                    switch (rate)
                    {
                        case DMM.DMM_Rate.NONE:
                            break;

                        case DMM.DMM_Rate.SLOW:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_slow_DCV).Wait();
                            break;

                        case DMM.DMM_Rate.MID:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_Mid_DCV).Wait();
                            break;

                        case DMM.DMM_Rate.FAST:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_Fast_DCV).Wait();
                            break;

                        default:
                            break;
                    }
                    break;

                case DMM.DMM_Mode.ACV:
                    switch (rate)
                    {
                        case DMM.DMM_Rate.NONE:
                            break;

                        case DMM.DMM_Rate.SLOW:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_slow_ACVFRQ).Wait();
                            break;

                        case DMM.DMM_Rate.MID:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_Mid_ACVFRQ).Wait();
                            break;

                        case DMM.DMM_Rate.FAST:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_Fast_ACVFRQ).Wait();
                            break;

                        default:
                            break;
                    }
                    break;

                case DMM.DMM_Mode.FREQ:
                    switch (rate)
                    {
                        case DMM.DMM_Rate.NONE:
                            break;

                        case DMM.DMM_Rate.SLOW:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_slow_ACVFRQ).Wait();
                            break;

                        case DMM.DMM_Rate.MID:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_Mid_ACVFRQ).Wait();
                            break;

                        case DMM.DMM_Rate.FAST:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_Fast_ACVFRQ).Wait();
                            break;

                        default:
                            break;
                    }
                    break;

                case DMM.DMM_Mode.RES:
                    switch (rate)
                    {
                        case DMM.DMM_Rate.NONE:
                            break;

                        case DMM.DMM_Rate.SLOW:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_slow_RES).Wait();
                            break;

                        case DMM.DMM_Rate.MID:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_Mid_RES).Wait();
                            break;

                        case DMM.DMM_Rate.FAST:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_Fast_RES).Wait();
                            break;

                        default:
                            break;
                    }
                    break;

                case DMM.DMM_Mode.DIODE:
                    switch (rate)
                    {
                        case DMM.DMM_Rate.NONE:
                            break;

                        case DMM.DMM_Rate.SLOW:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_slow_DCV).Wait();
                            break;

                        case DMM.DMM_Rate.MID:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_Mid_DCV).Wait();
                            break;

                        case DMM.DMM_Rate.FAST:
                            Task.Delay(appSetting.ETCSetting.MUXdelay_Fast_DCV).Wait();
                            break;

                        default:
                            break;
                    }
                    break;

                default:
                    break;
            }
        }

        private bool DisCharge()
        {
            if (!_DMM.DMM1.SerialPort.Port.IsOpen & !_DMM.DMM2.SerialPort.Port.IsOpen)
            {
                return false;
            }
            System.System_Board.MachineIO.ADSC = true;
            System.System_Board.MachineIO.BDSC = true;
            System.System_Board.SendControl();

            //Discharge item 1
            bool DisChargeItem1Pass = false;
            bool DisChargeItem2Pass = false;
            bool DisChargeItem3Pass = false;

            DateTime StartDisChargeTime = DateTime.Now;
            if (TestModel.Discharge.DischargeItem1 != 0) // check none discharge mode
            {
                switch (TestModel.Discharge.DischargeItem1)
                {
                    case 1:
                        _DMM.SetModeDC(DMM.DMM_DCV_Range.DC1000V, DMM.DMM_Rate.MID);
                        break;

                    case 2:
                        _DMM.SetModeAC(DMM.DMM_ACV_Range.AC750V, DMM.DMM_Rate.MID);
                        break;

                    default:
                        break;
                }
                switch (Boards.Count)
                {
                    case 1:
                        if (Boards[0].Skip) return true;
                        if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out bool IsMux2WhenTest1Board))
                        {
                            return false;
                        }
                        DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                        StartDisChargeTime = DateTime.Now;
                        while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                        {
                            if (IsMux2WhenTest1Board)
                            {
                                _DMM.DMM1.GetValue();
                                if (_DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow)
                                {
                                    DisChargeItem1Pass = true;
                                    break;
                                }
                            }
                            else
                            {
                                _DMM.DMM2.GetValue();
                                if (_DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow)
                                {
                                    DisChargeItem1Pass = true;
                                    break;
                                }
                            }
                        }
                        break;

                    case 2:
                        if (Boards[0].Skip && Boards[1].Skip) return true;
                        if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out _))
                        {
                            return false;
                        }
                        if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 2, out _))
                        {
                            return false;
                        }
                        DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                        StartDisChargeTime = DateTime.Now;
                        while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                        {
                            _DMM.DMM1.GetValue();
                            _DMM.DMM2.GetValue();
                            if (!Boards[0].Skip && !Boards[1].Skip)
                            {
                                DisChargeItem1Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                    & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                if (DisChargeItem1Pass) break;
                            }
                            else if (!Boards[0].Skip)
                            {
                                DisChargeItem1Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                if (DisChargeItem1Pass) break;
                            }
                            else if (!Boards[1].Skip)
                            {
                                DisChargeItem1Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                if (DisChargeItem1Pass) break;
                            }
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip || !Boards[1].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out _))
                            {
                                return false;
                            }
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 2, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                _DMM.DMM2.GetValue();
                                if (!Boards[0].Skip && !Boards[1].Skip)
                                {
                                    DisChargeItem1Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                        & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem1Pass) break;
                                }
                                else if (!Boards[0].Skip)
                                {
                                    DisChargeItem1Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem1Pass) break;
                                }
                                else if (!Boards[1].Skip)
                                {
                                    DisChargeItem1Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem1Pass) break;
                                }
                            }
                        }

                        if (!Boards[2].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 3, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                if (_DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow)
                                {
                                    DisChargeItem1Pass &= true;
                                    break;
                                }
                            }
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip || !Boards[1].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out _))
                            {
                                return false;
                            }
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 2, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                _DMM.DMM2.GetValue();
                                if (!Boards[0].Skip && !Boards[1].Skip)
                                {
                                    DisChargeItem1Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                        & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem1Pass) break;
                                }
                                else if (!Boards[0].Skip)
                                {
                                    DisChargeItem1Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem1Pass) break;
                                }
                                else if (!Boards[1].Skip)
                                {
                                    DisChargeItem1Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem1Pass) break;
                                }
                            }
                        }

                        if (!Boards[3].Skip || !Boards[2].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 3, out _))
                            {
                                return false;
                            }
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 4, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                _DMM.DMM2.GetValue();
                                if (!Boards[2].Skip && !Boards[3].Skip)
                                {
                                    DisChargeItem1Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                        & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem1Pass) break;
                                }
                                else if (!Boards[2].Skip)
                                {
                                    DisChargeItem1Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem1Pass) break;
                                }
                                else if (!Boards[3].Skip)
                                {
                                    DisChargeItem1Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem1Pass) break;
                                }
                            }
                        }
                        break;
                }
            }

            if (TestModel.Discharge.DischargeItem2 != 0 && DisChargeItem1Pass) // check none discharge mode
            {
                switch (TestModel.Discharge.DischargeItem2)
                {
                    case 1:
                        _DMM.SetModeDC(DMM.DMM_DCV_Range.DC1000V, DMM.DMM_Rate.MID);
                        break;

                    case 2:
                        _DMM.SetModeAC(DMM.DMM_ACV_Range.AC750V, DMM.DMM_Rate.MID);
                        break;

                    default:
                        break;
                }
                switch (Boards.Count)
                {
                    case 1:
                        if (Boards[0].Skip) return true;
                        if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out bool IsMux2WhenTest1Board))
                        {
                            return false;
                        }
                        DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                        StartDisChargeTime = DateTime.Now;
                        while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                        {
                            if (IsMux2WhenTest1Board)
                            {
                                _DMM.DMM1.GetValue();
                                if (_DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow)
                                {
                                    DisChargeItem2Pass = true;
                                    break;
                                }
                            }
                            else
                            {
                                _DMM.DMM2.GetValue();
                                if (_DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow)
                                {
                                    DisChargeItem2Pass = true;
                                    break;
                                }
                            }
                        }
                        break;

                    case 2:
                        if (Boards[0].Skip && Boards[1].Skip) return true;
                        if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out _))
                        {
                            return false;
                        }
                        if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 2, out _))
                        {
                            return false;
                        }
                        DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                        StartDisChargeTime = DateTime.Now;
                        while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                        {
                            _DMM.DMM1.GetValue();
                            _DMM.DMM2.GetValue();
                            if (!Boards[0].Skip && !Boards[1].Skip)
                            {
                                DisChargeItem2Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                    & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                if (DisChargeItem2Pass) break;
                            }
                            else if (!Boards[0].Skip)
                            {
                                DisChargeItem2Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                if (DisChargeItem2Pass) break;
                            }
                            else if (!Boards[1].Skip)
                            {
                                DisChargeItem2Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                if (DisChargeItem2Pass) break;
                            }
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip || !Boards[1].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out _))
                            {
                                return false;
                            }
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 2, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                _DMM.DMM2.GetValue();
                                if (!Boards[0].Skip && !Boards[1].Skip)
                                {
                                    DisChargeItem2Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                        & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem2Pass) break;
                                }
                                else if (!Boards[0].Skip)
                                {
                                    DisChargeItem2Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem2Pass) break;
                                }
                                else if (!Boards[1].Skip)
                                {
                                    DisChargeItem2Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem2Pass) break;
                                }
                            }
                        }

                        if (!Boards[2].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 3, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                if (_DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow)
                                {
                                    DisChargeItem2Pass &= true;
                                    break;
                                }
                            }
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip || !Boards[1].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out _))
                            {
                                return false;
                            }
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 2, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                _DMM.DMM2.GetValue();
                                if (!Boards[0].Skip && !Boards[1].Skip)
                                {
                                    DisChargeItem2Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                        & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem2Pass) break;
                                }
                                else if (!Boards[0].Skip)
                                {
                                    DisChargeItem2Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem2Pass) break;
                                }
                                else if (!Boards[1].Skip)
                                {
                                    DisChargeItem2Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem2Pass) break;
                                }
                            }
                        }

                        if (!Boards[3].Skip || !Boards[2].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 3, out _))
                            {
                                return false;
                            }
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 4, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                _DMM.DMM2.GetValue();
                                if (!Boards[2].Skip && !Boards[3].Skip)
                                {
                                    DisChargeItem2Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                        & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem2Pass) break;
                                }
                                else if (!Boards[2].Skip)
                                {
                                    DisChargeItem2Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem2Pass) break;
                                }
                                else if (!Boards[3].Skip)
                                {
                                    DisChargeItem2Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem2Pass) break;
                                }
                            }
                        }
                        break;
                }
            }

            if (TestModel.Discharge.DischargeItem3 != 0 && DisChargeItem2Pass) // check none discharge mode
            {
                switch (TestModel.Discharge.DischargeItem3)
                {
                    case 1:
                        _DMM.SetModeDC(DMM.DMM_DCV_Range.DC1000V, DMM.DMM_Rate.MID);
                        break;

                    case 2:
                        _DMM.SetModeAC(DMM.DMM_ACV_Range.AC750V, DMM.DMM_Rate.MID);
                        break;

                    default:
                        break;
                }
                switch (Boards.Count)
                {
                    case 1:
                        if (Boards[0].Skip) return true;
                        if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out bool IsMux2WhenTest1Board))
                        {
                            return false;
                        }
                        DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                        StartDisChargeTime = DateTime.Now;
                        while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                        {
                            if (IsMux2WhenTest1Board)
                            {
                                _DMM.DMM1.GetValue();
                                if (_DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow)
                                {
                                    DisChargeItem3Pass = true;
                                    break;
                                }
                            }
                            else
                            {
                                _DMM.DMM2.GetValue();
                                if (_DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow)
                                {
                                    DisChargeItem3Pass = true;
                                    break;
                                }
                            }
                        }
                        break;

                    case 2:
                        if (Boards[0].Skip && Boards[1].Skip) return true;
                        if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out _))
                        {
                            return false;
                        }
                        if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 2, out _))
                        {
                            return false;
                        }
                        DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                        StartDisChargeTime = DateTime.Now;
                        while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                        {
                            _DMM.DMM1.GetValue();
                            _DMM.DMM2.GetValue();
                            if (!Boards[0].Skip && !Boards[1].Skip)
                            {
                                DisChargeItem3Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                    & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                if (DisChargeItem3Pass) break;
                            }
                            else if (!Boards[0].Skip)
                            {
                                DisChargeItem3Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                if (DisChargeItem3Pass) break;
                            }
                            else if (!Boards[1].Skip)
                            {
                                DisChargeItem3Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                if (DisChargeItem3Pass) break;
                            }
                        }
                        break;

                    case 3:
                        if (!Boards[0].Skip || !Boards[1].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out _))
                            {
                                return false;
                            }
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 2, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                _DMM.DMM2.GetValue();
                                if (!Boards[0].Skip && !Boards[1].Skip)
                                {
                                    DisChargeItem3Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                        & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem3Pass) break;
                                }
                                else if (!Boards[0].Skip)
                                {
                                    DisChargeItem3Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem3Pass) break;
                                }
                                else if (!Boards[1].Skip)
                                {
                                    DisChargeItem3Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem3Pass) break;
                                }
                            }
                        }

                        if (!Boards[2].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 3, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                if (_DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow)
                                {
                                    DisChargeItem3Pass &= true;
                                    break;
                                }
                            }
                        }
                        break;

                    case 4:
                        if (!Boards[0].Skip || !Boards[1].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 1, out _))
                            {
                                return false;
                            }
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 2, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                _DMM.DMM2.GetValue();
                                if (!Boards[0].Skip && !Boards[1].Skip)
                                {
                                    DisChargeItem3Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                        & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem3Pass) break;
                                }
                                else if (!Boards[0].Skip)
                                {
                                    DisChargeItem3Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem3Pass) break;
                                }
                                else if (!Boards[1].Skip)
                                {
                                    DisChargeItem3Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem3Pass) break;
                                }
                            }
                        }

                        if (!Boards[3].Skip || !Boards[2].Skip)
                        {
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 3, out _))
                            {
                                return false;
                            }
                            if (!SetBoardMux(String.Format("{0}/{1}", TestModel.Discharge.Item1ChannelP, TestModel.Discharge.Item1ChannelN), 4, out _))
                            {
                                return false;
                            }
                            DelayAfterMuxSellect(_DMM.Mode, _DMM.Rate);
                            StartDisChargeTime = DateTime.Now;
                            while (DateTime.Now.Subtract(StartDisChargeTime).TotalMilliseconds < TestModel.Discharge.DischargeTime)
                            {
                                _DMM.DMM1.GetValue();
                                _DMM.DMM2.GetValue();
                                if (!Boards[2].Skip && !Boards[3].Skip)
                                {
                                    DisChargeItem3Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow
                                        & _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem3Pass) break;
                                }
                                else if (!Boards[2].Skip)
                                {
                                    DisChargeItem3Pass = _DMM.DMM1.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem3Pass) break;
                                }
                                else if (!Boards[3].Skip)
                                {
                                    DisChargeItem3Pass = _DMM.DMM2.LastDoubleValue < TestModel.Discharge.Item1VoltageBelow;
                                    if (DisChargeItem3Pass) break;
                                }
                            }
                        }
                        break;
                }
            }

            return DisChargeItem1Pass & DisChargeItem2Pass & DisChargeItem3Pass;
        }

        private void PCB(Step step)
        {
            List<string> BoardSellect = step.Condition1.Split(',').ToList();
            foreach (var item in Boards)
            {
                item.Skip = true;
            }
            foreach (var item in BoardSellect)
            {
                switch (item)
                {
                    case "1":
                        if (Boards.Count > 1) Boards[0].Skip = false;
                        break;

                    case "2":
                        if (Boards.Count > 2) Boards[1].Skip = false;
                        break;

                    case "3":
                        if (Boards.Count > 3) Boards[2].Skip = false;
                        break;

                    case "4":
                        if (Boards.Count > 4) Boards[3].Skip = false;
                        break;

                    default:
                        break;
                }
            }
        }

        private void STL(Step step)
        {
            if (!Level.SerialPort.Port.IsOpen)
            {
                FunctionsParameterError("Sys", step);
                return;
            }

            if (!Int32.TryParse(step.Condition2, out int totalTime))
            {
                if (string.IsNullOrEmpty(step.Condition2))
                {
                    totalTime = -1;
                }
                else
                {
                    FunctionsParameterError("Condition2", step);
                    return;
                }
            }

            int sample_time = 0;
            if (!Int32.TryParse(step.Oper, out sample_time))
            {
                if (string.IsNullOrEmpty(step.Oper))
                {
                    sample_time = 100;
                    step.Oper = "100";
                }
                else
                {
                    FunctionsParameterError("Oper", step);
                    return;
                }
            }

            Level.StartGetSample(sample_time);
            if (!(totalTime == -1))
            {
                LevelChannel arbitrary_channel = Boards[0].LevelChannels.Where(channel => channel.IsUse == true).ToList().FirstOrDefault();
                if (arbitrary_channel != null)
                {
                    while (arbitrary_channel.Samples.Count * sample_time < totalTime)
                    {
                        // Do nothing, it's blocking;
                    }
                    Level.StopGetSample();
                }
            }

            bool SetOK = true;

            for (int board_idx = 0; board_idx < Boards.Count; board_idx++)
            {
                var Board = Boards[board_idx];

                if (!Board.Skip)
                {
                    if (!SetOK)
                    {
                        step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "Sys");
                        step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ng);
                    }
                    else
                    {
                        step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "exe");
                        step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ok);
                    }
                }
            }
        }

        private void EDL(Step step)
        {
            if (!Level.SerialPort.Port.IsOpen)
            {
                FunctionsParameterError("Sys", step);
                return;
            }
            Level.StopGetSample();
            bool SetOK = true;

            for (int board_idx = 0; board_idx < Boards.Count; board_idx++)
            {
                var Board = Boards[board_idx];

                if (!Board.Skip)
                {
                    if (!SetOK)
                    {
                        step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "Sys");
                        step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ng);
                    }
                    else
                    {
                        step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "exe");
                        step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ok);
                    }
                }
            }
        }

        private void LCC(Step step)
        {
            if (!Level.SerialPort.Port.IsOpen)
            {
                FunctionsParameterError("Sys", step);
                return;
            }

            int channel = 0;
            if (!Int32.TryParse(step.Condition1, out channel))
            {
                FunctionsParameterError("Condition1", step);
                return;
            }
            channel = channel - 1;

            if (channel >= Boards[0].LevelChannels.Count())
            {
                FunctionsParameterError("Condition1", step);
                return;
            }

            int skip_samples = 0;
            if (!Int32.TryParse(step.Condition2, out skip_samples))
            {
                if (string.IsNullOrEmpty(step.Condition2))
                {
                    skip_samples = 0;
                }
                else
                {
                    FunctionsParameterError("Condition2", step);
                    return;
                }
            }

            bool IsHigh = step.Oper.Contains("H");

            for (int board_idx = 0; board_idx < Boards.Count; board_idx++)
            {
                var Board = Boards[board_idx];
                if (!Board.Skip)
                {
                    List<LevelSample> samples = Board.LevelChannels[channel].Samples.Skip(skip_samples).ToList();

                    if (samples.Count > 0)
                    {
                        if (samples.Where(x => x.Level != IsHigh).Count() > 0)
                        {
                            var failChannels = samples.Where(x => x.Level != IsHigh).ToList();
                            for (int i = 0; i < failChannels.Count; i++)
                            {
                                Console.WriteLine("{0}->{1}", i, failChannels[i].Level);
                            }

                            step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "NG");
                            step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ng);
                        }
                        else
                        {
                            step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "OK");
                            step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ok);
                        }
                    }
                    else
                    {
                        step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "NG");
                        step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ng);
                    }
                }
            }
        }

        private void LEC(Step step)
        {
            if (!Level.SerialPort.Port.IsOpen)
            {
                FunctionsParameterError("Sys", step);
                return;
            }

            int channel;
            if (!Int32.TryParse(step.Condition1, out channel))
            {
                FunctionsParameterError("Condition1", step);
                return;
            }
            channel = channel - 1;

            if (channel >= Boards[0].LevelChannels.Count())
            {
                FunctionsParameterError("Condition1", step);
                return;
            }

            int spect;
            if (!Int32.TryParse(step.Spect, out spect))
            {
                FunctionsParameterError("Spect", step);
                return;
            }

            if (step.Oper != "H" && step.Oper != "L")
            {
                FunctionsParameterError("Oper", step);
                return;
            }

            bool IsHigh = step.Oper == "H";

            int skip_samples = 0;
            if (step.Condition2 != null)
            {
                if (step.Condition2.Length >= 1)
                {
                    if (!Int32.TryParse(step.Condition2, out skip_samples))
                    {
                        if (string.IsNullOrEmpty(step.Condition2))
                        {
                            skip_samples = 0;
                        }
                        else
                        {
                            FunctionsParameterError("Condition2", step);
                            return;
                        }
                    }
                }
            }

            for (int board_idx = 0; board_idx < Boards.Count; board_idx++)
            {
                Board board = Boards[board_idx];

                if (!board.Skip)
                {
                    int countA = board.LEVEL_COUNT(IsHigh, channel, skip_samples);
                    step.ValueGet1 = countA.ToString();

                    if (countA == spect)
                    {
                        step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "OK");
                        step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ok);
                    }
                    else
                    {
                        step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, countA.ToString());
                        step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ng);
                    }
                }
            }
        }

        private void LSQ(Step step)
        {
            if (!Level.SerialPort.Port.IsOpen)
            {
                FunctionsParameterError("Sys", step);
                return;
            }

            if (step.Oper != "H" && step.Oper != "L")
            {
                FunctionsParameterError("Oper", step);
                return;
            }

            bool level_set = step.Oper.Contains("H");

            int skip_samples;

            if (!Int32.TryParse(step.Condition2, out skip_samples))
            {
                if (string.IsNullOrEmpty(step.Condition2))
                {
                    skip_samples = 0;
                }
                else
                {
                    FunctionsParameterError("Condition2", step);
                    return;
                }
            }

            int std_channel;

            if (!Int32.TryParse(step.Spect, out std_channel))
            {
                FunctionsParameterError("Spect", step);
                return;
            }

            std_channel = std_channel - 1;

            if (std_channel >= Boards[0].LevelChannels.Count())
            {
                FunctionsParameterError("Spect", step);

                return;
            }

            List<String> Channel = new List<string>();
            if (step.Condition1.Contains("/"))
            {
                Channel = step.Condition1.Split('/').ToList();
            }
            else
            {
                Channel.Add(step.Condition1);
            }
            List<int> channel_idx_list_ordered = new List<int>();
            foreach (var item in Channel)
            {
                if (item.Contains('~'))
                {
                    int startChannel = Convert.ToInt32(item.Split('~')[0]);
                    int endChannel = Convert.ToInt32(item.Split('~')[1]);
                    for (int i = startChannel; i < endChannel; i++)
                    {
                        channel_idx_list_ordered.Add(i - 1);
                    }
                }
                else
                {
                    channel_idx_list_ordered.Add(Convert.ToInt32(item) - 1);
                }
            }

            if (channel_idx_list_ordered.Count >= 2)
            {
                for (int board_idx = 0; board_idx < Boards.Count; board_idx++)
                {
                    Board board = Boards[board_idx];

                    if (!board.Skip)
                    {
                        var samples_std_channel = board.LevelChannels[std_channel].Samples.Skip(skip_samples).ToList();

                        var std_change_point = board.FindChangePoint(samples_std_channel, level_set);

                        List<int> corresponding_change_points = new List<int>();
                        foreach (int channel in channel_idx_list_ordered)
                        {
                            var samples_channel = board.LevelChannels[channel].Samples;
                            var change_point = board.FindChangePoint(samples_channel.Skip(std_change_point).ToList(), level_set);
                            corresponding_change_points.Add(change_point);
                        }

                        bool all_increasing = corresponding_change_points.Zip(corresponding_change_points.Skip(1), (a, b) => b > a).All(x => x);

                        if (!all_increasing)
                        {
                            step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "NG");
                            step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ng);
                        }
                        else
                        {
                            step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "OK");
                            step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ok);
                        }
                    }
                }
            }
            else
            {
                FunctionsParameterError("Condition1", step);
            }
        }

        public void LTM(Step step)
        {
            int channel;
            if (!Int32.TryParse(step.Condition1, out channel))
            {
                FunctionsParameterError("Condition1", step);
                return;
            }

            channel = channel - 1;

            if (step.Oper != "HL" && step.Oper != "LH" && step.Oper != "LL" && step.Oper != "HH")
            {
                FunctionsParameterError("Oper", step);
                return;
            }

            int skip_samples;

            if (!Int32.TryParse(step.Condition2, out skip_samples))
            {
                if (string.IsNullOrEmpty(step.Condition2))
                {
                    skip_samples = 0;
                }
                else
                {
                    FunctionsParameterError("Condition2", step);
                    return;
                }
            }

            //int spect;
            //if (!Int32.TryParse(step.Spect, out spect))
            //{
            //    FunctionsParameterError("Spect", step);
            //    return;
            //}

            double max;
            if (!Double.TryParse(step.Max, out max))
            {
                if (string.IsNullOrEmpty(step.Max))
                {
                    max = Double.MaxValue;
                }
                else
                {
                    FunctionsParameterError("Max", step);
                    return;
                }
            }

            double min;
            if (!Double.TryParse(step.Min, out min))
            {
                FunctionsParameterError("Min", step);
                return;
            }

            for (int board_idx = 0; board_idx < Boards.Count; board_idx++)
            {
                Board board = Boards[board_idx];
                if (!board.Skip)
                {
                    var samples_channel = board.LevelChannels[channel].Samples.Skip(skip_samples).ToList();

                    bool flag_begin = true;

                    int duration = 0;

                    bool state_start = board.CharToBool(step.Oper[0]);
                    bool state_end = board.CharToBool(step.Oper[1]);

                    if (state_start != state_end)
                    {
                        for (int idx = 0; idx < samples_channel.Count; idx++)
                        {
                            if (flag_begin)
                            {
                                if (samples_channel[idx].Level == board.CharToBool(step.Oper[0]))
                                {
                                    flag_begin = false;
                                }
                            }
                            else
                            {
                                if (samples_channel[idx].Level == board.CharToBool(step.Oper[1]))
                                {
                                    duration++;
                                    break;
                                }
                                else
                                {
                                    duration++;
                                }
                            }
                        }
                    }
                    else
                    {
                        for (int idx = 0; idx < samples_channel.Count; idx++)
                        {
                            if (flag_begin)
                            {
                                if (samples_channel[idx].Level == board.CharToBool(step.Oper[0]))
                                {
                                    flag_begin = false;
                                }
                            }
                            else
                            {
                                if (samples_channel[idx].Level == board.CharToBool(step.Oper[0]))
                                {
                                    duration++;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }

                    if (duration < min || duration > max)
                    {
                        step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, duration.ToString());
                        step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ng);
                    }
                    else
                    {
                        step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "OK");
                        step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ok);
                    }
                }
            }
        }

        private void DLY(Step step)
        {
            bool SetOK = false;
            int dlyTime = 0;
            if (int.TryParse(step.Oper, out int delayTime))
            {
                dlyTime = delayTime;
                SetOK = true;
                if (delayTime > 100)
                {
                    int delay = 0;
                    while (delay + 100 <= delayTime)
                    {
                        Task.Delay(90).Wait();
                        delay += 100;
                        step.ValueGet1 = delay.ToString();
                        step.ValueGet2 = delay.ToString();
                        step.ValueGet3 = delay.ToString();
                        step.ValueGet4 = delay.ToString();
                    }
                }
                else
                {
                    Task.Delay(delayTime).Wait();
                }
            }

            for (int board_idx = 0; board_idx < Boards.Count; board_idx++)
            {
                var Board = Boards[board_idx];

                if (!Board.Skip)
                {
                    if (!SetOK)
                    {
                        step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "NG");
                        step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ng);
                    }
                    else
                    {
                        step.GetType().GetProperty($"ValueGet{board_idx + 1}").SetValue(step, "OK");
                        step.GetType().GetProperty($"Result{board_idx + 1}").SetValue(step, Step.Ok);
                    }
                }
            }
        }

        private void PWR(Step step)
        {
            bool IsON = step.Oper == "ON";
            bool Is220V = step.Condition1 == "220VAC";
            bool Is110V = step.Condition1 == "110VAC";

            if (!System.System_Board.SerialPort.Port.IsOpen)
            {
                FunctionsParameterError("Sys", step);
                return;
            }

            switch (Boards.Count)
            {
                case 1:
                    if (!Boards[0].Skip)
                    {
                        if (Is110V)
                            System.System_Board.MachineIO.AC110 = IsON;
                        if (Is220V)
                            System.System_Board.MachineIO.AC220 = IsON;
                        System.System_Board.SendControl();

                        step.ValueGet1 = step.Oper.ToString();
                        step.Result1 = Step.Ok;
                    }
                    break;

                case 2:
                    if (!Boards[0].Skip)
                    {
                        if (Is110V)
                            System.System_Board.MachineIO.AC110 = IsON;
                        if (Is220V)
                            System.System_Board.MachineIO.AC220 = IsON;

                        step.ValueGet1 = step.Oper.ToString();
                        step.Result1 = Step.Ok;
                    }

                    if (!Boards[1].Skip)
                    {
                        if (Is110V)
                            System.System_Board.MachineIO.BC110 = IsON;
                        if (Is220V)
                            System.System_Board.MachineIO.BC220 = IsON;

                        step.ValueGet2 = step.Oper.ToString();
                        step.Result2 = Step.Ok;
                    }

                    System.System_Board.SendControl();
                    break;

                case 3:
                    if (!Boards[0].Skip)
                    {
                        if (Is110V)
                            System.System_Board.MachineIO.AC110 = IsON;
                        if (Is220V)
                            System.System_Board.MachineIO.AC220 = IsON;

                        step.ValueGet1 = step.Oper.ToString();
                        step.Result1 = Step.Ok;
                    }

                    if (!Boards[1].Skip)
                    {
                        if (Is110V)
                            System.System_Board.MachineIO.BC110 = IsON;
                        if (Is220V)
                            System.System_Board.MachineIO.BC220 = IsON;

                        step.ValueGet2 = step.Oper.ToString();
                        step.Result2 = Step.Ok;
                    }
                    if (!Boards[2].Skip)
                    {
                        if (Is110V)
                            System.System_Board.MachineIO.AC110 = IsON;
                        if (Is220V)
                            System.System_Board.MachineIO.AC220 = IsON;

                        step.ValueGet3 = step.Oper.ToString();
                        step.Result3 = Step.Ok;
                    }
                    System.System_Board.SendControl();
                    break;

                case 4:
                    if (!Boards[0].Skip)
                    {
                        if (Is110V)
                            System.System_Board.MachineIO.AC110 = IsON;
                        if (Is220V)
                            System.System_Board.MachineIO.AC220 = IsON;

                        step.ValueGet1 = step.Oper.ToString();
                        step.Result1 = Step.Ok;
                    }

                    if (!Boards[1].Skip)
                    {
                        if (Is110V)
                            System.System_Board.MachineIO.BC110 = IsON;
                        if (Is220V)
                            System.System_Board.MachineIO.BC220 = IsON;

                        step.ValueGet2 = step.Oper.ToString();
                        step.Result2 = Step.Ok;
                    }
                    if (!Boards[2].Skip)
                    {
                        if (Is110V)
                            System.System_Board.MachineIO.AC110 = IsON;
                        if (Is220V)
                            System.System_Board.MachineIO.AC220 = IsON;

                        step.ValueGet3 = step.Oper.ToString();
                        step.Result3 = Step.Ok;
                    }

                    if (!Boards[4].Skip)
                    {
                        if (Is110V)
                            System.System_Board.MachineIO.BC110 = IsON;
                        if (Is220V)
                            System.System_Board.MachineIO.BC220 = IsON;

                        step.ValueGet4 = step.Oper.ToString();
                        step.Result4 = Step.Ok;
                    }
                    System.System_Board.SendControl();
                    break;

                default:
                    break;
            }
        }

        private void DIS(Step step)
        {
            if (System.System_Board.SerialPort.Port.IsOpen)
            {
                if (step.Condition1 == "ON")
                {
                    System.System_Board.MachineIO.ADSC = true;
                    System.System_Board.MachineIO.BDSC = true;
                }
                else
                {
                    System.System_Board.MachineIO.ADSC = false;
                    System.System_Board.MachineIO.BDSC = false;
                }

                System.System_Board.SendControl();
                if (Boards.Count >= 1) if (!Boards[0].Skip) step.ValueGet1 = "exe";
                if (Boards.Count >= 2) if (!Boards[1].Skip) step.ValueGet2 = "exe";
                if (Boards.Count >= 3) if (!Boards[2].Skip) step.ValueGet3 = "exe";
                if (Boards.Count >= 4) if (!Boards[3].Skip) step.ValueGet4 = "exe";
            }
            else
            {
                FunctionsParameterError("sys", step);
            }
        }

        public void MOT(Step step)
        {
            if (!PowerMetter.SerialPort.Port.IsOpen)
            {
                FunctionsParameterError("sys", step);
            }

            #region RPM

            if (step.Condition1 == "RPM")
            {
                double minValue = 0;
                double maxValue = 0;

                if (!Double.TryParse(step.Min, out minValue))
                {
                    if (step.Min == "")
                    {
                        minValue = Double.MinValue;
                    }
                    else
                    {
                        FunctionsParameterError("Min", step);
                        return;
                    }
                }

                if (!Double.TryParse(step.Max, out maxValue))
                {
                    if (step.Max == "")
                    {
                        maxValue = Double.MaxValue;
                    }
                    else
                    {
                        FunctionsParameterError("Max", step);
                        return;
                    }
                }

                List<float> RPMs = new List<float>();

                List<float> RPMsScan = new List<float>();

                int channel = 1;
                if (!Int32.TryParse(step.Oper, out channel))
                {
                    FunctionsParameterError("oper", step);
                    return;
                }
                else
                {
                    if (channel > 2)
                    {
                        FunctionsParameterError("oper > 2", step);
                        return;
                    }
                    if (channel <= 0)
                    {
                        FunctionsParameterError("oper <= 0", step);
                        return;
                    }
                }

                string spin_direction_set = step.Condition2;

                if (int.TryParse(step.Spect, out int scanTime))
                {
                    int currentDirection = 0;
                    int previousDirection = 0;
                    bool initialChecking = true;

                    bool ACW_CW_result = false;
                    bool CW_result = false;
                    bool ACW_result = false;

                    DateTime start = DateTime.Now;
                    while (DateTime.Now.Subtract(start).TotalMilliseconds < scanTime)
                    {
                        if (BoardExtension.ReadRPMs(out RPMs))
                        {
                            float currentRPM = RPMs[channel - 1];
                            RPMsScan.Add(currentRPM);

                            if (spin_direction_set == "CW")
                            {
                                step.ValueGet1 = currentRPM.ToString();
                                if (GetSign(currentRPM) == 1 && minValue <= currentRPM && maxValue >= currentRPM)
                                {
                                    CW_result = true;
                                    // exit executing immediately !!!
                                    break;
                                }
                            }
                            if (spin_direction_set == "ACW")
                            {
                                step.ValueGet1 = currentRPM.ToString();
                                if (GetSign(currentRPM) == -1 && minValue <= currentRPM && maxValue >= currentRPM)
                                {
                                    ACW_result = true;
                                    // exit executing immediately !!!
                                    break;
                                }
                            }

                            if (spin_direction_set == "CW/ACW")
                            {
                                if (initialChecking)
                                {
                                    currentDirection = GetSign(currentRPM);
                                    previousDirection = currentDirection;

                                    if (currentDirection != 0)
                                    {
                                        initialChecking = false;
                                    }
                                }
                                else
                                {
                                    currentDirection = GetSign(currentRPM);
                                    if (currentDirection != previousDirection)
                                    {
                                        ACW_CW_result = true;
                                        // exit executing immediately !!!
                                        break;
                                    }
                                    previousDirection = currentDirection;
                                }
                            }
                        }
                        else
                        {
                            FunctionsParameterError("sys", step);
                            return;
                        }
                    }

                    if (RPMsScan.All(item => item == 0))
                    {
                        step.ValueGet1 = "No Rotation";
                        step.Result1 = Step.Ng;
                        return;
                    }
                    if (spin_direction_set == "CW")
                    {
                        if (CW_result)
                        {
                            step.ValueGet1 = "OK";
                            step.Result1 = Step.Ok;
                            return;
                        }
                        else
                        {
                            step.ValueGet1 = "NG";
                            step.Result1 = Step.Ng;
                            return;
                        }
                    }
                    if (spin_direction_set == "ACW")
                    {
                        if (ACW_result)
                        {
                            step.Result1 = Step.Ok;
                            return;
                        }
                        else
                        {
                            step.Result1 = Step.Ng;
                            return;
                        }
                    }
                    if (spin_direction_set == "CW/ACW")
                    {
                        if (ACW_CW_result)
                        {
                            step.ValueGet1 = "OK";
                            step.Result1 = Step.Ok;
                        }
                        else
                        {
                            step.ValueGet1 = "NG";
                            step.Result1 = Step.Ng;
                        }
                        return;
                    }
                }
                else
                {
                    FunctionsParameterError("Spect", step);
                    return;
                }
            }

            #endregion RPM

            #region READ

            if (step.Condition1 == "READ")
            {
                foreach (var powerMettterValueHolder in PowerMetter.ValueHolders)
                {
                    powerMettterValueHolder.ClearValueCollection();
                }
                if (int.TryParse(step.Spect, out int scanTime))
                {
                    DateTime start = DateTime.Now;
                    while (DateTime.Now.Subtract(start).TotalMilliseconds < scanTime)
                    {
                        if (Boards.Count >= 1) if (!Boards[0].Skip) if (PowerMetter.Read('A')) step.ValueGet1 = "exe"; else { step.ValueGet1 = "sys"; step.Result1 = Step.Ng; }
                        if (Boards.Count >= 2) if (!Boards[1].Skip) if (PowerMetter.Read('B')) step.ValueGet2 = "exe"; else { step.ValueGet2 = "sys"; step.Result2 = Step.Ng; }
                        if (Boards.Count >= 3) if (!Boards[2].Skip) if (PowerMetter.Read('C')) step.ValueGet3 = "exe"; else { step.ValueGet3 = "sys"; step.Result3 = Step.Ng; }
                        if (Boards.Count >= 4) if (!Boards[3].Skip) if (PowerMetter.Read('D')) step.ValueGet4 = "exe"; else { step.ValueGet4 = "sys"; step.Result4 = Step.Ng; }
                    }
                }
            }
            else
            {
                double minValue = 0;
                double maxValue = 0;

                if (!Double.TryParse(step.Min, out minValue))
                {
                    if (step.Min == "")
                    {
                        minValue = Double.MinValue;
                    }
                    else
                    {
                        FunctionsParameterError("Min", step);
                        return;
                    }
                }

                if (!Double.TryParse(step.Max, out maxValue))
                {
                    if (step.Max == "")
                    {
                        maxValue = Double.MaxValue;
                    }
                    else
                    {
                        FunctionsParameterError("Max", step);
                        return;
                    }
                }

                //"READ", "CMP UU", "CMP UW", "CMP UV", "CMP UUW", "CMP UWV", "CMP UVU", "CMP IU", "CMP IW", "CMP IV"
                switch (step.Condition1)
                {
                    case "CMP Voltage U":

                        if (Boards.Count >= 1)
                        {
                            step.ValueGet1 = PowerMetter.ValueHolders.Select(x => x.Voltage_U_Collection).ToArray()[0].Max().ToString("N2");

                            if (CheckStepMinMax(step, PowerMetter.ValueHolders.Select(x => x.Voltage_U_Collection).ToArray()[0], minValue, maxValue))
                            {
                                step.Result1 = Step.Ok;
                            }
                            else
                            {
                                step.Result1 = Step.Ng;
                            }
                        }
                        break;

                    case "CMP Voltage W":
                        if (Boards.Count >= 1)
                        {
                            step.ValueGet1 = PowerMetter.ValueHolders.Select(x => x.Voltage_W_Collection).ToArray()[0].Max().ToString("N2");

                            if (CheckStepMinMax(step, PowerMetter.ValueHolders.Select(x => x.Voltage_W_Collection).ToArray()[0], minValue, maxValue))
                            {
                                step.Result1 = Step.Ok;
                            }
                            else
                            {
                                step.Result1 = Step.Ng;
                            }
                        }

                        break;

                    case "CMP Voltage V":
                        if (Boards.Count >= 1)
                        {
                            step.ValueGet1 = PowerMetter.ValueHolders.Select(x => x.Voltage_V_Collection).ToArray()[0].Max().ToString("N2");

                            if (CheckStepMinMax(step, PowerMetter.ValueHolders.Select(x => x.Voltage_V_Collection).ToArray()[0], minValue, maxValue))
                            {
                                step.Result1 = Step.Ok;
                            }
                            else
                            {
                                step.Result1 = Step.Ng;
                            }
                        }
                        break;

                    case "CMP Voltage UW":
                        if (Boards.Count >= 1)
                        {
                            step.ValueGet1 = PowerMetter.ValueHolders.Select(x => x.Voltage_UW_Collection).ToArray()[0].Max().ToString("N2");
                            if (CheckStepMinMax(step, PowerMetter.ValueHolders.Select(x => x.Voltage_UW_Collection).ToArray()[0], minValue, maxValue))
                            {
                                step.Result1 = Step.Ok;
                            }
                            else
                            {
                                step.Result1 = Step.Ng;
                            }
                        }
                        break;

                    case "CMP Voltage WV":
                        if (Boards.Count >= 1)
                        {
                            step.ValueGet1 = PowerMetter.ValueHolders.Select(x => x.Voltage_WV_Collection).ToArray()[0].Max().ToString("N2");
                            if (CheckStepMinMax(step, PowerMetter.ValueHolders.Select(x => x.Voltage_WV_Collection).ToArray()[0], minValue, maxValue))
                            {
                                step.Result1 = Step.Ok;
                            }
                            else
                            {
                                step.Result1 = Step.Ng;
                            }
                        }
                        break;

                    case "CMP Voltage VU":
                        if (Boards.Count >= 1)
                        {
                            step.ValueGet1 = PowerMetter.ValueHolders.Select(x => x.Voltage_VU_Collection).ToArray()[0].Max().ToString("N2");
                            if (CheckStepMinMax(step, PowerMetter.ValueHolders.Select(x => x.Voltage_VU_Collection).ToArray()[0], minValue, maxValue))
                            {
                                step.Result1 = Step.Ok;
                            }
                            else
                            {
                                step.Result1 = Step.Ng;
                            }
                        }
                        break;

                    case "CMP Current U":
                        if (Boards.Count >= 1)
                        {
                            step.ValueGet1 = PowerMetter.ValueHolders.Select(x => x.Current_U_Collection).ToArray()[0].Max().ToString("N2");
                            if (CheckStepMinMax(step, PowerMetter.ValueHolders.Select(x => x.Current_U_Collection).ToArray()[0], minValue, maxValue))
                            {
                                step.Result1 = Step.Ok;
                            }
                            else
                            {
                                step.Result1 = Step.Ng;
                            }
                        }
                        break;

                    case "CMP Current W":
                        if (Boards.Count >= 1)
                        {
                            step.ValueGet1 = PowerMetter.ValueHolders.Select(x => x.Current_W_Collection).ToArray()[0].Max().ToString("N2");

                            if (CheckStepMinMax(step, PowerMetter.ValueHolders.Select(x => x.Current_W_Collection).ToArray()[0], minValue, maxValue))
                            {
                                step.Result1 = Step.Ok;
                            }
                            else
                            {
                                step.Result1 = Step.Ng;
                            }
                        }
                        break;

                    case "CMP Current V":
                        if (Boards.Count >= 1)
                        {
                            step.ValueGet1 = PowerMetter.ValueHolders.Select(x => x.Current_V_Collection).ToArray()[0].Max().ToString("N2");

                            if (CheckStepMinMax(step, PowerMetter.ValueHolders.Select(x => x.Current_V_Collection).ToArray()[0], minValue, maxValue))
                            {
                                step.Result1 = Step.Ng;
                            }
                            else
                            {
                                step.Result1 = Step.Ng;
                            }
                        }
                        break;

                    default:
                        FunctionsParameterError("condition", step);
                        break;
                }
            }

            #endregion READ
        }

        private int GetSign(float value)
        {
            int sign = 0;

            if (value < 0)
            {
                sign = -1;
            }
            if (value > 0)
            {
                sign = 1;
            }

            return sign;
        }

        private bool CheckStepMinMax(Step step, List<double> values, double minValue, double maxValue)
        {
            bool result = false;

            //step.ValueGet1 = value.ToString("N3");
            if (values.Max() >= minValue && values.Max() <= maxValue)
            {
                result = true;
            }

            return result;
        }

        public void END(Step step)
        {
            System.System_Board.PowerRelease();
            Relay.Card.Release();
            Solenoid.Card.Release();
            MuxCard.Card.ReleaseChannels();
        }

        private void FunctionsParameterError(string nameOfFunc, Step step)
        {
            switch (Boards.Count)
            {
                case 1:
                    if (!Boards[0].Skip)
                    {
                        step.ValueGet1 = nameOfFunc;
                        step.Result1 = Step.Ng;
                    }
                    break;

                case 2:
                    if (!Boards[0].Skip)
                    {
                        step.ValueGet1 = nameOfFunc;
                        step.Result1 = Step.Ng;
                    }

                    if (!Boards[1].Skip)
                    {
                        step.ValueGet2 = nameOfFunc;
                        step.Result2 = Step.Ng;
                    }
                    break;

                case 3:
                    if (!Boards[0].Skip)
                    {
                        step.ValueGet1 = nameOfFunc;
                        step.Result1 = Step.Ng;
                    }

                    if (!Boards[1].Skip)
                    {
                        step.ValueGet2 = nameOfFunc;
                        step.Result2 = Step.Ng;
                    }
                    if (!Boards[2].Skip)
                    {
                        step.ValueGet3 = nameOfFunc;
                        step.Result3 = Step.Ng;
                    }
                    break;

                case 4:
                    if (!Boards[0].Skip)
                    {
                        step.ValueGet1 = nameOfFunc;
                        step.Result1 = Step.Ng;
                    }

                    if (!Boards[1].Skip)
                    {
                        step.ValueGet2 = nameOfFunc;
                        step.Result2 = Step.Ng;
                    }
                    if (!Boards[2].Skip)
                    {
                        step.ValueGet3 = nameOfFunc;
                        step.Result3 = Step.Ng;
                    }
                    if (!Boards[3].Skip)
                    {
                        step.ValueGet4 = nameOfFunc;
                        step.Result4 = Step.Ng;
                    }
                    break;

                default:
                    break;
            }
        }

        #endregion Functions Code

        private bool StepTestResult(Step step)
        {
            bool isOk = true;
            switch (Boards.Count)
            {
                case 1:
                    if (!Boards[0].Skip) isOk = isOk && step.Result1 != Step.Ng;
                    return isOk;

                case 2:
                    if (!Boards[0].Skip) isOk = isOk && step.Result1 != Step.Ng;
                    if (!Boards[1].Skip) isOk = isOk && step.Result2 != Step.Ng;
                    return isOk;

                case 3:
                    if (!Boards[0].Skip) isOk = isOk && step.Result1 != Step.Ng;
                    if (!Boards[1].Skip) isOk = isOk && step.Result2 != Step.Ng;
                    if (!Boards[2].Skip) isOk = isOk && step.Result3 != Step.Ng;
                    return isOk;

                case 4:
                    if (!Boards[0].Skip) isOk = isOk && step.Result1 != Step.Ng;
                    if (!Boards[1].Skip) isOk = isOk && step.Result2 != Step.Ng;
                    if (!Boards[2].Skip) isOk = isOk && step.Result3 != Step.Ng;
                    if (!Boards[3].Skip) isOk = isOk && step.Result4 != Step.Ng;
                    return isOk;

                default:
                    return false;
            }
        }
    }
}