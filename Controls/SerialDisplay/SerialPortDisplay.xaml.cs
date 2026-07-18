using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Runtime.CompilerServices;
using System.Timers;
using Utility;
using System.Threading;
using Timer = System.Timers.Timer;

namespace Controls
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class SerialPortDisplay : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public CancellationTokenSource _shutDown = new CancellationTokenSource();

        public event EventHandler SerialDataReciver;

        private string deviceName;

        public string DeviceName
        {
            get { return deviceName; }
            set
            {
                if (deviceName != value)
                {
                    deviceName = value;
                    NotifyPropertyChanged(nameof(DeviceName));
                }
            }
        }

        // Per-port TX/RX logging: only ports that opt in (SystemBoard, Solenoid) write serial traffic to the log.
        public bool LogTxRx { get; set; } = false;

        // Short tag shown in the LOG only (keeps DeviceName full for the settings UI). Falls back to DeviceName.
        public string LogTag { get; set; }

        // Name used for TX/RX + connection log lines: the short LogTag, else DeviceName, else the COM port.
        public string LogName => !string.IsNullOrEmpty(LogTag) ? LogTag
                               : (!string.IsNullOrEmpty(deviceName) ? deviceName : (Port?.PortName ?? portName ?? "COM"));

        private void LogTx(byte[] data) { if (LogTxRx) Utility.Debug.Tx(LogName, data); }
        private void LogRx(byte[] data) { if (LogTxRx) Utility.Debug.Rx(LogName, data); }
        private void LogTxStr(string s)
        {
            if (LogTxRx)
                Utility.Debug.Write(LogName + " >> FCT \"" + (s ?? "").Replace("\r", "\\r").Replace("\n", "\\n") + "\"", Utility.Debug.ContentType.Tx);
        }

        private string portName;

        public string PortName
        {
            get { return portName; }
            set
            {
                if (portName != value)
                {
                    portName = value;
                    NotifyPropertyChanged(nameof(PortName));
                }
            }
        }

        private bool _BufferReceiver = false;

        public bool BufferReceiver
        {
            get
            {
                bool lastStatus = _BufferReceiver;
                _BufferReceiver = false;
                return lastStatus;
            }
            set
            {
                if (value != _BufferReceiver)
                {
                    _BufferReceiver = value;
                }
            }
        }

        public List<byte> BytesReciever = new List<byte>();

        private Timer rxTimer = new Timer()
        {
            Interval = 25,
        };

        private Timer txTimer = new Timer()
        {
            Interval = 25,
        };

        public double BlinkTime
        {
            get { return txTimer.Interval; }
            set
            {
                if (value != txTimer.Interval)
                {
                    txTimer.Interval = value;
                    rxTimer.Interval = value;
                }
            }
        }

        private SerialPort port = new SerialPort();

        public SerialPort Port
        {
            get { return port; }
            set
            {
                if (port != null && port.PortName != value.PortName)
                {
                    try
                    {
                        port.Close();
                    }
                    catch (Exception)
                    {
                    }
                    port = value;
                    port.DataReceived += Port_DataReceived;
                }
            }
        }

        public SerialPortDisplay()
        {
            InitializeComponent();
            txTimer.Elapsed += TxTimer_Tick;
            rxTimer.Elapsed += RxTimer_Tick;

            txTimer.Enabled = true;
            rxTimer.Enabled = true;

            this.DataContext = this;
        }

        private void RxTimer_Tick(object sender, EventArgs e)
        {
            if (!this._shutDown.IsCancellationRequested)
            {
                Rx.Dispatcher.Invoke(new Action(() =>
            {
                Rx.Fill = new SolidColorBrush(Color.FromRgb(198, 198, 198));
            }), DispatcherPriority.Normal);
                rxTimer.Stop();
            }
        }

        private void TxTimer_Tick(object sender, EventArgs e)
        {
            if (!this._shutDown.IsCancellationRequested)
            {
                Tx.Dispatcher.Invoke(new Action(() =>
                        {
                            Tx.Fill = new SolidColorBrush(Color.FromRgb(198, 198, 198));
                        }), DispatcherPriority.Normal);
                txTimer.Stop();
            }
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            BufferReceiver = true;
            if (!this._shutDown.IsCancellationRequested)
            {
                Rx.Dispatcher.Invoke(new Action(() =>
                {
                    Rx.Fill = new SolidColorBrush(Colors.LightGreen);
                    rxTimer.Start();
                }));
            }
            SerialDataReciver?.Invoke(sender, null);
        }

        public void OpenPort()
        {
            if (!Port.IsOpen)
            {
                lbPortName.ToolTip = Port.PortName;
                Open.Fill = new SolidColorBrush(Color.FromRgb(198, 198, 198));
            }
            else
            {
                lbPortName.ToolTip = Port.PortName;
                Open.Fill = new SolidColorBrush(Color.FromRgb(5, 247, 5));
            }
        }

        public void IsClosed()
        {
            Open.Dispatcher.Invoke(new Action(() =>
            {
                Open.Fill = new SolidColorBrush(Colors.Gray);
            }), DispatcherPriority.Normal);
        }

        public void SerialSend()
        {
            Tx.Dispatcher.Invoke(new Action(() =>
            {
                Tx.Fill = new SolidColorBrush(Colors.Yellow);
                txTimer.Start();
            }), DispatcherPriority.Normal);
        }

        public void SendString(string str)
        {
            if (Port.IsOpen)
            {
                Port.Write(str);
                LogTxStr(str);
                Tx.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Tx.Fill = new SolidColorBrush(Colors.LightYellow);
                    txTimer.Start();
                }), DispatcherPriority.Normal);
            }
            else
            {
                Open.Dispatcher.Invoke(new Action(() =>
                {
                    Open.Fill = new SolidColorBrush(Colors.Gray);
                }), DispatcherPriority.Normal);
            }
        }

        public string ReadLine()
        {
            if (port.IsOpen)
            {
                try
                {
                    string str = port.ReadLine();
                    return str;
                }
                catch (TimeoutException)
                {
                    return "ERROR";
                }
                catch
                {
                    return "ERROR";
                }
            }
            return "ERROR";
        }

        private readonly object _txLock = new object();

        public void SendBytes(byte[] buf, bool logTx = true)
        {
            if (Port == null) { return; }
            Tx.Dispatcher.Invoke(new Action(() =>
            {
                Tx.Fill = new SolidColorBrush(Colors.Yellow);
                txTimer.Start();
            }), DispatcherPriority.Normal);
            try
            {
                if (Port.IsOpen)
                {
                    // Serialize writes: the state machine, the UI, and the 500ms input heartbeat can all send at once.
                    lock (_txLock) { Port.Write(buf, 0, buf.Length); }
                    if (logTx) LogTx(buf);
                }
            }
            catch (Exception)
            {
            }
        }

        public void SendToControls(byte[] buf, bool IsNoSize = false)
        {
            if (Port == null) { return; }
            Tx.Dispatcher.Invoke(new Action(() =>
            {
                Tx.Fill = new SolidColorBrush(Colors.Yellow);
                txTimer.Start();
            }), DispatcherPriority.Normal);
            try
            {
                if (Port.IsOpen)
                {
                    var frame = SystemComunication.GetFrame(buf, IsNoSize);
                    Port.Write(frame, 0, frame.Length);
                    LogTx(frame);
                }
            }
            catch (Exception)
            {
            }
        }

        public bool SendToControls(byte[] buf, int TimeOut, byte[] lookResponse)
        {
            if (Port == null || !Port.IsOpen)
            {
                // Ex = "Port not found";
                return false;
            }

            List<byte> frame = new List<byte>();
            try
            {
                if (Port.IsOpen)
                {
                    SerialSend();

                    var frameToSend = SystemComunication.GetFrame(buf);
                    Port.Write(frameToSend, 0, frameToSend.Length);
                    LogTx(frameToSend);
                    var start = DateTime.Now;

                    while (DateTime.Now.Subtract(start).TotalMilliseconds < TimeOut)
                    {
                        if (Port.BytesToRead >= 5)
                        {
                            byte startByte = (byte)Port.ReadByte();

                            if (startByte == SystemComunication.Prefix1)
                            {
                                var secondByte = (byte)Port.ReadByte();

                                if (secondByte == SystemComunication.Prefix2)
                                {
                                    frame.Clear();
                                    frame.Add(startByte);
                                    frame.Add(secondByte);
                                    for (int i = 0; i < 4; i++)
                                    {
                                        frame.Add((byte)Port.ReadByte());
                                    }

                                    var result = frame.Count == 6;
                                    //var result = SystemComunication.GetResponse(lookResponse, frame.ToArray());
                                    LogRx(frame.ToArray());

                                    return result;
                                }
                            }
                        }
                    }
                    return false;
                }
                else
                {
                    // Ex = "Port not opened";
                    return false;
                }
            }
            catch (Exception)
            {
                //Ex = "Port exception: " + e.Message;
                return false;
            }
        }

        // Frame constants specific to SendRawFrame (independent, not shared with SystemComunication).
        private const byte RawHeader1 = 0x44; // 'D'
        private const byte RawHeader2 = 0x45; // 'E'
        private const byte RawFooter = 0x56;  // 'V'

        // Sends a fully pre-built frame as-is (no GetFrame wrapping) and waits for a 6-byte ACK.
        public bool SendRawFrame(byte[] frame, int TimeOut)
        {
            if (Port == null || !Port.IsOpen)
            {
                return false;
            }

            List<byte> response = new List<byte>();
            try
            {
                SerialSend();
                Port.Write(frame, 0, frame.Length);
                LogTx(frame);
                var start = DateTime.Now;

                while (DateTime.Now.Subtract(start).TotalMilliseconds < TimeOut)
                {
                    if (Port.BytesToRead >= 6)
                    {
                        byte startByte = (byte)Port.ReadByte();   // [0] header

                        if (startByte == RawHeader1)   // 0x44 'D'
                        {
                            var secondByte = (byte)Port.ReadByte();   // [1] mandatory

                            if (secondByte == RawHeader2)   // 0x45 'E'
                            {
                                response.Clear();
                                response.Add(startByte);
                                response.Add(secondByte);
                                for (int i = 0; i < 4; i++)   // [2][3] data, [4] data, [5] footer
                                {
                                    response.Add((byte)Port.ReadByte());
                                }

                                // [2][3][4] data - ignored; [5] must be 0x56 'V'
                                LogRx(response.ToArray());
                                return response.Count == 6 && response[5] == RawFooter;
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool SendAndRead(byte[] buf, byte function, int TimeOut, out byte[] Response, bool addRange = true, int size = 0)
        {
            if (Port == null || !Port.IsOpen)
            {
                // Ex = "Port not found";
                Response = new byte[0];
                return false;
            }
            else
            {
                List<byte> frame = new List<byte>();
                try
                {
                    if (Port.IsOpen)
                    {
                        SerialSend();
                        Port.DiscardInBuffer();
                        Port.ReadTimeout = TimeOut;
                        var frameToSend = SystemComunication.GetFrame(buf);
                        Port.Write(frameToSend, 0, frameToSend.Length);
                        var start = DateTime.Now;
                        while (DateTime.Now.Subtract(start).TotalMilliseconds < TimeOut)
                        {
                        readData:
                            byte byteReaded = (byte)Port.ReadByte();
                            if (frame.Count > 0 || byteReaded == SystemComunication.Prefix1)
                            {
                                frame.Add(byteReaded);
                                if (byteReaded == 0x56)
                                {
                                    var secondByte = frame[1];
                                    if (secondByte == SystemComunication.Prefix2)
                                    {
                                        Response = frame.ToArray();
                                        foreach (var item in frame)
                                        {
                                            Console.Write(item.ToString("X2") + " ");
                                        }
                                        Port.DiscardInBuffer();
                                        return true;
                                    }
                                }
                                else
                                {
                                    goto readData;
                                }
                            }
                            else
                            {
                                goto readData;
                            }
                        }
                    }
                    foreach (var item in frame)
                    {
                        Console.Write(item.ToString("X2") + " ");
                    }
                    Response = frame.ToArray();
                    return false;
                }
                catch (Exception)
                {
                    //Ex = "Port exception: " + e.Message;
                    Response = frame.ToArray();
                    return false;
                }
            }
        }

        public bool SendAndRead(byte[] buf, int TimeOut, out List<byte> Response)
        {
            if (Port == null || !Port.IsOpen)
            {
                // Ex = "Port not found";
                Response = new List<byte>();
                return false;
            }
            else
            {
                List<byte> frame = new List<byte>();
                try
                {
                    if (Port.IsOpen)
                    {
                        SerialSend();
                        Port.DiscardInBuffer();
                        Port.Write(buf, 0, buf.Length);
                        Task.Delay(100).Wait();
                        var start = DateTime.Now;
                        while (DateTime.Now.Subtract(start).TotalMilliseconds < TimeOut)
                        {
                            //read address
                            byte byteReaded = (byte)Port.ReadByte();
                            frame.Add(byteReaded);

                            //read function
                            byteReaded = (byte)Port.ReadByte();
                            frame.Add(byteReaded);

                            //read size
                            byteReaded = (byte)Port.ReadByte();
                            frame.Add(byteReaded);

                            //read data
                            for (int i = 0; i < frame[2] + 2; i++)
                            {
                                byteReaded = (byte)Port.ReadByte();
                                frame.Add(byteReaded);

                                if (i == frame[2] + 1) goto Out;
                            }
                        }
                    }
                Out:
                    foreach (var item in frame)
                    {
                        Console.Write(item.ToString("X2") + " ");
                    }
                    Console.WriteLine();
                    Response = frame;
                    return true;
                }
                catch (Exception)
                {
                    //Ex = "Port exception: " + e.Message;
                    Response = frame;
                    foreach (var item in frame)
                    {
                        Console.Write(item.ToString("X2") + " ");
                    }
                    return false;
                }
            }
        }

        public async Task<bool> CheckComPort(string PORT_NAME, int baudrate, string DATA_ASK, string endFrame, int TimeOut, string DeviceName)
        {
            if (port != null)
            {
                if (port.IsOpen)
                {
                    port.Close();
                    var startClosePortTime = DateTime.Now;
                    while (port.IsOpen)
                    {
                        if (DateTime.Now.Subtract(startClosePortTime).TotalMilliseconds > 500)
                        {
                            break;
                        }
                    }
                }
            }

            PortName = PORT_NAME;
            port.PortName = PORT_NAME;
            port.BaudRate = baudrate;
            try
            {
                port.Open();
            }
            catch (Exception e)
            {
                OpenPort();
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                if (port.IsOpen)
                {
                    port.DataReceived -= Port_DataReceived;
                    port.DataReceived += Port_DataReceived;
                    port.ReadTimeout = TimeOut;
                    port.Write(DATA_ASK + "\r\n");
                    Console.WriteLine("Send:\t " + DATA_ASK);
                    Tx.Dispatcher.Invoke(new Action(() =>
                    {
                        Tx.Fill = new SolidColorBrush(Colors.Yellow);
                        txTimer.Start();
                    }), DispatcherPriority.Normal);
                    await Task.Delay(100);
                    string answerString = "No anwser";
                    try
                    {
                        answerString = port.ReadTo(endFrame);
                        Console.WriteLine("Response:\t " + answerString);

                        if (answerString != "No anwser")
                        {
                            OpenPort();
                            return true;
                        }
                    }
                    catch (Exception err)
                    {
                        Console.WriteLine(err.Message);
                    }
                }
            }
            if (port.IsOpen)
            {
                port.Close();
            }
            OpenPort();
            return false;
        }

        public async Task<bool> CheckBoardComPort(string PORT_NAME, int baudrate, byte[] DATA_ASK, byte[] returnFrame, int TimeOut, bool IsNoSize = false, bool IsRaw = false)
        {
            if (port != null)
            {
                if (port.IsOpen)
                {
                    port.Close();
                    var startClosePortTime = DateTime.Now;
                    while (port.IsOpen)
                    {
                        if (DateTime.Now.Subtract(startClosePortTime).TotalMilliseconds > 500)
                        {
                            break;
                        }
                    }
                }
            }

            PortName = PORT_NAME;
            port.PortName = PORT_NAME;
            port.BaudRate = baudrate;
            port.DataBits = 8;
            port.Parity = Parity.None;
            port.StopBits = StopBits.One;
            try
            {
                port.Open();
            }
            catch (Exception e)
            {
                OpenPort();
                return false;
            }

            for (int i = 0; i < 5; i++)
            {
                if (port.IsOpen)
                {
                    port.DataReceived -= Port_DataReceived;
                    port.DataReceived += Port_DataReceived;
                    port.ReadTimeout = TimeOut;
                    port.WriteTimeout = TimeOut;

                    if (IsRaw)
                    {
                        // DATA_ASK is a fully built frame (e.g. the v2 key-value output write) - send it as-is.
                        SerialSend();
                        port.Write(DATA_ASK, 0, DATA_ASK.Length);
                        LogTx(DATA_ASK);
                    }
                    else
                    {
                        SendToControls(DATA_ASK, IsNoSize);
                    }

                    Tx?.Dispatcher.Invoke(new Action(() =>
                    {
                        Tx.Fill = new SolidColorBrush(Colors.Yellow);
                        txTimer.Start();
                    }), DispatcherPriority.Normal);

                    Console.WriteLine(" ");
                    await Task.Delay(100);
                    DateTime start = DateTime.Now;
                    try
                    {
                        while (DateTime.Now.Subtract(start).TotalMilliseconds < TimeOut)
                        {
                            if (port.IsOpen)
                            {
                                if (port.BytesToRead > 3)
                                {
                                    byte[] bytes = new byte[port.BytesToRead];
                                    port.Read(bytes, 0, bytes.Length);
                                    var buffer = bytes.ToList();
                                    foreach (var item in bytes)
                                    {
                                        Console.Write(" {0:X}", item);
                                    }
                                    Console.WriteLine(" ");
                                    OpenPort();
                                    return true;
                                }
                            }
                        }
                    }
                    catch (Exception err)
                    {
                        Console.WriteLine(err.Message);
                    }
                }
            }
            if (port.IsOpen)
            {
                port.Close();
            }
            OpenPort();
            return false;
        }
    }
}