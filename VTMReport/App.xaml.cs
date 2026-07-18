using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;

namespace VTM_Report
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;
            //initialize the splash screen and set it as the application main window
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            String macAddr = string.Empty;
            foreach (NetworkInterface adapter in nics)
            {
                if (macAddr == String.Empty)
                {
                    macAddr = adapter.GetPhysicalAddress().ToString();
                }
            }

            if (macAddr != "10FFE04AC71F")
            {
                MessageBox.Show("This Program Cannot be Copied!!!", "ERROR", MessageBoxButton.OK, MessageBoxImage.Error);
                App.Current.Shutdown();
            }


            //in order to ensure the UI stays responsive, we need to
            //do the work on a different thread


        }
    }
}
