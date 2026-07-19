using VTMUtility;
using VTMControls.DeviceControl;
using VTMBase;
using Microsoft.Win32;
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
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace VTMTester
{
    /// <summary>
    /// Interaction logic for SettingPage.xaml
    /// </summary>
    public partial class SettingPage : Page
    {
        public AppSettingParam Setting = new AppSettingParam();
        private VTMBase.Program _Program = new Program();

        public Program Program
        {
            get { return _Program; }
            set
            {
                _Program = value;
                Setting = _Program.appSetting;
                PrinterHolder.Child = _Program.Printer;
            }
        }

        public SettingPage()
        {
            InitializeComponent();
            this.Setting = Program.appSetting;
            cbbBarcodePort.ItemsSource = Communication.ComPorts;
            cbbDMM1Port.ItemsSource = Communication.ComPorts;
            cbbDMM2Port.ItemsSource = Communication.ComPorts;
            cbbLevelPort.ItemsSource = Communication.ComPorts;
            cbbMux1Port.ItemsSource = Communication.ComPorts;
            cbbMux2Port.ItemsSource = Communication.ComPorts;
            cbbPrinterPort.ItemsSource = Communication.ComPorts;
            cbbRelayPort.ItemsSource = Communication.ComPorts;
            cbbSolenoidPort.ItemsSource = Communication.ComPorts;
            cbbSysPort.ItemsSource = Communication.ComPorts;
            cbbUUT1Port.ItemsSource = Communication.ComPorts;
            cbbUUT2Port.ItemsSource = Communication.ComPorts;
            cbbUUT3Port.ItemsSource = Communication.ComPorts;
            cbbUUT4Port.ItemsSource = Communication.ComPorts;
            cbbPowerMetterPort.ItemsSource = Communication.ComPorts;
            cbbExtensionBoardPort.ItemsSource = Communication.ComPorts;

            cbbBarcodeParity.ItemsSource = Enum.GetNames(typeof(Parity)).ToList();
            cbbBarcodeParity.SelectedIndex = 0;
            LoadDataSetting();
        }

        public void LoadDataSetting()
        {
            // Operation
            cbNoTextPressUp.IsChecked = Setting.Operations.NoTest_PressUp;
            nudStartDelay.Value = Setting.Operations.StartDelaytime;
            nudPressUpTime.Value = Setting.Operations.TestPressUpTime;
            rbFailTestContinue.IsChecked = Setting.Operations.FailContinue;
            rbFailTestStopAll.IsChecked = Setting.Operations.FailStopAll;
            rbFailTestStopPCB.IsChecked = Setting.Operations.FailStopPCB;
            cbResistanceFailStop.IsChecked = Setting.Operations.FailResistanceStopAll;
            nudErrorJumpCount.Value = Setting.Operations.ErrorJumpCount;
            cbSaveFailPCB.IsChecked = Setting.Operations.SaveFailPCB;
            cbExportLog.IsChecked = Setting.Operations.ExportLog;
            cbUsePreEndSig.IsChecked = Setting.Operations.UsePre_endSignal;
            nudRetryCount.Value = Setting.Operations.RetryCount;
            cbSkipPass.IsChecked = Setting.Operations.PassSkipPCB;
            cbRetryUpdown.IsChecked = Setting.Operations.UseRetryUpdown;
            nudRetryUpdown.Value = Setting.Operations.RetryUpdownTime;

            // Comunication
            cbbBarcodePort.SelectedItem = Setting.Communication.ScannerPort;
            cbbDMM1Port.SelectedItem = Setting.Communication.DMM1Port;
            cbbDMM2Port.SelectedItem = Setting.Communication.DMM2Port;
            cbbLevelPort.SelectedItem = Setting.Communication.LevelPort;
            cbbMux1Port.SelectedItem = Setting.Communication.Mux1Port;
            cbbMux2Port.SelectedItem = Setting.Communication.Mux2Port;
            cbbPrinterPort.SelectedItem = Setting.Communication.PrinterPort;
            cbbRelayPort.SelectedItem = Setting.Communication.RelayPort;
            cbbSolenoidPort.SelectedItem = Setting.Communication.SolenoidPort;
            cbbSysPort.SelectedItem = Setting.Communication.SystemIOPort;
            cbbUUT1Port.SelectedItem = Setting.Communication.UUT1Port;
            cbbUUT2Port.SelectedItem = Setting.Communication.UUT2Port;
            cbbUUT3Port.SelectedItem = Setting.Communication.UUT3Port;
            cbbUUT4Port.SelectedItem = Setting.Communication.UUT4Port;

            cbbPowerMetterPort.SelectedItem = Setting.Communication.PowerMetterPort;
            cbbExtensionBoardPort.SelectedItem = Setting.Communication.BoardExtensionPort;

            cbbBarcodeBaud.SelectedItem = Setting.Communication.Scan_Baudrate;
            cbbBarcodeDatabit.SelectedItem = Setting.Communication.Scan_Databit;
            cbbBarcodeParity.SelectedItem = Setting.Communication.Scan_Parity.ToString();
            txtBoxExcelFileDir.Text = Setting.Communication.LogDirectory;

            // Microphone list (WASAPI capture endpoints)
            var mics = VTMBase.SoundTester.ListMicrophones();
            cbbMicrophone.ItemsSource = mics;
            var savedId = Setting.Communication.MicrophoneId;
            VTMBase.MicDeviceInfo match = null;
            foreach (var m in mics)
            {
                if (m.Id == savedId) { match = m; break; }
            }
            cbbMicrophone.SelectedItem = match;

            // Load the "Use" flags of each device
            chkSysPortUse.IsChecked = Setting.Communication.SystemIOUse;
            chkLevelUse.IsChecked = Setting.Communication.LevelUse;
            chkMux1Use.IsChecked = Setting.Communication.Mux1Use;
            chkMux2Use.IsChecked = Setting.Communication.Mux2Use;
            chkRelayUse.IsChecked = Setting.Communication.RelayUse;
            chkSolenoidUse.IsChecked = Setting.Communication.SolenoidUse;
            chkDMM1Use.IsChecked = Setting.Communication.DMM1Use;
            chkDMM2Use.IsChecked = Setting.Communication.DMM2Use;
            chkUUT1Use.IsChecked = Setting.Communication.UUT1Use;
            chkUUT2Use.IsChecked = Setting.Communication.UUT2Use;
            chkUUT3Use.IsChecked = Setting.Communication.UUT3Use;
            chkUUT4Use.IsChecked = Setting.Communication.UUT4Use;
            chkBarcodeUse.IsChecked = Setting.Communication.ScannerUse;
            chkPrinterUse.IsChecked = Setting.Communication.PrinterUse;
            chkPowerMetterUse.IsChecked = Setting.Communication.PowerMetterUse;
            chkExtensionBoardUse.IsChecked = Setting.Communication.BoardExtensionUse;
            chkMicrophoneUse.IsChecked = Setting.Communication.MicrophoneUse;
            chkCameraUse.IsChecked = Setting.Communication.CameraUse;

            // ETC
            nudMuxDelayFastRes.Value = Setting.ETCSetting.MUXdelay_Fast_RES;
            nudMuxDelayFastDCV.Value = Setting.ETCSetting.MUXdelay_Fast_DCV;
            nudMuxDelayFastACV.Value = Setting.ETCSetting.MUXdelay_Fast_ACVFRQ;

            nudMuxDelayMidRes.Value = Setting.ETCSetting.MUXdelay_Mid_RES;
            nudMuxDelayMidDCV.Value = Setting.ETCSetting.MUXdelay_Mid_DCV;
            nudMuxDelayMidACV.Value = Setting.ETCSetting.MUXdelay_Mid_ACVFRQ;

            nudMuxSlowRes.Value = Setting.ETCSetting.MUXdelay_slow_RES;
            nudMuxSlowDCV.Value = Setting.ETCSetting.MUXdelay_slow_DCV;
            nudMuxSlowACV.Value = Setting.ETCSetting.MUXdelay_slow_ACVFRQ;

            nudDMMContinueRead.Value = Setting.ETCSetting.DelayDMMRead;

            cbUseDischargeError.IsChecked = Setting.ETCSetting.UseDischargeError;
            cbUseDischargeConfig.IsChecked = Setting.ETCSetting.UseDischargeConfig;
            cbUseDischargeTestStart.IsChecked = Setting.ETCSetting.UseDischargeTestStart;

            nudDischargeTime.Value = Setting.ETCSetting.DischargeTime;
            nudDischargeVolt.Value = (int)Setting.ETCSetting.DischargeVolt;

            //// QR Tab load data
            //rbtTestPassAll.IsChecked = Setting.QR.TestPCBPrintAll;
            //rbtPassOnly.IsChecked = Setting.QR.TestPCBPassPrint;
            //nudPrintMaxStepCount.Value = Setting.QR.PrintMaxStepCount;
            //cbPCBArrayPrint.IsChecked = Setting.QR.ArrayPCBPrint;
            //tbCountryCode.Text = Setting.QR.CountryCode;
            //tbProductLine.Text = Setting.QR.ProductionLine;
            //tbProductMachine.Text = Setting.QR.InspectionEquipment;

            //nudPrintSpeed.Value = Setting.QR.label.speed;
            //nudDarkness.Value = Setting.QR.label.dark;
            //nudLabelLenght.Value = Setting.QR.label.Lenght;

            //nudLabelHomeX.Value = Setting.QR.label.home_x;
            //nudLableHomeY.Value = Setting.QR.label.home_y;
            //nudQRCodeX.Value = Setting.QR.label.qr_x;
            //nudQRCodeY.Value = Setting.QR.label.qr_y;
            //nudSize.Value = Setting.QR.Size;

            //nudSNPart1X.Value = Setting.QR.label.SN1_X;
            //nudSNPart1Y.Value = Setting.QR.label.SN1_Y;
            //nudSNPart1W.Value = Setting.QR.label.SN1_W;
            //nudSNPart1H.Value = Setting.QR.label.SN1_H;
            //tbSNPart1Font.Text = Setting.QR.label.SN1_Font.ToString();

            //nudSNPart2X.Value = Setting.QR.label.SN2_X;
            //nudSNPart2Y.Value = Setting.QR.label.SN2_Y;
            //nudSNPart2W.Value = Setting.QR.label.SN2_W;
            //nudSNPart2H.Value = Setting.QR.label.SN2_H;
            //tbSNPart2Font.Text = Setting.QR.label.SN2_Font.ToString();

            //nudMainCodeVersionX.Value = Setting.QR.label.MainCodeVersion_X;
            //nudMainCodeVersionY.Value = Setting.QR.label.MainCodeVersion_Y;
            //nudMainCodeVersionW.Value = Setting.QR.label.MainCodeVersion_W;
            //nudMainCodeVersionH.Value = Setting.QR.label.MainCodeVersion_H;
            //tbMainCodeVersionFont.Text = Setting.QR.label.MainCodeVersion_Font.ToString();

            //nudInvCodeVersionX.Value = Setting.QR.label.InvCodeVersion_X;
            //nudInvCodeVersionY.Value = Setting.QR.label.InvCodeVersion_Y;
            //nudInvCodeVersionW.Value = Setting.QR.label.InvCodeVersion_W;
            //nudInvCodeVersionH.Value = Setting.QR.label.InvCodeVersion_H;
            //tbInvCodeVersionFont.Text = Setting.QR.label.InvCodeVersion_Font.ToString();

            //nudPCBArrayTextX.Value = Setting.QR.label.PCBArrayText_X;
            //nudPCBArrayTextY.Value = Setting.QR.label.PCBArrayText_Y;
            //nudPCBArrayTextW.Value = Setting.QR.label.PCBArrayText_W;
            //nudPCBArrayTextH.Value = Setting.QR.label.PCBArrayText_H;
            //tbPCBArrayTextFont.Text = Setting.QR.label.PCBArrayText_Font.ToString();

            //System access
            cbUseAdminPass.IsChecked = Setting.SystemAccess.UseAdminPass;
            cbUseOperatorPass.IsChecked = Setting.SystemAccess.UseOperator;
        }

        public bool ApplyDataSetting()
        {
            //Operations
            Setting.Operations.NoTest_PressUp = (bool)cbNoTextPressUp.IsChecked;
            Setting.Operations.StartDelaytime = (int)nudStartDelay.Value;
            Setting.Operations.TestPressUpTime = (int)nudPressUpTime.Value;
            Setting.Operations.FailContinue = (bool)rbFailTestContinue.IsChecked;
            Setting.Operations.FailStopAll = (bool)rbFailTestStopAll.IsChecked;
            Setting.Operations.FailStopPCB = (bool)rbFailTestStopPCB.IsChecked;
            Setting.Operations.FailResistanceStopAll = (bool)cbResistanceFailStop.IsChecked;
            Setting.Operations.ErrorJumpCount = (int)nudErrorJumpCount.Value;
            Setting.Operations.SaveFailPCB = (bool)cbSaveFailPCB.IsChecked;
            Setting.Operations.ExportLog = (bool)cbExportLog.IsChecked;
            Setting.Operations.UsePre_endSignal = (bool)cbUsePreEndSig.IsChecked;
            Setting.Operations.RetryCount = (int)nudRetryCount.Value;
            Setting.Operations.PassSkipPCB = (bool)cbSkipPass.IsChecked;
            Setting.Operations.UseRetryUpdown = (bool)cbRetryUpdown.IsChecked;
            Setting.Operations.RetryUpdownTime = (int)nudRetryUpdown.Value;

            bool portHasDuplicate = CheckForDuplicatePorts();

            if (portHasDuplicate)
            {
                System.Windows.MessageBox.Show("Duplicate COM Port!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            else
            {
                //Communication
                Setting.Communication.ScannerPort = (string)cbbBarcodePort.SelectedItem;
                Setting.Communication.DMM1Port = (string)cbbDMM1Port.SelectedItem;
                Setting.Communication.DMM2Port = (string)cbbDMM2Port.SelectedItem;
                Setting.Communication.LevelPort = (string)cbbLevelPort.SelectedItem;
                Setting.Communication.Mux1Port = (string)cbbMux1Port.SelectedItem;
                Setting.Communication.Mux2Port = (string)cbbMux2Port.SelectedItem;
                Setting.Communication.PrinterPort = (string)cbbPrinterPort.SelectedItem;
                Setting.Communication.RelayPort = (string)cbbRelayPort.SelectedItem;
                Setting.Communication.SolenoidPort = (string)cbbSolenoidPort.SelectedItem;
                Setting.Communication.SystemIOPort = (string)cbbSysPort.SelectedItem;
                Setting.Communication.UUT1Port = (string)cbbUUT1Port.SelectedItem;
                Setting.Communication.UUT2Port = (string)cbbUUT2Port.SelectedItem;
                Setting.Communication.UUT3Port = (string)cbbUUT3Port.SelectedItem;
                Setting.Communication.UUT4Port = (string)cbbUUT4Port.SelectedItem;
                Setting.Communication.PowerMetterPort = (string)cbbPowerMetterPort.SelectedItem;
                Setting.Communication.BoardExtensionPort = (string)cbbExtensionBoardPort.SelectedItem;

                Setting.Communication.Scan_Baudrate = Int32.Parse(cbbBarcodeBaud.Text);
                Setting.Communication.Scan_Databit = Int32.Parse(cbbBarcodeDatabit.Text);
                Setting.Communication.LogDirectory = txtBoxExcelFileDir.Text.ToString();
                Enum.TryParse(cbbBarcodeParity.Text, out Parity parity);
                Setting.Communication.Scan_Parity = parity;

                // Microphone
                if (cbbMicrophone.SelectedItem is VTMBase.MicDeviceInfo mic)
                {
                    Setting.Communication.MicrophoneId = mic.Id;
                    Setting.Communication.MicrophoneName = mic.Name;
                }
                else
                {
                    Setting.Communication.MicrophoneId = "";
                    Setting.Communication.MicrophoneName = "";
                }

                // Save "Use" flags
                Setting.Communication.SystemIOUse = chkSysPortUse.IsChecked == true;
                Setting.Communication.LevelUse = chkLevelUse.IsChecked == true;
                Setting.Communication.Mux1Use = chkMux1Use.IsChecked == true;
                Setting.Communication.Mux2Use = chkMux2Use.IsChecked == true;
                Setting.Communication.RelayUse = chkRelayUse.IsChecked == true;
                Setting.Communication.SolenoidUse = chkSolenoidUse.IsChecked == true;
                Setting.Communication.DMM1Use = chkDMM1Use.IsChecked == true;
                Setting.Communication.DMM2Use = chkDMM2Use.IsChecked == true;
                Setting.Communication.UUT1Use = chkUUT1Use.IsChecked == true;
                Setting.Communication.UUT2Use = chkUUT2Use.IsChecked == true;
                Setting.Communication.UUT3Use = chkUUT3Use.IsChecked == true;
                Setting.Communication.UUT4Use = chkUUT4Use.IsChecked == true;
                Setting.Communication.ScannerUse = chkBarcodeUse.IsChecked == true;
                Setting.Communication.PrinterUse = chkPrinterUse.IsChecked == true;
                Setting.Communication.PowerMetterUse = chkPowerMetterUse.IsChecked == true;
                Setting.Communication.BoardExtensionUse = chkExtensionBoardUse.IsChecked == true;
                Setting.Communication.MicrophoneUse = chkMicrophoneUse.IsChecked == true;
                Setting.Communication.CameraUse = chkCameraUse.IsChecked == true;
            }

            //ETC
            Setting.ETCSetting.MUXdelay_Fast_RES = (int)nudMuxDelayFastRes.Value;
            Setting.ETCSetting.MUXdelay_Fast_DCV = (int)nudMuxDelayFastDCV.Value;
            Setting.ETCSetting.MUXdelay_Fast_ACVFRQ = (int)nudMuxDelayFastACV.Value;
            Setting.ETCSetting.MUXdelay_Mid_RES = (int)nudMuxDelayMidRes.Value;
            Setting.ETCSetting.MUXdelay_Mid_DCV = (int)nudMuxDelayMidDCV.Value;
            Setting.ETCSetting.MUXdelay_Mid_ACVFRQ = (int)nudMuxDelayMidACV.Value;
            Setting.ETCSetting.MUXdelay_slow_RES = (int)nudMuxSlowRes.Value;
            Setting.ETCSetting.MUXdelay_slow_DCV = (int)nudMuxSlowDCV.Value;
            Setting.ETCSetting.MUXdelay_slow_ACVFRQ = (int)nudMuxSlowACV.Value;
            Setting.ETCSetting.UseDischargeError = (bool)cbUseDischargeError.IsChecked;
            Setting.ETCSetting.UseDischargeConfig = (bool)cbUseDischargeConfig.IsChecked;
            Setting.ETCSetting.DelayDMMRead = (int)nudDMMContinueRead.Value;

            //QR
            //Setting.QR.TestPCBPrintAll = (bool)rbtTestPassAll.IsChecked;
            //Setting.QR.TestPCBPassPrint = (bool)rbtPassOnly.IsChecked;
            //Setting.QR.PrintMaxStepCount = (int)nudPrintMaxStepCount.Value;
            //Setting.QR.ArrayPCBPrint = (bool)cbPCBArrayPrint.IsChecked;
            //Setting.QR.CountryCode = tbCountryCode.Text;
            //Setting.QR.ProductionLine = tbProductLine.Text;
            //Setting.QR.InspectionEquipment = tbProductMachine.Text;
            //Setting.QR.label.speed = (int)nudPrintSpeed.Value;
            //Setting.QR.label.dark = (int)nudDarkness.Value;
            //Setting.QR.label.Lenght = (int)nudLabelLenght.Value;
            //Setting.QR.label.home_x = (int)nudLabelHomeX.Value;
            //Setting.QR.label.home_y = (int)nudLableHomeY.Value;
            //Setting.QR.label.qr_x = (int)nudQRCodeX.Value;
            //Setting.QR.label.qr_y = (int)nudQRCodeY.Value;
            //Setting.QR.Size = (int)nudSize.Value;
            //Setting.QR.label.SN1_X = (int)nudSNPart1X.Value;
            //Setting.QR.label.SN1_Y = (int)nudSNPart1Y.Value;
            //Setting.QR.label.SN1_W = (int)nudSNPart1W.Value;
            //Setting.QR.label.SN1_H = (int)nudSNPart1H.Value;
            //Setting.QR.label.SN1_Font = tbSNPart1Font.Text;
            //Setting.QR.label.SN2_X = (int)nudSNPart2X.Value;
            //Setting.QR.label.SN2_Y = (int)nudSNPart2Y.Value;
            //Setting.QR.label.SN2_W = (int)nudSNPart2W.Value;
            //Setting.QR.label.SN2_H = (int)nudSNPart2H.Value;
            //Setting.QR.label.SN2_Font = tbSNPart2Font.Text;
            //Setting.QR.label.MainCodeVersion_X = (int)nudMainCodeVersionX.Value;
            //Setting.QR.label.MainCodeVersion_Y = (int)nudMainCodeVersionY.Value;
            //Setting.QR.label.MainCodeVersion_W = (int)nudMainCodeVersionW.Value;
            //Setting.QR.label.MainCodeVersion_H = (int)nudMainCodeVersionH.Value;
            //Setting.QR.label.MainCodeVersion_Font = tbMainCodeVersionFont.Text;
            //Setting.QR.label.InvCodeVersion_X = (int)nudInvCodeVersionX.Value;
            //Setting.QR.label.InvCodeVersion_Y = (int)nudInvCodeVersionY.Value;
            //Setting.QR.label.InvCodeVersion_W = (int)nudInvCodeVersionW.Value;
            //Setting.QR.label.InvCodeVersion_H = (int)nudInvCodeVersionH.Value;
            //Setting.QR.label.InvCodeVersion_Font = tbInvCodeVersionFont.Text;
            //Setting.QR.label.PCBArrayText_X = (int)nudPCBArrayTextX.Value;
            //Setting.QR.label.PCBArrayText_Y = (int)nudPCBArrayTextY.Value;
            //Setting.QR.label.PCBArrayText_W = (int)nudPCBArrayTextW.Value;
            //Setting.QR.label.PCBArrayText_H = (int)nudPCBArrayTextH.Value;
            //Setting.QR.label.PCBArrayText_Font = tbPCBArrayTextFont.Text;
            //System
            Setting.SystemAccess.UseAdminPass = (bool)cbUseAdminPass.IsChecked;
            Setting.SystemAccess.UseOperator = (bool)cbUseOperatorPass.IsChecked;

            Program.appSetting = Setting.Clone();
            return true;
        }

        public bool CheckForDuplicatePorts()
        {
            // Only check duplicates for ports with "Use" ticked (unticked -> ignored)
            List<string> selectedPorts = new List<string>();
            if (chkBarcodeUse.IsChecked == true) selectedPorts.Add((string)cbbBarcodePort.SelectedItem);
            if (chkDMM1Use.IsChecked == true) selectedPorts.Add((string)cbbDMM1Port.SelectedItem);
            if (chkDMM2Use.IsChecked == true) selectedPorts.Add((string)cbbDMM2Port.SelectedItem);
            if (chkLevelUse.IsChecked == true) selectedPorts.Add((string)cbbLevelPort.SelectedItem);
            if (chkMux1Use.IsChecked == true) selectedPorts.Add((string)cbbMux1Port.SelectedItem);
            if (chkMux2Use.IsChecked == true) selectedPorts.Add((string)cbbMux2Port.SelectedItem);
            if (chkPrinterUse.IsChecked == true) selectedPorts.Add((string)cbbPrinterPort.SelectedItem);
            if (chkRelayUse.IsChecked == true) selectedPorts.Add((string)cbbRelayPort.SelectedItem);
            if (chkSolenoidUse.IsChecked == true) selectedPorts.Add((string)cbbSolenoidPort.SelectedItem);
            if (chkSysPortUse.IsChecked == true) selectedPorts.Add((string)cbbSysPort.SelectedItem);
            if (chkUUT1Use.IsChecked == true) selectedPorts.Add((string)cbbUUT1Port.SelectedItem);
            if (chkUUT2Use.IsChecked == true) selectedPorts.Add((string)cbbUUT2Port.SelectedItem);
            if (chkUUT3Use.IsChecked == true) selectedPorts.Add((string)cbbUUT3Port.SelectedItem);
            if (chkUUT4Use.IsChecked == true) selectedPorts.Add((string)cbbUUT4Port.SelectedItem);
            if (chkPowerMetterUse.IsChecked == true) selectedPorts.Add((string)cbbPowerMetterPort.SelectedItem);
            if (chkExtensionBoardUse.IsChecked == true) selectedPorts.Add((string)cbbExtensionBoardPort.SelectedItem);

            var portSet = new HashSet<string>();

            foreach (var port in selectedPorts)
            {
                if (port == null) continue;
                if (portSet.Contains(port))
                {
                    return true;
                }
                portSet.Add(port);
            }

            return false;
        }

        private void btPrintTest_Click(object sender, RoutedEventArgs e)
        {
            //Setting.QR.TestPCBPrintAll = (bool)rbtTestPassAll.IsChecked;
            //Setting.QR.TestPCBPassPrint = (bool)rbtPassOnly.IsChecked;
            //Setting.QR.PrintMaxStepCount = (int)nudPrintMaxStepCount.Value;
            //Setting.QR.ArrayPCBPrint = (bool)cbPCBArrayPrint.IsChecked;
            //Setting.QR.CountryCode = tbCountryCode.Text;
            //Setting.QR.ProductionLine = tbProductLine.Text;
            //Setting.QR.InspectionEquipment = tbProductMachine.Text;
            //Setting.QR.label.speed = (int)nudPrintSpeed.Value;
            //Setting.QR.label.dark = (int)nudDarkness.Value;
            //Setting.QR.label.Lenght = (int)nudLabelLenght.Value;
            //Setting.QR.label.home_x = (int)nudLabelHomeX.Value;
            //Setting.QR.label.home_y = (int)nudLableHomeY.Value;
            //Setting.QR.label.qr_x = (int)nudQRCodeX.Value;
            //Setting.QR.label.qr_y = (int)nudQRCodeY.Value;
            //Setting.QR.Size = (int)nudSize.Value;
            //Setting.QR.label.SN1_X = (int)nudSNPart1X.Value;
            //Setting.QR.label.SN1_Y = (int)nudSNPart1Y.Value;
            //Setting.QR.label.SN1_W = (int)nudSNPart1W.Value;
            //Setting.QR.label.SN1_H = (int)nudSNPart1H.Value;
            //Setting.QR.label.SN1_Font = tbSNPart1Font.Text;
            //Setting.QR.label.SN2_X = (int)nudSNPart2X.Value;
            //Setting.QR.label.SN2_Y = (int)nudSNPart2Y.Value;
            //Setting.QR.label.SN2_W = (int)nudSNPart2W.Value;
            //Setting.QR.label.SN2_H = (int)nudSNPart2H.Value;
            //Setting.QR.label.SN2_Font = tbSNPart2Font.Text;
            //Setting.QR.label.MainCodeVersion_X = (int)nudMainCodeVersionX.Value;
            //Setting.QR.label.MainCodeVersion_Y = (int)nudMainCodeVersionY.Value;
            //Setting.QR.label.MainCodeVersion_W = (int)nudMainCodeVersionW.Value;
            //Setting.QR.label.MainCodeVersion_H = (int)nudMainCodeVersionH.Value;
            //Setting.QR.label.MainCodeVersion_Font = tbMainCodeVersionFont.Text;
            //Setting.QR.label.InvCodeVersion_X = (int)nudInvCodeVersionX.Value;
            //Setting.QR.label.InvCodeVersion_Y = (int)nudInvCodeVersionY.Value;
            //Setting.QR.label.InvCodeVersion_W = (int)nudInvCodeVersionW.Value;
            //Setting.QR.label.InvCodeVersion_H = (int)nudInvCodeVersionH.Value;
            //Setting.QR.label.InvCodeVersion_Font = tbInvCodeVersionFont.Text;
            //Setting.QR.label.PCBArrayText_X = (int)nudPCBArrayTextX.Value;
            //Setting.QR.label.PCBArrayText_Y = (int)nudPCBArrayTextY.Value;
            //Setting.QR.label.PCBArrayText_W = (int)nudPCBArrayTextW.Value;
            //Setting.QR.label.PCBArrayText_H = (int)nudPCBArrayTextH.Value;
            //Setting.QR.label.PCBArrayText_Font = tbPCBArrayTextFont.Text;

            //Program.Print_Test(this.Setting.QR);
        }

        private void btSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            bool settingStatus = ApplyDataSetting();

            if (settingStatus)
            {
                Setting.SaveToFile("Config.cfg");
            }
        }

        private void btDefault_Click(object sender, RoutedEventArgs e)
        {
            //this.Setting.QR = new QR_Code();

            //rbtTestPassAll.IsChecked = Setting.QR.TestPCBPrintAll;
            //rbtPassOnly.IsChecked = Setting.QR.TestPCBPassPrint;
            //nudPrintMaxStepCount.Value = Setting.QR.PrintMaxStepCount;
            //cbPCBArrayPrint.IsChecked = Setting.QR.ArrayPCBPrint;
            //tbCountryCode.Text = Setting.QR.CountryCode;
            //tbProductLine.Text = Setting.QR.ProductionLine;
            //tbProductMachine.Text = Setting.QR.InspectionEquipment;

            //nudPrintSpeed.Value = Setting.QR.label.speed;
            //nudDarkness.Value = Setting.QR.label.dark;
            //nudLabelHomeX.Value = Setting.QR.label.home_x;
            //nudLableHomeY.Value = Setting.QR.label.home_y;
            //nudQRCodeX.Value = Setting.QR.label.qr_x;
            //nudQRCodeY.Value = Setting.QR.label.qr_y;
            //nudSize.Value = Setting.QR.Size;

            //nudSNPart1X.Value = Setting.QR.label.SN1_X;
            //nudSNPart1Y.Value = Setting.QR.label.SN1_Y;
            //nudSNPart1W.Value = Setting.QR.label.SN1_W;
            //nudSNPart1H.Value = Setting.QR.label.SN1_H;
            //tbSNPart1Font.Text = Setting.QR.label.SN1_Font.ToString();

            //nudSNPart2X.Value = Setting.QR.label.SN2_X;
            //nudSNPart2Y.Value = Setting.QR.label.SN2_Y;
            //nudSNPart2W.Value = Setting.QR.label.SN2_W;
            //nudSNPart2H.Value = Setting.QR.label.SN2_H;
            //tbSNPart2Font.Text = Setting.QR.label.SN2_Font.ToString();

            //nudMainCodeVersionX.Value = Setting.QR.label.MainCodeVersion_X;
            //nudMainCodeVersionY.Value = Setting.QR.label.MainCodeVersion_Y;
            //nudMainCodeVersionW.Value = Setting.QR.label.MainCodeVersion_W;
            //nudMainCodeVersionH.Value = Setting.QR.label.MainCodeVersion_H;
            //tbMainCodeVersionFont.Text = Setting.QR.label.MainCodeVersion_Font.ToString();

            //nudInvCodeVersionX.Value = Setting.QR.label.InvCodeVersion_X;
            //nudInvCodeVersionY.Value = Setting.QR.label.InvCodeVersion_Y;
            //nudInvCodeVersionW.Value = Setting.QR.label.InvCodeVersion_W;
            //nudInvCodeVersionH.Value = Setting.QR.label.InvCodeVersion_H;
            //tbInvCodeVersionFont.Text = Setting.QR.label.InvCodeVersion_Font.ToString();

            //nudPCBArrayTextX.Value = Setting.QR.label.PCBArrayText_X;
            //nudPCBArrayTextY.Value = Setting.QR.label.PCBArrayText_Y;
            //nudPCBArrayTextW.Value = Setting.QR.label.PCBArrayText_W;
            //nudPCBArrayTextH.Value = Setting.QR.label.PCBArrayText_H;
            //tbPCBArrayTextFont.Text = Setting.QR.label.PCBArrayText_Font.ToString();
        }

        private void btUndoall_Click(object sender, RoutedEventArgs e)
        {
            //this.Setting.QR = Program.appSetting.QR;

            //rbtTestPassAll.IsChecked = Setting.QR.TestPCBPrintAll;
            //rbtPassOnly.IsChecked = Setting.QR.TestPCBPassPrint;
            //nudPrintMaxStepCount.Value = Setting.QR.PrintMaxStepCount;
            //cbPCBArrayPrint.IsChecked = Setting.QR.ArrayPCBPrint;
            //tbCountryCode.Text = Setting.QR.CountryCode;
            //tbProductLine.Text = Setting.QR.ProductionLine;
            //tbProductMachine.Text = Setting.QR.InspectionEquipment;

            //nudPrintSpeed.Value = Setting.QR.label.speed;
            //nudDarkness.Value = Setting.QR.label.dark;
            //nudLabelHomeX.Value = Setting.QR.label.home_x;
            //nudLableHomeY.Value = Setting.QR.label.home_y;
            //nudQRCodeX.Value = Setting.QR.label.qr_x;
            //nudQRCodeY.Value = Setting.QR.label.qr_y;
            //nudSize.Value = Setting.QR.Size;

            //nudSNPart1X.Value = Setting.QR.label.SN1_X;
            //nudSNPart1Y.Value = Setting.QR.label.SN1_Y;
            //nudSNPart1W.Value = Setting.QR.label.SN1_W;
            //nudSNPart1H.Value = Setting.QR.label.SN1_H;
            //tbSNPart1Font.Text = Setting.QR.label.SN1_Font.ToString();

            //nudSNPart2X.Value = Setting.QR.label.SN2_X;
            //nudSNPart2Y.Value = Setting.QR.label.SN2_Y;
            //nudSNPart2W.Value = Setting.QR.label.SN2_W;
            //nudSNPart2H.Value = Setting.QR.label.SN2_H;
            //tbSNPart2Font.Text = Setting.QR.label.SN2_Font.ToString();

            //nudMainCodeVersionX.Value = Setting.QR.label.MainCodeVersion_X;
            //nudMainCodeVersionY.Value = Setting.QR.label.MainCodeVersion_Y;
            //nudMainCodeVersionW.Value = Setting.QR.label.MainCodeVersion_W;
            //nudMainCodeVersionH.Value = Setting.QR.label.MainCodeVersion_H;
            //tbMainCodeVersionFont.Text = Setting.QR.label.MainCodeVersion_Font.ToString();

            //nudInvCodeVersionX.Value = Setting.QR.label.InvCodeVersion_X;
            //nudInvCodeVersionY.Value = Setting.QR.label.InvCodeVersion_Y;
            //nudInvCodeVersionW.Value = Setting.QR.label.InvCodeVersion_W;
            //nudInvCodeVersionH.Value = Setting.QR.label.InvCodeVersion_H;
            //tbInvCodeVersionFont.Text = Setting.QR.label.InvCodeVersion_Font.ToString();

            //nudPCBArrayTextX.Value = Setting.QR.label.PCBArrayText_X;
            //nudPCBArrayTextY.Value = Setting.QR.label.PCBArrayText_Y;
            //nudPCBArrayTextW.Value = Setting.QR.label.PCBArrayText_W;
            //nudPCBArrayTextH.Value = Setting.QR.label.PCBArrayText_H;
            //tbPCBArrayTextFont.Text = Setting.QR.label.PCBArrayText_Font.ToString();
        }

        private void pwbCurrentAdminPass_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (pwbCurrentAdminPass.Password == Setting.SystemAccess.AdminPass)
            {
                pwbCurrentAdminPass.BorderBrush = new SolidColorBrush(Colors.Green);
            }
            else
            {
                pwbCurrentAdminPass.BorderBrush = new SolidColorBrush(Colors.Red);
            }
        }

        private void pwbNewAdminPass_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (pwbNewAdminPass.Password == pwbRenewAdminPass.Password)
            {
                pwbRenewAdminPass.BorderBrush = new SolidColorBrush(Colors.Green);
                pwbNewAdminPass.BorderBrush = new SolidColorBrush(Colors.Green);
            }
            else
            {
                pwbRenewAdminPass.BorderBrush = new SolidColorBrush(Colors.Red);
                pwbNewAdminPass.BorderBrush = new SolidColorBrush(Colors.Red);
            }
        }

        private void pwbRenewAdminPass_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (pwbNewAdminPass.Password == pwbRenewAdminPass.Password)
            {
                pwbRenewAdminPass.BorderBrush = new SolidColorBrush(Colors.Green);
                pwbNewAdminPass.BorderBrush = new SolidColorBrush(Colors.Green);
                if (pwbCurrentAdminPass.Password == Setting.SystemAccess.AdminPass)
                {
                    Setting.SystemAccess.AdminPass = pwbRenewAdminPass.Password;
                }
            }
            else
            {
                pwbRenewAdminPass.BorderBrush = new SolidColorBrush(Colors.Red);
                pwbNewAdminPass.BorderBrush = new SolidColorBrush(Colors.Red);
            }
        }

        private void pwbCurrentOPPass_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (pwbCurrentOPPass.Password == Setting.SystemAccess.OperationPass)
            {
                pwbCurrentOPPass.BorderBrush = new SolidColorBrush(Colors.Green);
            }
            else
            {
                pwbCurrentOPPass.BorderBrush = new SolidColorBrush(Colors.Red);
            }
        }

        private void pwbNewOPPass_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (pwbCurrentOPPass.Password == Setting.SystemAccess.OperationPass)
            {
                Setting.SystemAccess.OperationPass = pwbNewOPPass.Password;
            }
        }

        private void btCancle_Click(object sender, RoutedEventArgs e)
        {
            LoadDataSetting();
        }

        private void btApply_Click(object sender, RoutedEventArgs e)
        {
            if (!ApplyDataSetting()) return;

            // Apply immediately: refresh the device list on the status bar per the new Use flags
            if (System.Windows.Application.Current?.MainWindow is VTMTester.MainWindow mw)
            {
                mw.RefreshCommunicationPanel();
            }
        }

        private void btnChangeExcelDir_Click(object sender, RoutedEventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                // Optional: Set a description or starting folder
                folderDialog.Description = "Select Excel Files Directory";

                // Show the dialog and check if the user clicked OK
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    // Get the selected folder path
                    string selectedFolderPath = folderDialog.SelectedPath;

                    // Set the selected folder path to the lblExcelFileDir label's content
                    txtBoxExcelFileDir.Text = selectedFolderPath;
                }
            }
        }
    }
}