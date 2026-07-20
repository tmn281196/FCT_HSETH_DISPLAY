using VTMBase;
using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Linq;
using System.Collections.Generic;
using VTMUtility;
using System.Windows;
using VTMControls;
using VTMControls.DeviceControl;

namespace VTMBase
{
    // Owns every device instance the tester drives (serial cards, camera, printer, scanner, mic).
    // Split out of the Program partial class so device ownership is one named type; Program holds it as a
    // component (Program.Devices) and re-exposes each device as a thin proxy property, so the ~900 existing
    // call sites (Program.System, Program._DMM, ...) keep working unchanged. See Devices\Program.cs.
    //
    // This class is deliberately hardware-only: it constructs the devices and can close their ports. Anything
    // that needs the app settings or the test state machine (CheckComnunication, the MachineIO_On* handlers)
    // stays on Program, because it depends on appSetting / TestState / IsTestting / pageActive.
    public class ProgramDevices
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

        // Sound processing. The mic hardware itself is SoundTester.Mic (a DeviceControl.Microphone);
        // SoundTester on top of it is pure DSP / test logic.
        public SoundTester SoundTester { get; set; }

        public ProgramDevices()
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
            SoundTester = new SoundTester();
        }

        // Close every port before re-checking communications. Ports whose device is not flagged "Use" in the
        // settings are simply never re-opened afterwards, so they stay closed.
        public void CloseAllPorts()
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

            UUTs[0].serial.Port?.Close();
            UUTs[1].serial.Port?.Close();
            UUTs[2].serial.Port?.Close();
            UUTs[3].serial.Port?.Close();
        }
    }
}
