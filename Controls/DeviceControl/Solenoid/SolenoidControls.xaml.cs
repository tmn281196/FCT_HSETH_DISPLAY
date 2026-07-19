using Controls.DeviceControl;
using Controls.DevicesControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Controls
{
    /// <summary>
    /// Interaction logic for RelayControls.xaml
    /// </summary>
    public partial class SolenoidControls : UserControl
    {

        private SerialPortDisplay _SerialPort = new SerialPortDisplay();
        public SerialPortDisplay SerialPort
        {
            get { return _SerialPort; }
            set
            {
                if (value != null || value != _SerialPort)
                    _SerialPort = value;
            }
        }

        public BoardExtension BoardExtension;

        private SolenoidCard _Card = new SolenoidCard();
        public SolenoidCard Card
        {
            get { return _Card; }
            set
            {
                if (value != null || value != _Card)
                {
                    _Card = value;
                    _Card.SerialPort = _SerialPort;
                    this.DataContext = _Card;
                    CardChannelPanel.Children.Clear();
                    foreach (var item in _Card.Chanels)
                    {
                        CardChannelPanel.Children.Add(item.btOn);
                    }
                    Card.Update();
                }
            }
        }


        public SolenoidControls()
        {
            InitializeComponent();
            SerialPort.DeviceName = "SOLENOID";   // full name for the settings UI
            SerialPort.LogTag = "SOL";            // short tag for the log
            SerialPort.Port = new System.IO.Ports.SerialPort
            {
                PortName = "COM1",
                BaudRate = 9600,
                DataBits = 8,
                Parity = System.IO.Ports.Parity.None,
                StopBits = System.IO.Ports.StopBits.One
            };

            _Card.SerialPort = _SerialPort;
         
            this.DataContext = _Card;
            CardChannelPanel.Children.Clear();
            foreach (var item in _Card.Chanels)
            {
                CardChannelPanel.Children.Add(item.btOn);
            }
            Card.Update();
        }

        public async void CheckCardComunication(string PortName)
        {
            byte[] cardChannel = new byte[5];

            cardChannel[0] = (byte)0x53;

            byte[] cardResponse = SystemComunication.GetFrame(new byte[] { 0x00 });
            await SerialPort.CheckBoardComPort(PortName, 9600, cardChannel, cardResponse, 250);
        }
        public bool SetChannels(List<int> channels, Boolean IsOn)
        {
            foreach (var item in channels)
            {
                Card.Chanels[item - 1].isOn = IsOn;
            }
            return Card.SendCardStatus();
        }

        // Same as SetChannels but sends the fixed 10-byte frame with spec byte order (data2/3/4 = S01-08/09-16/17-24).
        public bool SetChannels2(List<int> channels, Boolean IsOn)
        {
            foreach (var item in channels)
            {
                Card.Chanels[item - 1].isOn = IsOn;
            }
            return Card.SendCardStatus2();
        }
    }
}
