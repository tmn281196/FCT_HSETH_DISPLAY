using Controls.DeviceControl;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Markup;
using System.Windows.Threading;

namespace Controls.Devices_Control
{
    public class BoardExtension
    {

        public string Name = "BoardExtension";

        public SevenSegment SevenSegment { get; set; }
        private SerialPortDisplay serialPort { get; set; }
        public SerialPortDisplay SerialPort
        {
            get { return serialPort; }
            set
            {
                serialPort = value;
            }
        }

        public byte byteControl =  0;

        private SysIOControl SysIOcontrol { get; set; }



        public DispatcherTimer Sampling = new DispatcherTimer()
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };


        public BoardExtension(SysIOControl pSysIOControl)
        {

            SevenSegment = new SevenSegment(this);
            serialPort = new SerialPortDisplay();


            SysIOcontrol = pSysIOControl;
            SerialPort.DeviceName = this.Name;
            SerialPort.BlinkTime = 50;
            SerialPort.Port = new SerialPort()
            {
                PortName = "COM1",
                BaudRate = 115200,
                ReadTimeout = 500,
            };

            Sampling.Tick += Sampling_Tick;

        }
        public async void CheckCommunication(string pComName)
        {
            try
            {
                SerialPort.DeviceName = this.Name;
                SerialPort.BlinkTime = 50;
                SerialPort.Port = new SerialPort()
                {
                    PortName = pComName,
                    BaudRate = 115200,
                    ReadTimeout = 500,
                };
                SerialPort.Port.Open();
                SerialPort.OpenPort();
            }
            catch (Exception e)
            {
                Utility.Debug.Write(Name + ": " + e.Message, Utility.Debug.ContentType.Error);
            }
        }

        public bool ReadRPMs(out List<float> RPMs)
        {
            RPMs = new List<float>(4) { 0, 0, 0, 0 };
            if (SerialPort.SendAndRead(new byte[] { 0x50 }, 0x50, 1000, out byte[] Response))
            {
                for (int i = 0; i < 4; i++)
                {
                    RPMs[i] = ConvertToFloat(Response, i * 4 + 4);
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        public float ConvertToFloat(byte[] value, int index)
        {
            float f = 0;
            byte[] bytes = { value[index], value[index + 1], value[index + 2], value[index + 3] };
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes); // Convert big endian to little endian
            }
            f = BitConverter.ToSingle(bytes, 0);
            return f;
        }


        private void Sampling_Tick(object sender, EventArgs e)
        {
            if (SysIOcontrol.EnableGetSoundData)
            {
                if (SysIOcontrol.System_Board.MachineIO.SamplesMicA.Count < 1000)
                {
                    GetSampleMic();
                }
                else
                {
                    SysIOcontrol.EnableGetSoundData = false;
                }
            }
        }


        public bool SendKey(List<int> keyList, bool level )
        {
            byte byteControl = (byte)0;

            for (int i = 0; i < keyList.Count; i++) {

                int bitInByteIndex = keyList[i];
                byte mask = (byte)(1 << bitInByteIndex);
                if (level)
                {
                    byteControl = (byte)(byteControl | mask);
                }
                else
                {
                    byteControl = (byte)(byteControl &  ~mask);
                }
            }
            return SerialPort.SendToControls(new byte[] { byteControl }, 500, new byte[] { 0x52, 0x00 });
        }

        public void GetSampleMic()
        {
            byte[] Response;

            if (SerialPort.Port.IsOpen)
            {
                if (SerialPort.SendAndRead(new byte[] { 0x49 }, 0x49, 1500, out Response))
                {
                    if (Response.Length == 10)
                    {
                        SysIOcontrol.System_Board.MachineIO.MIC_A = (Response[5] << 8) | Response[4];
                        SysIOcontrol.System_Board.MachineIO.MIC_B = (Response[7] << 8) | Response[6];

                        SysIOcontrol.System_Board.MachineIO.SamplesMicA.Add(SysIOcontrol.System_Board.MachineIO.MIC_A);
                        SysIOcontrol.System_Board.MachineIO.SamplesMicB.Add(SysIOcontrol.System_Board.MachineIO.MIC_B);
                    }
                }
            }
        }
    }
}
