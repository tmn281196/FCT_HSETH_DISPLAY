using VTMBase;
using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Linq;
using System.Collections.Generic;
using Utility;
using System.Windows;
using Controls;
using Controls.DevicesControl;
using Controls.DevicesControl;
using Controls.DeviceControl;

namespace VTMBase
{
    public partial class Program
    {
        // Device list
        public GWIN_TECH_DMM _DMM { get; set; }

        public MuxCardControl MuxCard { get; set; }
        public RelayControls Relay { get; set; }
        public LevelDataViewer Level { get; set; }
        public SolenoidControls Solenoid { get; set; }
        public VisionTester VisionTester { get; set; }
        public SysIOControl System { get; set; }
        public PowerMetter PowerMetter { get; set; }
        public BoardExtension BoardExtension { get; set; }
        public CameraControl Capture { get; set; }
        public SerialPortDisplay BarcodeReader { get; set; }
        public List<UUTPort> UUTs { get; set; }
        public Printer_QR Printer { get; set; }

        // Sound processing (placeholder - se ghep WASAPI capture + FFT sau)
        public SoundTester SoundTester { get; set; } = new SoundTester();

        public Program()
        {
            System = new SysIOControl();
            PowerMetter = new PowerMetter();
            VisionTester = new VisionTester();
            Level = new LevelDataViewer();
            Relay = new RelayControls();
            MuxCard = new MuxCardControl();
            _DMM = new GWIN_TECH_DMM();
            BarcodeReader = new SerialPortDisplay();

            BoardExtension = System.BoardExtension;
            // Extension Board to be authorized
            Solenoid = new SolenoidControls();

            UUTs = new List<UUTPort>(){
                new UUTPort(){
                    serial = new SerialPortDisplay()
                    {
                        DeviceName = "UUT 1",
                    }
                },
                new UUTPort(){
                    serial = new SerialPortDisplay()
                    {
                        DeviceName = "UUT 2",
                    }
                },
                new UUTPort(){
                    serial = new SerialPortDisplay()
                    {
                        DeviceName = "UUT 3",
                    }
                },
                new UUTPort(){
                    serial = new SerialPortDisplay()
                    {
                        DeviceName = "UUT 4",
                    }
                }
            };

            Printer = new Printer_QR();
        }

        // Check device comunications

        public async void CheckComnunication()
        {
            System.System_Board.SerialPort.Port?.Close();
            MuxCard.SerialPort1.Port?.Close();
            MuxCard.SerialPort2.Port?.Close();
            _DMM.DMM1.SerialPort.Port?.Close();
            _DMM.DMM2.SerialPort.Port?.Close();
            Relay.SerialPort.Port?.Close();
            Level.SerialPort.Port?.Close();
            Solenoid.SerialPort.Port?.Close();
            BarcodeReader.Port?.Close();
            PowerMetter.SerialPort.Port?.Close();
            BoardExtension.SerialPort.Port?.Close();

            BarcodeReader.Port?.Close();

            UUTs[0].serial.Port?.Close();
            UUTs[1].serial.Port?.Close();
            UUTs[2].serial.Port?.Close();
            UUTs[3].serial.Port?.Close();
            await Task.Delay(50);

            // Only connect the ports flagged "Use". Un-used ports were already closed above and stay closed.
            var comm = appSetting.Communication;

            // Annotate SYS (system board output/input signals) and SOL (solenoid channels) frames in the log.
            Utility.Debug.FrameAnnotator = (dev, f, n, tx) =>
                  dev == "SYS" ? Controls.SystemBoard.AnnotateFrame(f, n, tx)
                : dev == "SOL" ? Controls.SolenoidCard.AnnotateFrame(f, n, tx)
                : "";

            // TX/RX serial traffic is logged only for the system board and the solenoid board.
            if (System?.System_Board?.SerialPort != null)
            {
                System.System_Board.SerialPort.LogTxRx = true;
                if (string.IsNullOrEmpty(System.System_Board.SerialPort.DeviceName))
                    System.System_Board.SerialPort.DeviceName = "SystemBoard";
            }
            if (Solenoid?.SerialPort != null)
            {
                Solenoid.SerialPort.LogTxRx = true;
                if (string.IsNullOrEmpty(Solenoid.SerialPort.DeviceName))
                    Solenoid.SerialPort.DeviceName = "Solenoid";
            }

            //System board checking
            if (comm.SystemIOUse)
            {
                System.System_Board.CheckCardComunication(comm.SystemIOPort);
                await Task.Delay(50);
            }
            System.System_Board.MachineIO.OnStartRequest += MachineIO_OnStartRequest;
            System.System_Board.MachineIO.OnManualStartRequest += MachineIO_OnManualStartRequest;
            System.System_Board.MachineIO.OnCancleRequest += MachineIO_OnCancleRequest;
            System.System_Board.MachineIO.OnDoorStateChange += MachineIO_OnDoorStateChange;

            // Analog Extension port checking
            if (comm.BoardExtensionUse)
            {
                BoardExtension.CheckCommunication(comm.BoardExtensionPort);
                await Task.Delay(50);
            }

            ////Mux card Checking
            //MuxCard.CheckCard1Comunication(appSetting.Communication.Mux1Port);
            //await Task.Delay(50);
            //MuxCard.CheckCard2Comunication(appSetting.Communication.Mux2Port);
            //await Task.Delay(50);

            //// DMM check
            //_DMM.DMM1.CheckCommunication(appSetting.Communication.DMM1Port);
            //await Task.Delay(50);
            //_DMM.DMM2.CheckCommunication(appSetting.Communication.DMM2Port);
            //await Task.Delay(50);

            // RELAY CHECK
            if (comm.RelayUse)
            {
                Relay.CheckCardComunication(comm.RelayPort);
                await Task.Delay(50);
            }

            if (comm.LevelUse)
            {
                Level.CheckCardComunication(comm.LevelPort);
                await Task.Delay(50);
            }

            if (comm.SolenoidUse)
            {
                Solenoid.CheckCardComunication(comm.SolenoidPort);
                //Solenoid.SerialPort.Port = BoardExtension.SerialPort.Port;
                await Task.Delay(100);
            }

            //Power metter check
            if (comm.PowerMetterUse)
            {
                PowerMetter.CheckCommunication(comm.PowerMetterPort);
                await Task.Delay(50);
            }

            // Barcode scand
            if (comm.ScannerUse)
            {
                CheckBarcodeReader(comm.ScannerPort);
                await Task.Delay(50);
            }

            // UUTs
            if (comm.UUT1Use) { UUTs[0].CheckPort(comm.UUT1Port); await Task.Delay(50); }
            if (comm.UUT2Use) { UUTs[1].CheckPort(comm.UUT2Port); await Task.Delay(50); }
            if (comm.UUT3Use) { UUTs[2].CheckPort(comm.UUT3Port); await Task.Delay(50); }
            if (comm.UUT4Use) { UUTs[3].CheckPort(comm.UUT4Port); await Task.Delay(50); }
        }

