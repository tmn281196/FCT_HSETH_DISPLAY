using VTMControls;
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

namespace VTMControls.DeviceControl
{
    /// <summary>
    /// Interaction logic for UUTPortConfig.xaml
    /// </summary>
    public partial class UUTPortConfig : UserControl
    {

        private UUT_Config _UUT_config = new UUT_Config();
        public UUT_Config UUT_config
        {
            get { return _UUT_config; }
            set
            {
                if (_contentLoaded)
                {
                    if (value != null || value != _UUT_config)
                    {
                        _UUT_config = value;
                        this.DataContext = _UUT_config;
                    }
                }
            }
        }
        public UUTPortConfig()
        {
            InitializeComponent();

            this.DataContext = _UUT_config;
        }

    }
}
