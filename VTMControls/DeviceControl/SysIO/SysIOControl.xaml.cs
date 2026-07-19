using VTMControls.DeviceControl;
using VTMControls.DeviceControl;
using Microsoft.Office.Interop.Excel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace VTMControls.DeviceControl
{
    /// <summary>
    /// Interaction logic for SYSIOcontrol.xaml
    /// </summary>
    public partial class SysIOControl : UserControl
    {
        private SystemBoard _System_Board = new SystemBoard();

        public SystemBoard System_Board
        {
            get { return _System_Board; }
            set
            {
                if (value != null || value != _System_Board)
                    _System_Board = value;
                this.DataContext = System_Board.MachineIO;
            }
        }

        private BoardExtension _BoardExtension;

        public BoardExtension BoardExtension
        {
            get { return _BoardExtension; }
            set
            {
                if (value != null || value != _BoardExtension)
                    _BoardExtension = value;
            }
        }
        public SysIOControl()
        {
            InitializeComponent();
            AddExtension();
            this.DataContext = System_Board.MachineIO;
        }

        private void AddExtension()
        {
            _BoardExtension = new BoardExtension(this);
        }

        private bool _EnableGetSoundData = false;

        public bool EnableGetSoundData
        {
            get { return _EnableGetSoundData; }
            set
            {
                if (value != _EnableGetSoundData) _EnableGetSoundData = value;

                if (_EnableGetSoundData == true)
                {
                    System_Board.MachineIO.ClearSamples();
                    _BoardExtension.Sampling.Start();
                }
            }
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            System_Board.SendControl();
        }

        public void StartRecordMic(double Interval)
        {
            this.Dispatcher.Invoke(new System.Action(() =>
            {
                _BoardExtension.Sampling.Interval = TimeSpan.FromMilliseconds(Interval);
                EnableGetSoundData = true;
            }));
        }

        public void StopRecordMic()
        {
            this.Dispatcher.Invoke(new System.Action(() =>
            {
                EnableGetSoundData = false;
            }));
        }

    }
}