        private void MachineIO_OnDoorStateChange(object sender, EventArgs e)
        {
            if (System.System_Board.MachineIO.IsDoorOpen)
            {
                Debug.Write("Machine door open.", Debug.ContentType.Warning);
            }
        }

        private void MachineIO_OnCancleRequest(object sender, EventArgs e)
        {
            DiagLog.Write("CANCEL", $"IsTestting={IsTestting} TestState={TestState}");
            // Only a run that is genuinely in progress can be cancelled. Once it has CONCLUDED (GOOD/FAIL) the
            // result must stand: a PASS raises the cylinder, which drops SS_DOWN by design, and that release
            // must never overwrite the result with STOP. Likewise nothing to cancel before it starts (READY/WAIT).
            lock (_stateLock)
            {
                if (IsTestting && (TestState == RunTestState.TESTTING || TestState == RunTestState.PAUSE))
                {
                    TestState = RunTestState.STOP;
                    IsTestting = false;
                    DiagLog.Write("CANCEL", "→ STOP, IsTestting=false");
                }
            }
        }

        private void MachineIO_OnStartRequest(object sender, EventArgs e)
        {
            DiagLog.Write("START_REQ", $"IsTestting={IsTestting} IsloadModel={IsloadModel} page={pageActive} TestState={TestState}");
            ResultPanel.Dispatcher.Invoke(new Action(() =>
            ResultPanel.Visibility = Visibility.Hidden));
            // Only a press made while the machine is actually waiting for one counts. A press during GOOD/FAIL/STOP
            // - i.e. while the previous result is still on screen - must NOT be banked and then auto-fire the
            // instant READY arrives; the operator has to press again once the machine is ready for it.
            // WAIT stays allowed: that is exactly how the "pressed with no barcode loaded" branch notices the
            // press and pulses the jig back up.
            if (!IsTestting && IsloadModel
                && (TestState == RunTestState.READY || TestState == RunTestState.WAIT))
            {
                if (pageActive == PageActive.AutoPage)
                {
                    IsTestting = true;
                    DiagLog.Write("START_REQ", "→ IsTestting=true");
                }
            }
            else
            {
                DiagLog.Write("START_REQ", "ignored - machine not waiting for a press");
            }
        }


        private void MachineIO_OnManualStartRequest(object sender, EventArgs e)
        {
            DiagLog.Write("MANUAL_START", $"IsTestting={IsTestting} IsloadModel={IsloadModel} page={pageActive} TestState={TestState}");
            if (!IsTestting && IsloadModel)
            {
                if (pageActive == PageActive.ManualPage)
                {
                    IsTestting = true;
                    DiagLog.Write("MANUAL_START", "→ IsTestting=true");
                }
            }
        }
    }
}