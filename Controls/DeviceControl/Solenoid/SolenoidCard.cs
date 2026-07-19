using Controls.DeviceControl.Camera;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

using Utility;
namespace Controls.DeviceControl
{
    public class SolenoidCard
    {
        public ObservableCollection<SolenoidChannel> Chanels { get; set; } = new ObservableCollection<SolenoidChannel>()
        {
            new SolenoidChannel { Channel_P = 1 },
            new SolenoidChannel { Channel_P = 2 },
            new SolenoidChannel { Channel_P = 3 },
            new SolenoidChannel { Channel_P = 4 },
            new SolenoidChannel { Channel_P = 5 },
            new SolenoidChannel { Channel_P = 6 },
            new SolenoidChannel { Channel_P = 7 },
            new SolenoidChannel { Channel_P = 8 },
            new SolenoidChannel { Channel_P = 9 },
            new SolenoidChannel { Channel_P = 10 },
            new SolenoidChannel { Channel_P = 11 },
            new SolenoidChannel { Channel_P = 12 },
            new SolenoidChannel { Channel_P = 13 },
            new SolenoidChannel { Channel_P = 14 },
            new SolenoidChannel { Channel_P = 15 },
            new SolenoidChannel { Channel_P = 16 },
            new SolenoidChannel { Channel_P = 17 },
            new SolenoidChannel { Channel_P = 18 },
            new SolenoidChannel { Channel_P = 19 },
            new SolenoidChannel { Channel_P = 20 },
            new SolenoidChannel { Channel_P = 21 },
            new SolenoidChannel { Channel_P = 22 },
            new SolenoidChannel { Channel_P = 23 },
            new SolenoidChannel { Channel_P = 24 }
        };

        private SerialPortDisplay _SerialPort;

        public SerialPortDisplay SerialPort
        {
            get { return _SerialPort; }
            set
            {
                if (value != null || value != _SerialPort)
                    _SerialPort = value;
            }
        }

        public SolenoidCard(WrapPanel panelSelect)
        {
            panelSelect.Children.Clear();
            foreach (var item in Chanels)
            {
                panelSelect.Children.Add(item.CbUse);
            }
        }

        public SolenoidCard()
        {
            foreach (var item in Chanels)
            {
                item.ManualStateChange += Item_ManualStateChange;
            }
        }

        public void Update()
        {
            foreach (var item in Chanels)
            {
                item.ManualStateChange -= Item_ManualStateChange;
                item.ManualStateChange += Item_ManualStateChange;
            }
        }

        private void Item_ManualStateChange(object sender, EventArgs e)
        {
            (sender as SolenoidChannel).ManualStateChange -= Item_ManualStateChange;
            bool changeOk = SendCardStatus2();
            (sender as SolenoidChannel).IsON = (sender as SolenoidChannel).IsON ? changeOk : !changeOk;
            (sender as SolenoidChannel).ManualStateChange += Item_ManualStateChange;
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
        }

        public void SelectAll()
        {
            foreach (var item in Chanels)
            {
                item.isUse = true;
            }
        }

        public void ClearAll()
        {
            foreach (var item in Chanels)
            {
                item.isUse = false;
            }
        }

        public void SetPort()
        {
        }

        public void Release()
        {
            foreach (var item in Chanels)
            {
                item.isOn = false;
            }
            SendCardStatus2();
        }

