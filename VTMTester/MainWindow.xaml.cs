using Camera;
using VTMBase;
using VTMProgram;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace VTMTester
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [DllImport("Kernel32")]
        public static extern void FreeConsole();

        private AutoPage AutoPage = new AutoPage();
        private ManualPage ManualPage = new ManualPage();
        private ModelPage ModelPage = new ModelPage();
        private VisionPage VisionPage = new VisionPage();
        private SoundPage SoundPage = new SoundPage();
        private SettingPage SettingPage = new SettingPage();

        public DispatcherTimer Cleanning = new DispatcherTimer()
        {
            Interval = TimeSpan.FromMilliseconds(1000),
        };

        //
        public Program MainProgram = new Program();

        // Show / hide the status-bar log-directory warning. Replaces the old startup MessageBox: a popup is seen
        // once and dismissed, while a missing log directory stays broken until someone fixes the path - so the
        // warning has to stay on screen for as long as it is true.
        public void WarningLogDirNotExist()
        {
            string logDirectoryPath = MainProgram.appSetting.Communication.LogDirectory;
            bool missing = !string.IsNullOrWhiteSpace(logDirectoryPath) && !Directory.Exists(logDirectoryPath);

            if (LogDirWarnPanel == null) return;   // called before InitializeComponent (App startup ordering)

            if (missing)
            {
                lbLogDirWarn.Text = "LOG DIR NOT FOUND: " + logDirectoryPath;
                LogDirWarnPanel.ToolTip = "Test results cannot be exported.\r\n" + logDirectoryPath
                                        + "\r\nCheck the path in Settings.";
                LogDirWarnPanel.Visibility = Visibility.Visible;
            }
            else
            {
                LogDirWarnPanel.Visibility = Visibility.Collapsed;
            }
        }

        public MainWindow()
        {

            InitializeComponent();
            //ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(Int32.MaxValue));

            Cleanning.Tick += Cleanning_Tick;
            Cleanning.IsEnabled = true;

            AutoPageHolder.Content = AutoPage;
            ManualPageHolder.Content = ManualPage;
            ModelPageHolder.Content = ModelPage;
            VisionPageHolder.Content = VisionPage;
            SoundPageHolder.Content = SoundPage;
            SettingPageHolder.Content = SettingPage;

            MainProgram.Machine_Init();

            if (MainProgram.appSetting.Communication.LogDirectory != null)
            {
                MainProgram.AppFolder.LogDir = MainProgram.appSetting.Communication.LogDirectory;
            }
            SettingPage.txtBoxExcelFileDir.TextChanged += TxtBoxExcelFileDir_TextChanged;

            AutoPage.Program = MainProgram;
            ManualPage.Program = MainProgram;
            ModelPage.Program = MainProgram;
            VisionPage.Program = MainProgram;
            SoundPage.Program = MainProgram;
            SettingPage.Program = MainProgram;

            // App version from the global AppInfo, shown next to the Date time (right corner of the status bar)
            lbAppVersion.Text = "Version " + AppInfo.Version;

            // Bind cylinder IO indicators (SDOWN input, MainUP/MainDOWN output) to MachineIO
            CylinderIoPanel.DataContext = MainProgram.System.System_Board.MachineIO;

            // Communication panel: only Add devices with the "Use" tick in settings
            RefreshCommunicationPanel();

            //btSelectPage_Click(btAutoPage, null);
            //// binding camera source between 3 page
            Binding cameraBinding = new Binding("LastFrame")
            {
                Source = ManualPage.camera
            };
            AutoPage.cameraViewer.SetBinding(Image.SourceProperty, cameraBinding);
            VisionPage.cameraViewer.SetBinding(Image.SourceProperty, cameraBinding);

            AutoPage.cameraSetting.Capture = ManualPage.camera;
            ManualPage.cameraSetting.Capture = ManualPage.camera;
            VisionPage.cameraSetting.Capture = ManualPage.camera;
            MainProgram.Capture = ManualPage.camera;
            VisionPage.EditModel = ModelPage.EditModel;

            Binding stepsBinding = new Binding("Steps");
            stepsBinding.Source = AutoPage.TestModel;
            ManualPage.dgModelSteps.SetBinding(DataGrid.ItemsSourceProperty, stepsBinding);

            MainProgram.EditModel_OnSave += MainProgram_EditModel_OnSave;
            MainProgram.EditModel_OnLoaded += MainProgram_EditModel_OnLoaded;

            // Auto-open the most recent model if the path is still valid - silent when none
            TryAutoLoadLastModel();
        }

        // About dialog - style copied from ZEROC (HANSOL---ZEROC)
        private void btAbout_Click(object sender, RoutedEventArgs e)
        {
            var panel = new System.Windows.Controls.StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(20)
            };
            var logo = new System.Windows.Controls.Image
            {
                Width = 260,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(0, 0, 0, 14)
            };
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri("pack://application:,,,/img/TNG%20Logo.png", UriKind.Absolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                logo.Source = bmp;
            }
            catch { }
            panel.Children.Add(logo);
            panel.Children.Add(new TextBlock
            {
                Text = AppInfo.CompanyName + " Co., Ltd",
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Display Function Tester  v" + AppInfo.Version,
                FontSize = 12,
                Foreground = System.Windows.Media.Brushes.LightGray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Built: " + AppInfo.BuildDateString,
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.LightGray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });

            var win = new Window
            {
                Title = "About",
                Width = 480,
                Height = 320,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x00, 0x3a, 0x71)),
                Content = panel
            };

            var autoClose = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            autoClose.Tick += (s, ev) => { autoClose.Stop(); win.Close(); };
            win.Loaded += (s, ev) => autoClose.Start();
            win.Closed += (s, ev) => autoClose.Stop();

            win.ShowDialog();
        }

        private void TryAutoLoadLastModel()
        {
            try
            {
                var path = MainProgram?.appSetting?.LastModelPath;
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return;

                var str = AutoPage.LoadModel(path);
                if (str == null) return;

                ManualPage.TestModel = AutoPage.TestModel;
                MainProgram.SetBoards();
                MainProgram.EditModel = Utility.Extensions.ConvertFromJson<VTMBase.Model>(str);
                if (MainProgram.EditModel != null)
                {
                    MainProgram.EditModel.Path = path;
                    ModelPage.EditModel = MainProgram.EditModel;
                    VisionPage.EditModel = MainProgram.EditModel;
                    // Load the model's camera settings into the sliders AND push them to the camera (set on load).
                    var cs = MainProgram.EditModel.CameraSetting;
                    AutoPage.cameraSetting.ApplyModelSettings(cs);
                    ManualPage.cameraSetting.ApplyModelSettings(cs);
                    VisionPage.cameraSetting.ApplyModelSettings(cs);
                    if (cs != null)
                        Utility.Debug.Write("CAMERA: set from model (Bri=" + cs.Brightness + " Con=" + cs.Contrast
                            + " Exp=" + cs.Exposure + " Foc=" + cs.Focus + " WB=" + cs.WBTemperature + " Gain=" + cs.Gain + ")",
                            Utility.Debug.ContentType.Notify);
                }
            }
            catch
            {
                // Auto-load failed -> silently ignore, the user can still open a model manually
            }
        }

        private void TxtBoxExcelFileDir_TextChanged(object sender, TextChangedEventArgs e)
        {
            MainProgram.AppFolder.LogDir = SettingPage.txtBoxExcelFileDir.Text.ToString();
            // Re-check here so picking a good folder clears the status-bar warning without a restart, and picking
            // a bad one warns immediately instead of at the end of the next test.
            WarningLogDirNotExist();
        }

        private void Cleanning_Tick(object sender, EventArgs e)
        {
            //GC.Collect();
            // Day/month/2-digit-year + wall-clock time (dd/MM/yy HH:mm:ss). MM = month; mm = minutes.
            lbDateTime.Content = DateTime.Now.ToString("dd/MM/yy HH:mm:ss");
        }

        // Reload the device list on the status bar per the current Communication.<X>Use flags.
        // Called at init and after pressing Apply on SettingPage.
        public void RefreshCommunicationPanel()
        {
            if (MainProgram == null) return;
            var comm = MainProgram.appSetting.Communication;

            // Remove old serial devices (keep btCheckCommunications and CylinderIoPanel)
            var toRemove = new List<UIElement>();
            foreach (UIElement child in stackpanelComunication.Children)
            {
                if (child == btCheckCommunications) continue;
                if (child == CylinderIoPanel) continue;
                toRemove.Add(child);
            }
            foreach (var child in toRemove)
            {
                stackpanelComunication.Children.Remove(child);
            }

            // Re-add per the Use flags
            if (comm.SystemIOUse) stackpanelComunication.Children.Add(MainProgram.System.System_Board.SerialPort);
            if (comm.Mux1Use) stackpanelComunication.Children.Add(MainProgram.MuxCard.SerialPort1);
            if (comm.Mux2Use) stackpanelComunication.Children.Add(MainProgram.MuxCard.SerialPort2);
            if (comm.DMM1Use) stackpanelComunication.Children.Add(MainProgram._DMM.DMM1.SerialPort);
            if (comm.DMM2Use) stackpanelComunication.Children.Add(MainProgram._DMM.DMM2.SerialPort);
            if (comm.RelayUse) stackpanelComunication.Children.Add(MainProgram.Relay.SerialPort);
            if (comm.LevelUse) stackpanelComunication.Children.Add(MainProgram.Level.SerialPort);
            if (comm.SolenoidUse) stackpanelComunication.Children.Add(MainProgram.Solenoid.SerialPort);
            if (comm.PowerMetterUse) stackpanelComunication.Children.Add(MainProgram.PowerMetter.SerialPort);
            if (comm.BoardExtensionUse) stackpanelComunication.Children.Add(MainProgram.BoardExtension.SerialPort);
            if (comm.UUT1Use) stackpanelComunication.Children.Add(MainProgram.UUTs[0].serial);
            if (comm.UUT2Use) stackpanelComunication.Children.Add(MainProgram.UUTs[1].serial);
            if (comm.UUT3Use) stackpanelComunication.Children.Add(MainProgram.UUTs[2].serial);
            if (comm.UUT4Use) stackpanelComunication.Children.Add(MainProgram.UUTs[3].serial);
            if (comm.ScannerUse) stackpanelComunication.Children.Add(MainProgram.BarcodeReader);
            if (comm.PrinterUse) stackpanelComunication.Children.Add(MainProgram.Printer.Serial);

            if (comm.MicrophoneUse) stackpanelComunication.Children.Add(BuildMicrophoneStatus(comm));
            if (comm.CameraUse) stackpanelComunication.Children.Add(BuildCameraStatus());
        }

        // Small widget shown on the device status bar: [icon] [LED] [name]. Style consistent with SerialPortDisplay.
        // Shared "idle" gray for the device-status icons - the ICON itself is the status light (LimeGreen when
        // active, this gray when idle), so there is no separate coloured dot.
        private static readonly System.Windows.Media.Brush IdleGray =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC6, 0xC6, 0xC6));

        private UIElement BuildMicrophoneStatus(VTMProgram.Communication comm)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var icon = new FontAwesome.Sharp.IconImage { Icon = FontAwesome.Sharp.IconChar.Microphone, Foreground = IdleGray, Height = 13, Width = 13, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "green = capturing, gray = idle" };
            var name = !string.IsNullOrEmpty(comm.MicrophoneName) ? comm.MicrophoneName : "MIC (default)";
            panel.Children.Add(icon);
            panel.Children.Add(new TextBlock { Text = name, FontSize = 10, FontWeight = FontWeights.DemiBold, VerticalAlignment = VerticalAlignment.Center });

            // Icon goes green while SoundTester is capturing
            UpdateStatusIcon(icon, MainProgram?.SoundTester?.IsCapturing ?? false);
            if (MainProgram?.SoundTester != null)
            {
                MainProgram.SoundTester.CaptureStarted += (s, e) => Dispatcher.Invoke(() => UpdateStatusIcon(icon, true));
                MainProgram.SoundTester.CaptureStopped += (s, e) => Dispatcher.Invoke(() => UpdateStatusIcon(icon, false));
            }
            return panel;
        }

        private static void UpdateStatusIcon(FontAwesome.Sharp.IconImage icon, bool active)
        {
            icon.Foreground = active ? (System.Windows.Media.Brush)System.Windows.Media.Brushes.LimeGreen : IdleGray;
        }

        private UIElement BuildCameraStatus()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var icon = new FontAwesome.Sharp.IconImage { Icon = FontAwesome.Sharp.IconChar.Camera, Foreground = IdleGray, Height = 13, Width = 13, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "green = running, gray = idle" };
            panel.Children.Add(icon);
            panel.Children.Add(new TextBlock { Text = "CAMERA", FontSize = 10, FontWeight = FontWeights.DemiBold, VerticalAlignment = VerticalAlignment.Center });

            // Poll camera state via a timer (CameraControl has no ready event)
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (s, e) =>
            {
                bool running = false;
                try { running = MainProgram?.Capture != null && MainProgram.Capture.LastMatFrame != null; } catch { }
                UpdateStatusIcon(icon, running);
            };
            timer.Start();
            return panel;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            btAutoPage.IsChecked = true;
            ManualPage.camera.START();
            MainProgram.START();
            MainProgram.ResetTest();
            AutoPage.EnableLive();
        }


        private void MainProgram_EditModel_OnLoaded(object sender, EventArgs e)
        {
            if (MainProgram.EditModel != null)
            {
                VisionPage.EditModel = MainProgram.EditModel;
                ModelPage.EditModel = MainProgram.EditModel;
                SoundPage.RefreshStepList();   // reload SND steps + model name for the new model
            }
        }

        private void MainProgram_EditModel_OnSave(object sender, EventArgs e)
        {
            SHOW_LOADING();
            var str = AutoPage.LoadModel(MainProgram.EditModel.Path);
            ManualPage.TestModel = AutoPage.TestModel;
            MainProgram.SetBoards();
            if (str != null)
            {
                MainProgram.EditModel = Utility.Extensions.ConvertFromJson<Model>(str);
                MainProgram.EditModel.Path = AutoPage.TestModel.Path;   // Name follows the ACTUAL opened file
                ModelPage.EditModel = MainProgram.EditModel;
                VisionPage.EditModel = MainProgram.EditModel;
                var cs = MainProgram.EditModel.CameraSetting;
                AutoPage.cameraSetting.ApplyModelSettings(cs);
                ManualPage.cameraSetting.ApplyModelSettings(cs);
                VisionPage.cameraSetting.ApplyModelSettings(cs);
            }
            HIDE_LOADING();
        }

        #region Form control

        //variable
        private string LastPageSelected = "";

        // -Functions-

        // 현재 run 하고있는 operation 다 멈추고 앱 끄기
        private void btCloseWindow_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in stackpanelComunication.Children)
            {
                (item as Controls.SerialPortDisplay)?._shutDown.Cancel();
            }
            AutoPage._shutDown?.Cancel();

            App.Current.Shutdown();
            Close();
        }

        // WindowState 바꾸기 -> 현재 윈도우가 최대 화면이면 일반 크기 화면으로 바꾸고
        // 최대 화면이 아니면 최대 화면으로 바꾸기
        private void btMaximizeWindow_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        // 윈도우 화면 내리기 (minimized)
        private void btMinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        // DockPanel을 왼쪽 마우스로 누르면 앱 윈도우 전체를 드래그 해서 움직일수있다
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    this.DragMove();
                }
                catch (Exception ex)
                {
                }
            }
        }

        #endregion Form control

        #region Menu Panel

        // Enter, leaver effect
        // Page Select

        private void btSelectPage_Click(object sender, RoutedEventArgs e)
        {
            var bt = (sender as ToggleButton);
            btAutoPage.IsChecked = bt == btAutoPage;
            btManualPage.IsChecked = bt == btManualPage;
            btModelPage.IsChecked = bt == btModelPage;
            btVisionPage.IsChecked = bt == btVisionPage;
            btSoundPage.IsChecked = bt == btSoundPage;
            btSettingPage.IsChecked = bt == btSettingPage;

            if (MainProgram.IsTestting)
            {
                btAutoPage.IsChecked = LastPageSelected == btAutoPage.Name;
                btManualPage.IsChecked = LastPageSelected == btManualPage.Name;
                btModelPage.IsChecked = LastPageSelected == btModelPage.Name;
                btSettingPage.IsChecked = LastPageSelected == btSettingPage.Name;

                return;
            }

            if (bt.Name == LastPageSelected)
            {
                bt.IsChecked = true;
                return;
            }

            AutoPageHolder.Visibility = Visibility.Hidden;
            ManualPageHolder.Visibility = Visibility.Hidden;
            ModelPageHolder.Visibility = Visibility.Hidden;
            VisionPageHolder.Visibility = Visibility.Hidden;
            SoundPageHolder.Visibility = Visibility.Hidden;
            SettingPageHolder.Visibility = Visibility.Hidden;

            MainProgram.EditModel.CameraSetting = VisionPage.cameraSetting.Capture?.cameraSetting;

            VisionPage.DisableLive();
            AutoPage.DisableLive();
            ManualPage.DisableLive();

            LastPageSelected = bt.Name;
            switch (bt.Name)
            {
                case "btAutoPage":
                    btAutoPage.Background = new SolidColorBrush(Color.FromRgb(224, 224, 224));
                    AutoPageHolder.Visibility = Visibility.Visible;
                    MainProgram.pageActive = Program.PageActive.AutoPage;
                    AutoPage.AttachVisionTester();   // the shared VisionTester follows the visible page
                    AutoPage.EnableLive();
                    MainProgram.ResetTest();
                    break;

                case "btManualPage":
                    btManualPage.Background = new SolidColorBrush(Color.FromRgb(224, 224, 224));
                    ManualPageHolder.Visibility = Visibility.Visible;
                    MainProgram.pageActive = Program.PageActive.ManualPage;
                    ManualPage.AttachVisionTester();
                    ManualPage.EnableLive();

                    break;

                case "btModelPage":
                    btModelPage.Background = new SolidColorBrush(Color.FromRgb(224, 224, 224));
                    MainProgram.pageActive = Program.PageActive.ModelPage;
                    ModelPageHolder.Visibility = Visibility.Visible;
                    ModelPage.StepsGridData.Items.Refresh();
                    break;

                case "btVisionPage":
                    btVisionPage.Background = new SolidColorBrush(Color.FromRgb(224, 224, 224));
                    MainProgram.pageActive = Program.PageActive.VistionPage;
                    VisionPageHolder.Visibility = Visibility.Visible;
                    VisionPage.EnableLive();
                    break;

                case "btSoundPage":
                    btSoundPage.Background = new SolidColorBrush(Color.FromRgb(224, 224, 224));
                    SoundPageHolder.Visibility = Visibility.Visible;
                    SoundPage.RefreshStepList();
                    break;

                case "btSettingPage":
                    btSettingPage.Background = new SolidColorBrush(Color.FromRgb(128, 128, 128));
                    SettingPage.Program = MainProgram;
                    MainProgram.pageActive = Program.PageActive.ModelPage;
                    SettingPage.LoadDataSetting();
                    SettingPageHolder.Visibility = Visibility.Visible;
                    break;

                default:
                    break;
            }
        }

        #endregion Menu Panel

        //Open Model

        private void btOpenModel_Click(object sender, RoutedEventArgs e)
        {
            OpenModel();
        }

        // Central open-model flow, callable from any page (e.g. SoundPage's Open button).
        public void OpenModel()
        {
            SHOW_LOADING();
            var str = AutoPage.LoadModel();
            ManualPage.TestModel = AutoPage.TestModel;
            if (str != null)
            {
                MainProgram.EditModel = Utility.Extensions.ConvertFromJson<Model>(str);
                MainProgram.EditModel.Path = AutoPage.TestModel.Path;   // Name follows the ACTUAL opened file
                ModelPage.EditModel = MainProgram.EditModel;
                VisionPage.EditModel = MainProgram.EditModel;
                SoundPage.RefreshStepList();
                // Load the model's camera settings onto the sliders (direct, from the model) and push them to the live
                // camera via the capture-thread queue. A direct videoCapture set + read-back was racy while streaming,
                // which made a just-saved model look like it "didn't save" on reopen.
                var cs = MainProgram.EditModel.CameraSetting;
                AutoPage.cameraSetting.ApplyModelSettings(cs);
                ManualPage.cameraSetting.ApplyModelSettings(cs);
                VisionPage.cameraSetting.ApplyModelSettings(cs);
                if (cs != null)
                    Utility.Debug.Write("CAMERA: set from model (Bri=" + cs.Brightness + " Con=" + cs.Contrast
                        + " Exp=" + cs.Exposure + " Foc=" + cs.Focus + " WB=" + cs.WBTemperature + " Gain=" + cs.Gain + ")",
                        Utility.Debug.ContentType.Notify);
            }
            HIDE_LOADING();
        }

        public System.Timers.Timer disableSpam = new System.Timers.Timer() { Interval = 3000 };

        private void btCheckCommunications_Click(object sender, RoutedEventArgs e)
        {
            btCheckCommunications.IsEnabled = false;
            disableSpam.Elapsed += DisableSpam_Elapsed;
            disableSpam.Enabled = true;
            disableSpam.AutoReset = false;
            disableSpam.Stop();
            disableSpam.Start();
            MainProgram.CheckComnunication();
        }

        private void DisableSpam_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            this.Dispatcher.Invoke(new Action(() => btCheckCommunications.IsEnabled = true));
            disableSpam.Elapsed -= DisableSpam_Elapsed;
            disableSpam.Stop();
        }

        // Bench tools relocated from the AutoPage Diagnostic Log header to the bottom status bar. They act on the
        // Auto page's state (barcode / fail count), so the click just delegates to AutoPage's own handlers.
        private void Bench_FakeScan_Click(object sender, RoutedEventArgs e)
        {
            AutoPage.FakeScan_Click(sender, e);
        }

        private void Bench_MinusFail_Click(object sender, RoutedEventArgs e)
        {
            AutoPage.MinusFailQtyBtn_Click(sender, e);
        }

        // pnLoading = 로딩 스크린
        // 로딩 스크린 보여주기
        public void SHOW_LOADING()
        {
            pnLoading.Visibility = Visibility.Visible;
        }

        public void HIDE_LOADING()
        {
            pnLoading.Visibility = Visibility.Collapsed;
        }

        //private void btReportPage_Click(object sender, RoutedEventArgs e)
        //{
        //    Process.Start(Directory.GetCurrentDirectory() + @"\VTM_Report.exe");
        //}

        private void btCheckCommunications_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Console.WriteLine(DateTime.Now.ToString() + e.NewValue);
        }

        private void AutoPageHolder_Navigated(object sender, NavigationEventArgs e)
        {
        }

        private void window_SourceInitialized(object sender, EventArgs e)
        {
            //WindowSizing.WindowInitialized(this);
        }
    }

    public static class WindowSizing
    {
        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        #region DLLImports

        [DllImport("shell32", CallingConvention = CallingConvention.StdCall)]
        public static extern int SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

        [DllImport("user32", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32")]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        [DllImport("user32")]
        internal static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        #endregion DLLImports

        private static MINMAXINFO AdjustWorkingAreaForAutoHide(IntPtr monitorContainingApplication, MINMAXINFO mmi)
        {
            IntPtr hwnd = FindWindow("Shell_TrayWnd", null);
            if (hwnd == null) return mmi;
            IntPtr monitorWithTaskbarOnIt = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (!monitorContainingApplication.Equals(monitorWithTaskbarOnIt)) return mmi;
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = hwnd;
            SHAppBarMessage((int)ABMsg.ABM_GETTASKBARPOS, ref abd);
            int uEdge = GetEdge(abd.rc);
            bool autoHide = System.Convert.ToBoolean(SHAppBarMessage((int)ABMsg.ABM_GETSTATE, ref abd));

            if (!autoHide) return mmi;

            switch (uEdge)
            {
                case (int)ABEdge.ABE_LEFT:
                    mmi.ptMaxPosition.x += 2;
                    mmi.ptMaxTrackSize.x -= 2;
                    mmi.ptMaxSize.x -= 2;
                    break;

                case (int)ABEdge.ABE_RIGHT:
                    mmi.ptMaxSize.x -= 2;
                    mmi.ptMaxTrackSize.x -= 2;
                    break;

                case (int)ABEdge.ABE_TOP:
                    mmi.ptMaxPosition.y += 2;
                    mmi.ptMaxTrackSize.y -= 2;
                    mmi.ptMaxSize.y -= 2;
                    break;

                case (int)ABEdge.ABE_BOTTOM:
                    mmi.ptMaxSize.y -= 2;
                    mmi.ptMaxTrackSize.y -= 2;
                    break;

                default:
                    return mmi;
            }
            return mmi;
        }

        private static int GetEdge(RECT rc)
        {
            int uEdge = -1;
            if (rc.top == rc.left && rc.bottom > rc.right)
                uEdge = (int)ABEdge.ABE_LEFT;
            else if (rc.top == rc.left && rc.bottom < rc.right)
                uEdge = (int)ABEdge.ABE_TOP;
            else if (rc.top > rc.left)
                uEdge = (int)ABEdge.ABE_BOTTOM;
            else
                uEdge = (int)ABEdge.ABE_RIGHT;
            return uEdge;
        }

        public static void WindowInitialized(Window window)
        {
            IntPtr handle = (new System.Windows.Interop.WindowInteropHelper(window)).Handle;
            System.Windows.Interop.HwndSource.FromHwnd(handle).AddHook(new System.Windows.Interop.HwndSourceHook(WindowProc));
        }

        private static IntPtr WindowProc(System.IntPtr hwnd, int msg, System.IntPtr wParam, System.IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x0024:
                    WmGetMinMaxInfo(hwnd, lParam);
                    handled = true;
                    break;
            }

            return (IntPtr)0;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            IntPtr monitorContainingApplication = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitorContainingApplication != System.IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                GetMonitorInfo(monitorContainingApplication, monitorInfo);
                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;
                mmi.ptMaxPosition.x = Math.Abs(rcWorkArea.left - rcMonitorArea.left);
                mmi.ptMaxPosition.y = Math.Abs(rcWorkArea.top - rcMonitorArea.top);
                mmi.ptMaxSize.x = Math.Abs(rcWorkArea.right - rcWorkArea.left);
                mmi.ptMaxSize.y = Math.Abs(rcWorkArea.bottom - rcWorkArea.top);
                mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x + 4;                                                 //maximum drag X size for the window
                mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y + 4;                                                //maximum drag Y size for the window
                mmi.ptMinTrackSize.x = 800;                                                             //minimum drag X size for the window
                mmi.ptMinTrackSize.y = 600;                                                             //minimum drag Y size for the window
                mmi = AdjustWorkingAreaForAutoHide(monitorContainingApplication, mmi);                  //need to adjust sizing if taskbar is set to autohide
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        public enum ABEdge
        {
            ABE_LEFT = 0,
            ABE_TOP = 1,
            ABE_RIGHT = 2,
            ABE_BOTTOM = 3
        }

        public enum ABMsg
        {
            ABM_NEW = 0,
            ABM_REMOVE = 1,
            ABM_QUERYPOS = 2,
            ABM_SETPOS = 3,
            ABM_GETSTATE = 4,
            ABM_GETTASKBARPOS = 5,
            ABM_ACTIVATE = 6,
            ABM_GETAUTOHIDEBAR = 7,
            ABM_SETAUTOHIDEBAR = 8,
            ABM_WINDOWPOSCHANGED = 9,
            ABM_SETSTATE = 10
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uCallbackMessage;
            public int uEdge;
            public RECT rc;
            public bool lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor = new RECT();
            public RECT rcWork = new RECT();
            public int dwFlags = 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;

            public POINT(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }
    }
}