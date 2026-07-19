using OpenCvSharp.XFeatures2D;
using VTMControls.DeviceControl;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using static OpenCvSharp.Stitcher;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Windows.Controls;
using System.Windows.Markup;

namespace VTMBase
{
    public class Command
    {
        private CMDs privateCMD;
        public CMDs cmd
        {
            get { return privateCMD; }
            set
            {
                if (value != privateCMD)
                {
                    privateCMD = value;
                    CMD.Clear();
                    CMD.Add(Commands.SingleOrDefault(x => x.CMD == privateCMD));
                }
            }
        }

        public List<CommandDescriptions> CMD = new List<CommandDescriptions>
        {
            new CommandDescriptions()
                                    {
                                        CMD = CMDs.PWR,
                                        Condition1 = "KIND",
                                        Oper = "STATUS",
                                        Description = "Selected application of power ON/OFF"
                                    }
        };

        public static ObservableCollection<CommandDescriptions> Commands = new ObservableCollection<CommandDescriptions>
        {
            new CommandDescriptions()
            {
            CMD = CMDs.NON,
            },

            new CommandDescriptions(){
            CMD = CMDs.PWR,
            Condition1 = "KIND",
            IsListCondition1 = true,
            Condition1List = CommandDescriptions.CommandMode_PowerKIND,
            Oper = "STATUS",
            IsListOper = true,
            OperList = new List<string> { "ON", "OFF"},
            Description = "Selected application of power ON/OFF"
            },

            new CommandDescriptions(){
            CMD = CMDs.DLY,
            Oper = "TIME",
            Description = "Waits the progress for the time(ms)."
            },

            new CommandDescriptions()
            {
            CMD = CMDs.GEN,
            Condition1 = "FREQ",
            Oper = "CH",
            Description = "Control the Device Load."
            },

            new CommandDescriptions()
            {
            CMD = CMDs.LOD,
            Condition1 = "KIND",
            Oper = "STATUS",
            Description = "VTMControls the output of Channel which Generator B/d is designated.\r\n Frequency range : 0~100kHz"
            },

            new CommandDescriptions()
            {
            CMD = CMDs.BUZ,
            IsListOper = true,
            OperList = new List<string>(){ "START", "READ"},
            Oper = "FUNCTION TYPE",
            Condition1 = "Sample Time",
            Min = "Min",
            Max = "Max",
            Description = "Measure Buzzer as value (0 ~ 1024)\r\n Mode: START/READ\r\n  Maximum 2 site support, CH: A/B"
            },

            new CommandDescriptions()
            {
            CMD = CMDs.KEY,
            Condition1 = "CH",
            Oper = "STATUS",
            IsListOper = true,
            OperList = new List<string> { "ON", "OFF"},
            Condition2 = "Time ON",
            Count = "Delay time",
            Description = "Changes the condition of Channel which top Solenoid is designated.\r"
                        + "The case which will use Channel at multiple, '/', '~' With divides. (ex. 3/6/10~12)\r"
                        + "The status uses ON/OFF or ON time(ms).\r"
                        + "After relay operating, the delay time(ms) which is set waits.\r"
            },

            new CommandDescriptions()
            {
            CMD = CMDs.RLY,
            Condition1 = "CH",
            Oper = "STATUS",
            IsListOper = true,
            OperList = new List<string> { "ON", "OFF"},
            Condition2 = "Time ON",
            Count = "Delay time",
            Description = "Changes the condition of Channel which relay B/d is designated.\r"
                        + "The case which will use Channel at multiple, '/', '~' With divides. (ex. 3/6/10~12)\r"
                        + "The status uses ON/OFF or ON time(ms).\r"
                        + "After relay operating, the delay time(ms) which is set waits.\r"
            },

            //new CommandDescriptions()
            //{
            //CMD = CMDs.FRY,
            //Condition1 = "CH",
            //Oper = "STATUS",
            //Description = "Changes the condition of Channel which Fixture relay B/d is designated.\r"
            //            + "The case which will use Channel at multiple, '/', '~' With divides. (ex. 3/6/10~12)\r"
            //            + "The status uses ON/OFF or ON time(ms).\r"
            //            + "After relay operating, the delay time(ms) which is set waits.\r"
            //},

            new CommandDescriptions()
            {
            CMD = CMDs.MAK,
            Condition1 = "TEXT",
            Description = "Remark step"
            },

            new CommandDescriptions()
            {
            CMD = CMDs.DIS,
            Description = "Discharge command.\r\n" +
                          "The discharge which follows in discharge set executes.\r\n" +
                          "Will not be able to use from Power-ON conditions.\r\n",
            Condition1 = "STATUS",
            IsListCondition1 = true,
            Condition1List = new List<string>(){ "ON","OFF"},
            },

            new CommandDescriptions()
            {
            CMD = CMDs.END,
            Description = "Stops a test progress.\r\n"
            },

            new CommandDescriptions()
            {
            CMD = CMDs.ACV,
            Condition1 = "MUX(/RELAY)",
            Oper = "RANGE",
            IsListOper = true,
            OperList = CommandDescriptions.CommandMode_DMM_ACVrange,
            Condition2 = "RESOL",
            IsListCondition2 = true,
            Condition2List = CommandDescriptions.CommandMode_DMMresol,
            Spect = "SPEC",
            Min = "MIN",
            Max = "MAX",
            Mode = "MODE",
            IsListMode = true,
            ModeList = CommandDescriptions.CommandMode_DMM,
            Count = "COUNT/TIME",
            Description =" Uses DMM and measures AC voltage.\r\n"+
                        " The 'Range' selects DMM voltage measuring range. The 'M No' uses when storing a measurement result in the memory. There is a 'spec' and after comparison deciding, stores in the memory.\r\n"+
                        " mode : SPEC, CONT, MIN, AVR, MAX.\r\n"+
                        " SPEC : When the data which hits to an min-max limit scope comes in the within the set number of times just. (range : 1~1000 EA)\r\n"+
                        " CONT : As the set number of times continuously in min-max limit scope and when comes just.(range : 1~30 EA)\r\n"+
                        " MIN, AVR, MAX : For measurement time the minimum value which is measured, mean value and maximum extraction. (range : 300 ~ 10000ms)\r\n"

            },

            new CommandDescriptions()
            {
            CMD = CMDs.DCV,
            Condition1 = "MUX(/RELAY)",
            Oper = "RANGE",
            IsListOper = true,
            OperList= CommandDescriptions.CommandMode_DMM_DCVrange,
            Condition2 = "RESOL",
            IsListCondition2 = true,
            Condition2List= CommandDescriptions.CommandMode_DMMresol,
            Spect = "SPEC",
            Min = "MIN",
            Max = "MAX",
            Mode = "MODE",
            IsListMode = true,
            ModeList = CommandDescriptions.CommandMode_DMM,
            Count = "COUNT/TIME",
            Description =" Uses DMM and measures DC voltage.\r\n"+
                        " The 'Range' selects DMM voltage measuring range. The 'M No' uses when storing a measurement result in the memory. There is a 'spec' and after comparison deciding, stores in the memory.\r\n"+
                        " mode : SPEC, CONT, MIN, AVR, MAX.\r\n"+
                        " SPEC : When the data which hits to an min-max limit scope comes in the within the set number of times just. (range : 1~1000 EA)\r\n"+
                        " CONT : As the set number of times continuously in min-max limit scope and when comes just.(range : 1~30 EA)\r\n"+
                        " MIN, AVR, MAX : For measurement time the minimum value which is measured, mean value and maximum extraction. (range : 300 ~ 10000ms)\r\n"

            },

            new CommandDescriptions()
            {
            CMD = CMDs.FRQ,
            Condition1 = "MUX(/RELAY)",
            Oper = "RANGE",
            IsListOper = true,
            OperList= CommandDescriptions.CommandMode_DMM_ACVrange,
            Condition2 = "RESOL",
            IsListCondition2 = true,
            Condition2List= CommandDescriptions.CommandMode_DMMresol,
            Spect = "SPEC",
            Min = "MIN",
            Max = "MAX",
            Mode = "MODE",
            IsListMode = true,
            ModeList = CommandDescriptions.CommandMode_DMM,
            Count = "COUNT/TIME",
            Description =" Uses DMM and measures Frequency value.\r\n"+
                        " The 'Range' selects DMM voltage measuring range. The 'M No' uses when storing a measurement result in the memory. There is a 'spec' and after comparison deciding, stores in the memory.\r\n"+
                        " mode : SPEC, CONT, MIN, AVR, MAX.\r\n"+
                        " SPEC : When the data which hits to an min-max limit scope comes in the within the set number of times just. (range : 1~1000 EA)\r\n"+
                        " CONT : As the set number of times continuously in min-max limit scope and when comes just.(range : 1~30 EA)\r\n"+
                        " MIN, AVR, MAX : For measurement time the minimum value which is measured, mean value and maximum extraction. (range : 300 ~ 10000ms)\r\n"

            },

            new CommandDescriptions()
            {
            CMD = CMDs.RES,
            Condition1 = "MUX(/RELAY)",
            Oper = "RANGE",
            IsListOper = true,
            OperList = CommandDescriptions.CommandMode_DMM_RESrange,
            IsListCondition2 = true,
            Condition2List= CommandDescriptions.CommandMode_DMMresol,
            Condition2 = "RESOL",
            Spect = "SPEC",
            Min = "MIN",
            Max = "MAX",
            Mode = "MODE",
            IsListMode = true,
            ModeList = CommandDescriptions.CommandMode_DMM,
            Count = "COUNT/TIME",
            Description =" Uses DMM and measures Resistance value.\r\n"+
                        " The 'Range' selects DMM voltage measuring range. The 'M No' uses when storing a measurement result in the memory. There is a 'spec' and after comparison deciding, stores in the memory.\r\n"+
                        " mode : SPEC, CONT, MIN, AVR, MAX.\r\n"+
                        " SPEC : When the data which hits to an min-max limit scope comes in the within the set number of times just. (range : 1~1000 EA)\r\n"+
                        " CONT : As the set number of times continuously in min-max limit scope and when comes just.(range : 1~30 EA)\r\n"+
                        " MIN, AVR, MAX : For measurement time the minimum value which is measured, mean value and maximum extraction. (range : 300 ~ 10000ms)\r\n"

            },

            new CommandDescriptions()
            {
            CMD = CMDs.URD,
            Oper = "PORT",
            IsListOper = true,
            OperList= CommandDescriptions.CommandMode_UUT_Port,
            Condition2 = "COUNT",
            Description ="In the UUT port, data input of the port (P1/P2) which is set is a buffering.\r\n"+
                         "When inputs a 'Count', as the frame of the count which is set the bay restrictively is a buffering"
            },

            new CommandDescriptions()
            {
            CMD = CMDs.UTN,
            Condition1 = "NAMING",
            IsListCondition1 = true,
            Condition1List = CommandDescriptions.TXnaming,
            Oper = "PORT",
            IsListOper = true,
            OperList= CommandDescriptions.CommandMode_UUT_Port,
            Condition2 = "LIMIT TIME",
            Spect = "BUFFER",
            IsListSpect = true,
            SpectList = new List<string> { "NONE", "RX BUF"},
            Min = "TRY COUNT",
            Mode = "MODE",
            IsListMode = true,
            ModeList= CommandDescriptions.CommandMode_UUT,
            Count = "SET TIME",
            Description ="In the UUT port, in the port (P1/P2) which is set outputs a Tx Naming data. The contents of data uses Hex format. there is not a data and uses the transmission memory buffer.\r\n"+
                        "mode : NORMAL/R-WAIT/SEND-R/TIMER  (Buffer only  R-WAIT or SEND-R modes will be able to use)\r\n"+
                        "NORMAL : Any restriction without data rightly transmission.\r\n"+
                        "R-WAIT : When the command execute is accomplished from, restrictive time (ms) periods the case which will be reception particulars of the corresponding pots, set time (ms) after, transmits a data.\r\n"+
                        "SEND-R : After Data transmitting, restrictive time (ms) periods confirms the data reception and delay for set time (ms). (Use the Try Cnt),  CHANGE-R : after UTD Data changing, restrictive time (ms) periods confirms the data reception and delay for set time (ms).\r\n"+
                        "TIMER  : Transmits a data at set time(ms) period. When cancels a transmission, sets a data in the blank Data and accomplishes or Set time is 0.\r\n"

            },

            new CommandDescriptions()
            {
            CMD = CMDs.UTX,
            Condition1 = "DATA (HEX)",
            Oper = "PORT",
            IsListOper = true,
            OperList= CommandDescriptions.CommandMode_UUT_Port,
            Condition2 = "LIMIT TIME",
            Spect = "BUFFER",
            IsListSpect = true,
            SpectList = new List<string> { "NONE", "RX BUF"},
            Min = "TRY COUNT",
            Mode = "MODE",
            IsListMode = true,
            ModeList= CommandDescriptions.CommandMode_UUT,
            Count = "SET TIME",
            Description ="In the UUT port, in the port (P1/P2) which is set outputs a Tx Naming data. The contents of data uses Hex format. there is not a data and uses the transmission memory buffer.\r\n"+
                        "mode : NORMAL/R-WAIT/SEND-R/TIMER  (Buffer only  R-WAIT or SEND-R modes will be able to use)\r\n"+
                        "NORMAL : Any restriction without data rightly transmission.\r\n"+
                        "R-WAIT : When the command execute is accomplished from, restrictive time (ms) periods the case which will be reception particulars of the corresponding pots, set time (ms) after, transmits a data.\r\n"+
                        "SEND-R : After Data transmitting, restrictive time (ms) periods confirms the data reception and delay for set time (ms). (Use the Try Cnt),  CHANGE-R : after UTD Data changing, restrictive time (ms) periods confirms the data reception and delay for set time (ms).\r\n"+
                        "TIMER  : Transmits a data at set time(ms) period. When cancels a transmission, sets a data in the blank Data and accomplishes or Set time is 0.\r\n"

            },

            new CommandDescriptions()
            {
            CMD = CMDs.UCN,
            Condition1 = "RX DATA NAME",
            IsListCondition1 = true,
            Condition1List= CommandDescriptions.RXnaming,
            Oper = "PORT",
            IsListOper = true,
            OperList = CommandDescriptions.CommandMode_UUT_Port,
            //Condition2 = "TX DATA NAME",
            //IsListCondition2 = true,
            //Condition2List= CommandDescriptions.TXnaming,
            Spect = "SPEC",
            Min = "MIN",
            Max = "MAX",
            Mode = "MODE",
            IsListMode = true,
            ModeList = CommandDescriptions.CommandMode_UUT,
            Count = "SET TIME",
            Description ="In the UUT port, data which has become the bufferring of the port which is set, seeks the Rx Data Name which same to a corresponding condition and compares, in Memory substitutes.\r\n"+
                            "Input the min & max spec with Rx Data Name table type format. (HEX , DEC or ASC)\r\n"+
                            "ASCII Rx Naming type is selected if the data input is compared to the 'spec'.  If the 'Tx Data Name' is input to the naming After the comparison, the transmission data is transmitted.\r\n"+
                            "Mode - NORMAL : recieved data compare.\r\n"+
                            "WAIT : To collect data in the same format, then compare the Rx Data Name\r\n"+
                            "W-DATA : Collecting data with the Rx Data Name format in the same upper and lower limit comparison data.\r\n"
            },

            //new CommandDescriptions()
            //{
            //CMD = CMDs.UCP,
            //Condition1 = "LOC-DATA",
            //Oper = "PORT",
            //IsListOper = true,
            //OperList = CommandDescriptions.CommandMode_UUT_Port,
            //Condition2 = "GET LOC",
            //Spect = "SPEC",
            //Min = "MIN",
            //Max = "MAX",
            //Description ="In the UUT port, data which has become the bufferring of the port which is set, seeks the data which same to a corresponding condition and compares, in Memory substitutes.\r\n"+
            //                "The case which will use Channel at multiple, '/'.  (ex. 1-E5/2-00/3-11)\r\n"+
            //                "When the buffer data where the data of condition agrees exists, the min-max limit and compares the data of collection location, substitutes in Memory.\r\n"+
            //                "When data comparing, the case which will use a collection location at multiple,'/','~' with divides. (ex. 3/6/10~12)\r\n"+
            //                "When substituting in Memory, will not be able to use a collection location at multiple."
            //},

            new CommandDescriptions()
            {
            CMD = CMDs.STL,
            Oper = "SAMPLING",
            Condition2 = "LIMIT",
            Description ="Starts the incoming data collection of Level B/d. (Max 4000 count)\r\n"+
                            "The sampling speed sets with normality 100ms.\r\n"+
                            "When does not set a Limit-count, when EDL command are executed until, collects a data.\r\n"
            },

            new CommandDescriptions()
            {
            CMD = CMDs.EDL,
            Description ="Stop the incoming data collection of Level B/d.\r\n"
            },

            new CommandDescriptions()
            {
            CMD = CMDs.LCC,
            Condition1 = "CH",
            Oper = "Status",
            IsListOper = true,
            OperList= new List<string>(){"H", "L" },
            Condition2 = "SKIP",
            Description ="In incoming data of Level B/d, was the condition of data of corresponding Channel maintained compares in continuation.\r\n"+
                            "The Status selects H or L.\r\n"+
                            "The case which will use Channel at multiple, '/' With divides. (ex. 1/3/6)\r\n"+
                            "In incoming data, the number of skip-count which is set is excepted from the comparative object"
            },

            new CommandDescriptions()
            {
            CMD = CMDs.LEC,
            Condition1 = "CH",
            Oper = "Status",
            IsListOper = true,
            OperList= new List<string>(){"H", "L" },
            Condition2 = "SKIP",
            Spect = "SPEC",
            Description ="In incoming data of Level B/d, was the condition of data of corresponding Channel maintained compares in continuation.\r\n"+
                            "The Status selects H or L.\r\n"+
                            "The case which will use Channel at multiple, '/' With divides. (ex. 1/3/6)\r\n"+
                            "In incoming data, the number of skip-count which is set is excepted from the comparative object"
            },


            new CommandDescriptions()
            {
            CMD = CMDs.LSQ,
            Condition1 = "CH",
            Oper = "Status",
            IsListOper = true,
            OperList= new List<string>(){"H", "L" },
            Condition2 = "SKIP",
                Spect = "STD",
            Description ="In incoming data of Level B/d, compares the change of state order of data of Channel between.\r\n" +
                         "The Status selects H or L.\r\n" +
                         "Channel inputs 2 or more certainly, comparative order as inputs.\r\n" +
                         "The case which will use Channel at multiple, '/' With divides. (ex. 1/3/6) \r\n" +
                         "In incoming data, the number of skip-count which is set is excepted from the comparative object. \r\n" +
                         "Std : The specified channel to launch comparison of change point.\r\n"
            },
            new CommandDescriptions()
            {
            CMD = CMDs.LTM,
            Condition1 = "CH",
            Oper = "Status",
            IsListOper = true,
            OperList= new List<string>(){"HL", "LH", "HH","LL" },
            Condition2 = "SKIP",
            //Spect = "SPEC",
            Min = "MIN",
            Max = "MAX",
            Description ="In incoming data of Level B/d, compares the change of state duration of data of set Channel.\r\n" +
                         "The Status selects HL or LH or HH or LL.\r\n" +
                         "Channel order as must input only 1 or 2.\r\n" +
                         "The case which will use Channel at multiple, '/' With divides. (ex. 1/3/6) \r\n" +
                         "In incoming data, the number of skip-count which is set is excepted from the comparative object. \r\n" 
            },

            new CommandDescriptions()
            {
            CMD = CMDs.CAL,
            Condition1 = "CALCULATION",
            Oper = "MODE",
            IsListOper = true,
            OperList= new List<string>(){"REAL", "HEX"},
            Spect = "SPEC",
            Min = "MIN",
            Max = "MAX",
            Description ="After operation calculating, the min-max limit and compares, operation input in the memory.\r\n"+
                            "Mode : REAL or HEX\r\n"+
                            "When substituting in the memory, there is a 'spec' and after operation calculating, the min-max limit and comparison after deciding in the memory, substitutes.\r\n"+
                            "Calculation : numerical '+','-','*','/' ,absolute value '_',  logical '&','|','L','R' the single operation bay will be able to use.\r\n"+
                            "When use the logical operation AND(&), OR(|), The second factor uses Binary formats."

            },
            new CommandDescriptions()
            {
            CMD= CMDs.GLED,
            Oper = "GLED hex data",
            Description = "Read all Gled of PCB, return value as HEX number."
            },

            new CommandDescriptions()
            {
            CMD= CMDs.LED,
            Oper = "LED hex data",
            Condition2 = "Scan time",
            Description = "Read all LED of PCB, return value as HEX number."
            },

            new CommandDescriptions()
            {
            CMD= CMDs.FND,
            // Condition1/Min/Max are deliberately NOT declared - undeclared means "not use", which greys the
            // cells out. Measured on every model still in use: all 8 FND steps in ROT.vmdl have Condition1="",
            // Min="" and Max="" and drive the check purely from Oper (the expected string, e.g. "88" / "nF")
            // plus Condition2 (scan time). Only the retired Dual_Disp models ever set them.
            Oper = "String detected",
            Condition2 = "Scan time",
            Description = "Read SEGMENT value as string, compare with \"Oper\". Read until the same value with \"Oper\""
            },

            new CommandDescriptions()
            {
            CMD= CMDs.SEV,
            Condition1 = "Type",
            IsListCondition1 = true,
            Condition1List = new List<string>(){ "Icon","String" },
            Oper = "Icon Selection",
            Spect = "String Compare",
            Condition2 = "Scan time",
            Description = "Spect: The case which will use String, compare with \"Spect\" \r\n"
                        + "Oper: The case which will use Icon at multiple, '/', '~' With divides. (ex. 3/6/10~12) \r\n"
            },

            new CommandDescriptions()
            {
            CMD= CMDs.LCD,
            Oper = "String detected",
            Condition2 = "Scan time",
            Description = "Read all character in LCD of PCB as string, compare with min value."
            },

            new CommandDescriptions()
            {
            CMD = CMDs.PCB,
            Condition1 = "SELECT PCB",
            Description ="Command is for test only the selected PCB.\r\n"+
                            "select PCB : You can use any combination of 1, 2, 3, 4 PCB.\r\n"+
                            "ex) 1/2/3/4 or 1/2"

            },

            new CommandDescriptions()
            {
            CMD = CMDs.CAM,
            Condition1 = "Property",
            IsListCondition1 = true,
            Condition1List = Enum.GetNames(typeof(VTMControls.DeviceControl.CameraControl.VideoProperties)).ToList(),
            Oper = "Value",
            Description ="Apply setting to camera device.\r\n",
            },

            new CommandDescriptions()
            {
            CMD = CMDs.MOT,
            Condition1 = "Sub Command",
            IsListCondition1 = true,
            Condition1List = new List<string>(){ "READ", "RPM", "CMP Voltage U", "CMP Voltage W", "CMP Voltage V", "CMP Voltage UW", "CMP Voltage WV", "CMP Voltage VU", "CMP Current U", "CMP Current W", "CMP Current V" },
            Condition2 = "Direction rotation",
            IsListCondition2 = true,
            Condition2List = new List<string>(){ "CW","ACW","CW/ACW"},
            Oper = "Channels",
            IsListOper = true,
            OperList= new List<string>(){"1"},
            Spect = "Scan time",
            Min = "MIN",
            Max = "MAX",
            Description = "Measuring param of 3 phase motor at \"READ\" condition.\r\n"+
                          "RPM mode measure the RPM of motor, channel 1~2 \r\n"+
                          "The positive sign represents clockwise rotation (CW) \r\n" +
                          "The negative sign represents counterclockwise rotation (ACW) \r\n"
            },

            new CommandDescriptions()
            {
            CMD = CMDs.SND,
            Oper = "Mode",
            IsListOper = true,
            OperList = new List<string>(){ "START", "STOP", "CHECK" },
            Condition2 = "ROIs",
            Description = "Sound processing test.\r"
                        + "Mode START: begin mic capture\r"
                        + "Mode STOP: stop capture\r"
                        + "Mode CHECK: verify the global ROIs. Condition2 empty = check ALL ROIs;\r"
                        + "or list ROI indices to check a subset (e.g. 1/3/5 or 1~3, 1-based).\r"
                        + "ROIs are global (edit them on the SOUND PROCESSING page)."
            }
        };

        public static void UpdateCommand()
        {
            foreach (var item in Commands)
            {
                item.TestContentsList = CommandDescriptions.QRnaming;

                if (item.Condition1 == "NAMING")
                {
                    item.Condition1List = CommandDescriptions.TXnaming;
                }
                else if (item.Condition1 == "RX DATA NAME")
                {
                    item.Condition1List = CommandDescriptions.RXnaming;
                }

                if (item.Condition2 == "TX DATA NAME")
                {
                    item.Condition2List = CommandDescriptions.TXnaming;
                }
            }
        }
    }
}