        private static readonly object _logLock = new object();
        private static void LogDiag(string message)
        {
            try
            {
                lock (_logLock)
                {
                    string dir = @"C:\log";
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(dir, $"diag_{DateTime.Now:yyyy-MM-dd}.txt"),
                        $"{DateTime.Now:HH:mm:ss.fff} [SOLENOID] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }

        public bool SendCardStatus()
        {
            byte[] cardChannel = new byte[5];

            cardChannel[0] = (byte)0x53;

            UInt32 data = 0;
            for (int i = Chanels.Count - 1; i >= 0; i--)
            {
                data = data << 1;
                if (Chanels[i].IsON)
                {
                    data |= 1;
                }
            }
            var bytes = BitConverter.GetBytes(data);
            for (int i = cardChannel.Length - 1; i >= 1; i--)
            {
                cardChannel[i] = bytes[cardChannel.Length - 1 - i];
            }

            if (SerialPort == null || SerialPort.Port == null || !SerialPort.Port.IsOpen)
            {
                LogDiag($"SendCardStatus FAIL - port not ready bitmask={data:X6}");
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                SerialPort.Port.DiscardInBuffer();
                var result = SerialPort.SendToControls(cardChannel, 500, new byte[] { 0x53, 0x00 });
                if (result)
                {
                    LogDiag($"SendCardStatus OK retry={i + 1} bitmask={data:X6}");
                    return true;
                }
            }

            LogDiag($"SendCardStatus FAIL after 3 retries bitmask={data:X6}");
            return false;


        }

        // --- Human-readable annotation of a SOLENOID frame for the log: which channels the 'S' command sets. ---
        // Frame: [44 45 06 53 data2 data3 data4 00 chk 56] - data2/3/4 = channels S01-08/09-16/17-24 (bit = channel).
        public static string AnnotateFrame(byte[] f, int n, bool isTx)
        {
            if (f == null || n < 5) return "";
            int cmd = (n >= 10 && f[2] == 0x06) ? f[3] : f[2];
            if (cmd != 0x53 || n < 7) return "";   // only the 'S' set-channel command carries channel data (ACK has none)

            var on = new List<string>();
            for (int b = 0; b < 8; b++) if ((f[4] & (1 << b)) != 0) on.Add("S" + (b + 1).ToString("D2"));    // S01-S08
            for (int b = 0; b < 8; b++) if ((f[5] & (1 << b)) != 0) on.Add("S" + (b + 9).ToString("D2"));    // S09-S16
            for (int b = 0; b < 8; b++) if ((f[6] & (1 << b)) != 0) on.Add("S" + (b + 17).ToString("D2"));   // S17-S24
            return "OUT> " + (on.Count == 0 ? "all off" : string.Join(",", on));
        }

        // Builds the fixed 10-byte solenoid frame per spec and sends it raw (no GetFrame wrapping):
        //   [0]=0x44 'D' [1]=0x45 'E' [2]=0x06 len [3]=0x53 'S'
        //   [4]=data2 S01-S08 [5]=data3 S09-S16 [6]=data4 S17-S24 [7]=0x00 reserved
        //   [8]=XOR checksum of [0]..[7] [9]=0x56 'V'
        public bool SendCardStatus2()
        {
            byte data2 = 0, data3 = 0, data4 = 0;
            foreach (var ch in Chanels)
            {
                if (!ch.IsON) continue;
                int idx = (ch.Channel_P - 1) % 24; // 0..23
                int bit = idx % 8;
                switch (idx / 8)
                {
                    case 0: data2 |= (byte)(1 << bit); break; // S01-S08
                    case 1: data3 |= (byte)(1 << bit); break; // S09-S16
                    case 2: data4 |= (byte)(1 << bit); break; // S17-S24
                }
            }

            byte[] frame = new byte[10];
            frame[0] = 0x44; // 'D'
            frame[1] = 0x45; // 'E'
            frame[2] = 0x06; // payload length
            frame[3] = 0x53; // 'S'
            frame[4] = data2;
            frame[5] = data3;
            frame[6] = data4;
            frame[7] = 0x00; // reserved
            byte chk = 0x00;
            for (int i = 0; i <= 7; i++) chk ^= frame[i];
            frame[8] = chk;
            frame[9] = 0x56; // 'V'

            if (SerialPort == null || SerialPort.Port == null || !SerialPort.Port.IsOpen)
            {
                LogDiag($"SendCardStatus2 FAIL - port not ready data=[{data2:X2} {data3:X2} {data4:X2}]");
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                SerialPort.Port.DiscardInBuffer();
                if (SerialPort.SendRawFrame(frame, 500))
                {
                    LogDiag($"SendCardStatus2 OK retry={i + 1} data=[{data2:X2} {data3:X2} {data4:X2}]");
                    return true;
                }
            }

            LogDiag($"SendCardStatus2 FAIL after 3 retries data=[{data2:X2} {data3:X2} {data4:X2}]");
            return false;
        }
    }
}