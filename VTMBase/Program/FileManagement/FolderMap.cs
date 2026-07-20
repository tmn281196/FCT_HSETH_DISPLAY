using VTMBase;
using VTMControls.DeviceControl;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;

namespace VTMBase
{
    public class FolderMap
    {
        private static string pcName;
        public static string PCName
        {
            get { return pcName; }
            set
            {
                pcName = value;
            }
        }
        private static string rootFolder;

        public static string RootFolder
        {
            get { return rootFolder; }
            set
            {
                rootFolder = value;
            }
        }

        public const string SettingFolder = @"\Setting";
        public const string ModelFolder = @"\Model";
        public string HistoryFolder = @"\History\" + DateTime.Now.ToString(@"yyyy\\MM");
        public const string MESFolder = @"\MES";
        public const string PCBFolder = @"\PCB";
        public const string logFolder = "log";

        public const string DefaultModelFileExt = ".vmdl";
        public const string DefaultTxFileExt = ".vtx";
        public const string DefaultRxFileExt = ".vrx";
        public const string DefaultQrFileExt = ".vqr";
        public const string DefaultLogFileExt = ".vlog";

        public string LogDir = "";

        public void TryCreatFolderMap()
        {
            RootFolder = "C:\\";
            if (!Directory.Exists(RootFolder)) Directory.CreateDirectory(RootFolder);
            if (!Directory.Exists(RootFolder + logFolder)) Directory.CreateDirectory(RootFolder + logFolder);
            //if (!Directory.Exists(RootFolder + ModelFolder)) Directory.CreateDirectory(RootFolder + ModelFolder);
            //if (!Directory.Exists(RootFolder + HistoryFolder)) Directory.CreateDirectory(RootFolder + HistoryFolder);
            //if (!Directory.Exists(RootFolder + MESFolder)) Directory.CreateDirectory(RootFolder + MESFolder);
            //if (!Directory.Exists(RootFolder + PCBFolder)) Directory.CreateDirectory(RootFolder + PCBFolder);
            Console.WriteLine(String.Format("{0}\\{1}", HistoryFolder, DateTime.Now.Date.ToString("dd") + ".vtmh"));
        }

        public static List<ModelLoaded> ModelLoadeds = new List<ModelLoaded>();

        public static void GetListModelsLoaded()
        {
            if (File.Exists(RootFolder + SettingFolder + "\\models.ld"))
            {
                ModelLoadeds.Clear();
                var strModel = File.ReadAllLines(RootFolder + SettingFolder + "\\models.ld");
                foreach (var item in strModel)
                {
                    ModelLoadeds.Add(new ModelLoaded() { Path = item });
                }
            }
        }

        public static void SaveListModelLoaded()
        {
            if (File.Exists(RootFolder + SettingFolder + "\\models.ld")) File.Delete(RootFolder + SettingFolder + "\\models.ld");
            for (int i = 0; i < 10; i++)
            {
                if (i < ModelLoadeds.Count)
                {
                    using (StreamWriter writer = File.AppendText(RootFolder + SettingFolder + "\\models.ld"))
                    {
                        writer.WriteLine(ModelLoadeds[i].Path);
                    }
                }
            }
        }

        public void SaveHistory(object HistoryObject)
        {
            Console.WriteLine(String.Format("{0}\\{1}", RootFolder + HistoryFolder, DateTime.Now.Date.ToString("dd") + ".vtmh"));
            File.AppendAllText(String.Format("{0}\\{1}", RootFolder + HistoryFolder, DateTime.Now.Date.ToString("dd") + ".vtmh"), VTMUtility.Extensions.ConvertToJson(HistoryObject) + Environment.NewLine);
        }

