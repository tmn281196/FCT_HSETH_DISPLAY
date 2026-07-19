using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace VTMTester
{
    /// <summary>
    /// Interaction logic for SplashScreen.xaml
    /// </summary>
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
            // Splash at 50% of the screen, centred (WindowStartupLocation=CenterScreen uses this size). Set here so
            // it adapts to whatever monitor it runs on; the Viewbox scales the 1920x1080 content to fit.
            Width = SystemParameters.PrimaryScreenWidth / 2;
            Height = SystemParameters.PrimaryScreenHeight / 2;
            // Get Version from the global AppInfo - edit in one place, applied everywhere
            lbVersion.Content = "Version " + AppInfo.Version;
        }
    }
}
