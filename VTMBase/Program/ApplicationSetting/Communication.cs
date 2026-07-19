using System;
using Controls.DeviceControl;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VTMBase
{
    public class Communication
    {
        //public string SystemIOPort { get; set; } = "COM5";
        //public string LevelPort { get; set; } = "COM8";
        //public string Mux1Port { get; set; } = "COM7";
        //public string Mux2Port { get; set; } = "COM10";
        //public string RelayPort { get; set; } = "COM9";
        //public string SolenoidPort { get; set; } = "COM3";
        //public string DMM1Port { get; set; } = "COM6";
        //public string DMM2Port { get; set; } = "COM4";
        //public string UUT1Port { get; set; } = "COM9";
        //public string UUT2Port { get; set; } = "COM10";
        //public string UUT3Port { get; set; } = "COM11";
        //public string UUT4Port { get; set; } = "COM12";

        public string SystemIOPort { get; set; } = "COM1";
        public string LevelPort { get; set; } = "COM2";
        public string Mux1Port { get; set; } = "COM3";
        public string Mux2Port { get; set; } = "COM4";
        public string RelayPort { get; set; } = "COM5";
        public string SolenoidPort { get; set; } = "COM6";
        public string DMM1Port { get; set; } = "COM7";
        public string DMM2Port { get; set; } = "COM8";
        public string PowerMetterPort { get; set; } = "COM9";
        public string BoardExtensionPort { get; set; } = "COM10";
        public string UUT1Port { get; set; } = "COM11";
        public string UUT2Port { get; set; } = "COM12";
        public string UUT3Port { get; set; } = "COM13";
        public string UUT4Port { get; set; } = "COM14";

        public string ScannerPort { get; set; } = "COM15";
        public int Scan_Baudrate { get; set; } = 115200;
        public int Scan_Databit { get; set; } = 8;
        public Parity Scan_Parity { get; set; } = Parity.None;

        public string PrinterPort { get; set; } = "COM16";

        // Microphone endpoint ID (WASAPI). Empty = default device.
        public string MicrophoneId { get; set; } = "";
        public string MicrophoneName { get; set; } = "";
        public bool MicrophoneUse { get; set; } = true;
        public bool CameraUse { get; set; } = true;

        // "Use" flags: if false -> the port is skipped in duplicate checks and not shown on the status bar.
        // Default true for backward compat with old config.
        public bool SystemIOUse { get; set; } = true;
        public bool LevelUse { get; set; } = true;
        public bool Mux1Use { get; set; } = true;
        public bool Mux2Use { get; set; } = true;
        public bool RelayUse { get; set; } = true;
        public bool SolenoidUse { get; set; } = true;
        public bool DMM1Use { get; set; } = true;
        public bool DMM2Use { get; set; } = true;
        public bool PowerMetterUse { get; set; } = true;
        public bool BoardExtensionUse { get; set; } = true;
        public bool UUT1Use { get; set; } = true;
        public bool UUT2Use { get; set; } = true;
        public bool UUT3Use { get; set; } = true;
        public bool UUT4Use { get; set; } = true;
        public bool ScannerUse { get; set; } = true;
        public bool PrinterUse { get; set; } = true;

        public string LogDirectory { get; set; } = "C:\\Log";

        public static List<string> ComPorts = new List<string>()
        {
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "COM10",

            "COM11",
            "COM12",
            "COM13",
            "COM14",
            "COM15",
            "COM16",
            "COM17",
            "COM18",
            "COM19",
            "COM20",

            "COM21",
            "COM22",
            "COM23",
            "COM24",
            "COM25",
            "COM26",
            "COM27",
            "COM28",
            "COM29",
            "COM30",

            "COM31",
            "COM32",
            "COM33",
            "COM34",
            "COM35",
            "COM36",
            "COM37",
            "COM38",
            "COM39",
            "COM40",

            "COM41",
            "COM42",
            "COM43",
            "COM44",
            "COM45",
            "COM46",
            "COM47",
            "COM48",
            "COM49",
            "COM50",

            "COM51",
            "COM52",
            "COM53",
            "COM54",
            "COM55",
            "COM56",
            "COM57",
            "COM58",
            "COM59",
            "COM60",

            "COM71",
            "COM72",
            "COM73",
            "COM74",
            "COM75",
            "COM76",
            "COM77",
            "COM78",
            "COM79",
            "COM80",

            "COM81",
            "COM82",
            "COM83",
            "COM84",
            "COM85",
            "COM86",
            "COM87",
            "COM88",
            "COM89",
            "COM90",

            "COM91",
            "COM92",
            "COM93",
            "COM94",
            "COM95",
            "COM96",
            "COM97",
            "COM98",
            "COM99",
            "COM100",
        };
    }
}