        // exportLog=false skips writing the .lgd file (Settings -> "Export log"), but the method still runs.
        // That is deliberate: the loop below also NORMALISES CAM/LED step results (ValueGet1/Result1) into the
        // very Step objects the UI grids bind to - board.TestStep is a shallow copy of TestModel.Steps, so these
        // are the same objects. Returning early would make an export toggle silently change what the operator
        // sees on screen. The guard therefore sits on the file write and the directory creation only.
        public void SaveLogFile(bool is_final_result_fail, object HistoryObject, int stepTesting, bool exportLog = true)
        {
            try
            {
                var item = (HistoryObject as Board);
                // != 0 ? fail : ok
                int stepNum = 1;

                if (stepTesting != 0)
                {
                    stepNum = item.TestStep.Count - stepTesting != 0 ? stepTesting + 1 : item.TestStep.Count;
                }

                string dateToday = DateTime.Now.ToString("yyyyMMdd").ToString();

                string baseDir = LogDir;
                string baseDateDir = baseDir + "\\" + dateToday;
                //string fileName = item.Barcode + "_" + item.StartTest.ToString("yyyy/MM/dd HH:mm:ss") + "_" + item.Result;
                string fileName = item.Barcode + "_" + item.StartTest.ToString("yyyy/MM/dd HH:mm:ss");
                fileName = fileName.Replace("-", "").Replace(":", "").Replace(" ", "").Replace("/", "");
                string dir = baseDir + "\\" + fileName + ".lgd";

                List<string> DatasExport = new List<string>();

                if (exportLog && !Directory.Exists(baseDir))
                {
                    Directory.CreateDirectory(baseDir);
                }

                //if (!Directory.Exists(baseDateDir))
                //{
                //    Directory.CreateDirectory(baseDateDir);
                //}

                for (int i = 0; i < stepNum; i++)
                {
                    if (item.TestStep[i].Skip)
                    {
                        continue;
                    }

                    string condition1 = item.TestStep[i].Condition1;

                    if (condition1 != null)
                    {
                        condition1 = condition1.Replace("/", ",");
                    }

                    if (item.TestStep[i].CMD == CMDs.LCD.ToString() || item.TestStep[i].CMD == CMDs.LED.ToString() || item.TestStep[i].CMD == CMDs.GLED.ToString())
                    {
                        condition1 = "VALUE";
                    }

                    if (item.TestStep[i].CMD == CMDs.CAM.ToString())
                    {
                        if (item.TestStep[i].ValueGet1 == "condition" || item.TestStep[i].ValueGet1 == "Oper")
                        {
                            item.TestStep[i].ValueGet1 = "OFF";
                            item.TestStep[i].Result1 = Step.Ng;
                        }
                        else
                        {
                            item.TestStep[i].ValueGet1 = "ON";
                            item.TestStep[i].Result1 = Step.Ok;
                        }
                    }

                    if (item.TestStep[i].CMD == CMDs.LED.ToString())
                    {
                        if (item.TestStep[i].Result1 == string.Empty)
                        {
                            item.TestStep[i].ValueGet1 = "";
                            item.TestStep[i].Result1 = "NG";
                        }
                    }

                    string valueGet1 = item.TestStep[i].ValueGet1;
                    if (valueGet1 != null)
                    {
                        valueGet1 = valueGet1.Replace("<invalid>", "");
                    }

                    string exportData = String.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|||||||{10}|{11}",
                        item.TestStep[i].No.ToString(),
                        item.Barcode,
                        PCName,
                        item.StartTest.ToString("yyyy/MM/dd HH:mm:ss"),
                        item.TestStep[i].CMD,
                        condition1,
                        item.TestStep[i].Oper,
                        condition1,
                        valueGet1,
                        "MP_" + item.Barcode,
                        item.TestStep[i].Result1,
                        is_final_result_fail ? "NG" : "OK"
                        );

                    DatasExport.Add(exportData);
                }

                if (exportLog)
                {
                    File.AppendAllLines(dir, DatasExport.ToArray());
                }
            }
            catch (Exception e)
            {
                MessageBox.Show("Crash" + e.Message, "ERROR", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}