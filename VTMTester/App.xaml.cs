using VTMProgram;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;

namespace VTMTester
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private SplashScreen splashScreen = new SplashScreen();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;
            //initialize the splash screen and set it as the application main window
            this.MainWindow = splashScreen;
            splashScreen.Show();

            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();

            List<String> macAddrList = nics.Select(x => x.GetPhysicalAddress().ToString()).ToList();

            string[] whiteListMacAddress = {
                "00D861E3E550", //Dev's PC: 
                "D843AE12EB2A", //Dev's PC: 
                "D843AE12EC0A", //Dev's PC: 
                "D8BBC1B21D13", //Dev's PC: 
                "A8A15940E606", //PC1
                "F4B52038E9AD", //PC2
                "10FFE04AC71F", //PC3
                "10FFE04AC936",  //PC4
                "94E70BCB3021",  //PC5
                "94E70BCB301D",  //DEV5 (this PC)
                "D4F32D1F99D8"
            };

            Dictionary<string, string> mappingPCNameDictionary = new Dictionary<string, string>();

            mappingPCNameDictionary.Add("00D861E3E550", "DEV1");
            mappingPCNameDictionary.Add("D843AE12EB2A", "DEV2");
            mappingPCNameDictionary.Add("D843AE12EC0A", "DEV3");
            mappingPCNameDictionary.Add("D8BBC1B21D13", "DEV4");
            mappingPCNameDictionary.Add("94E70BCB3021", "DEV5");
            mappingPCNameDictionary.Add("94E70BCB301D", "DEV6");  

            mappingPCNameDictionary.Add("A8A15940E606", "FCT41"); // PC1
            mappingPCNameDictionary.Add("F4B52038E9AD", "FCT42"); // PC2
            mappingPCNameDictionary.Add("10FFE04AC71F", "FCT31"); // PC3 
            mappingPCNameDictionary.Add("10FFE04AC936", "FCT32"); // PC4
            mappingPCNameDictionary.Add("D4F32D1F99D8", "dan");


            string macKey = macAddrList.FirstOrDefault(x => whiteListMacAddress.Contains(x));

            if (macKey == null)
            {
                MessageBox.Show("This Program Cannot be Copied!!!", "ERROR", MessageBoxButton.OK, MessageBoxImage.Error);
                App.Current.Shutdown();
                return;
            }

            string PCName = mappingPCNameDictionary[macKey];
            VTMProgram.FolderMap.PCName = PCName;

            //in order to ensure the UI stays responsive, we need to
            //do the work on a different thread

            var mainWindow = new MainWindow();
            mainWindow.Loaded += MainWindow_Loaded;
            this.MainWindow = mainWindow;
            mainWindow.Show();
            mainWindow.WarningLogDirNotExist();
        }
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            splashScreen.Close();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Unhandled exception occurred: \n" + e.Exception.Message + "stacktrace" + e.Exception.StackTrace, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}