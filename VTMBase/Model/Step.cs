using VTMControls.DeviceControl;
using VTMUtility;
// Models are saved with System.Text.Json (Extensions.ConvertToJson -> AutoPage.LoadModel), so [JsonIgnore] must
// resolve to ITS attribute. This used to be "using Newtonsoft.Json;", which made every [JsonIgnore] below a
// Newtonsoft attribute that System.Text.Json does not understand - so all the runtime-only fields marked below
// were being written into every .vmdl anyway. Newtonsoft has since been removed from the whole solution (the
// SoundStepConfig revert snapshot now also uses System.Text.Json), so STJ's JsonIgnore is the only one that exists.
using System.Text.Json.Serialization;
using OpenCvSharp.Flann;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;

namespace VTMBase
{
    // Step test
    public class Step : INotifyPropertyChanged
    {
        public const string DontCare = "exe";
        public const string Ok = "OK";
        public const string Ng = "NG";

        private int no;

        // No is a runtime display index only (row number). It is NOT persisted - it is derived from the step's
        // position in Model.Steps (index + 1) whenever the collection is assigned (see Model.Steps setter).
        [JsonIgnore]
        public int No
        {
            get { return no; }
            set
            {
                if (no != value)
                {
                    no = value;
                    NotifyPropertyChanged("No");
                }
            }
        }

        public string IMQSCode { get; set; }

        public string testContent { get; set; }

        public string TestContent
        {
            get
            {
                CommandDescriptions.IsListTestContent = false;
                return testContent;
            }
            set
            {
                if (testContent != value)
                {
                    testContent = value;
                    NotifyPropertyChanged(nameof(TestContent));

                    if (testContent != null)
                    {
                        CommandDescriptions.IsListTestContent = Naming.qrDatas.Where(x => x.Context.StartsWith(value)).ToList().Count > 0;
                        IMQSCode = Naming.qrDatas.Where(x => x.Context == value).Select(x => x.Code).DefaultIfEmpty("").First();
                        NotifyPropertyChanged(nameof(IMQSCode));
                    }
                    else
                    {
                        IMQSCode = "";
                        NotifyPropertyChanged(nameof(IMQSCode));
                    }
                }
            }
        }

        private ObservableCollection<SingleLED> ledList = new ObservableCollection<SingleLED>();

        public ObservableCollection<SingleLED> LedList
        {
            get
            {
                return ledList;
            }
            set
            {
                if (ledList != value)
                {
                    ledList = value;
                }
            }
        }

        private ObservableCollection<bool> _UseLed = new ObservableCollection<bool>();

        public ObservableCollection<bool> UseLed
        {
            get
            {
                return _UseLed;
            }
            set
            {
                if (_UseLed != value)
                {
                    _UseLed = value;
                }
            }
        }

        private List<Rect> _RectFNDsBoard0 = new List<Rect>();

        public List<Rect> RectFNDsBoard0
        {
            get
            {
                return _RectFNDsBoard0;
            }

            set
            {
                _RectFNDsBoard0 = value;
            }
        }

        private List<bool> _UseFNDsBoard0 = new List<bool>();

        public List<bool> UseFNDsBoard0
        {
            get
            {
                return _UseFNDsBoard0;
            }

            set
            {
                _UseFNDsBoard0 = value;
            }
        }

        private List<FND> _FNDsBoard0 = new List<FND>();

        public List<FND> FNDsBoard0
        {
            get
            {
                return _FNDsBoard0;
            }

            set
            {
                _FNDsBoard0 = value;
            }
        }

        // Sound ROIs are now GLOBAL on the model (Model.SoundConfig), not per-step. Each SND CHECK step
        // picks which ROIs to verify via its Condition2 index list - see SoundPage / Program.SND.

        private Rect? lcdroiValue1;

        public Rect? LCDRoiValue0
        {
            get
            {
                return lcdroiValue1;
            }

            set
            {
                lcdroiValue1 = value;
            }
        }

        private Rect? lcdroiValue2;

        public Rect? LCDRoiValue1
        {
            get
            {
                return lcdroiValue2;
            }

            set
            {
                lcdroiValue2 = value;
            }
        }

        private Rect? lcdroiValue3;

        public Rect? LCDRoiValue2
        {
            get
            {
                return lcdroiValue3;
            }

            set
            {
                lcdroiValue3 = value;
            }
        }

        private Rect? lcdroiValue4;

        public Rect? LCDRoiValue3
        {
            get
            {
                return lcdroiValue4;
            }

            set
            {
                lcdroiValue4 = value;
            }
        }

        public string CMD
        {
            get { return cmd.ToString(); }
            set
            {
               
                CMDs ds = CMDs.NON;
                if (Enum.TryParse<CMDs>(value, out ds))
                {
                    cmd = ds;
                    CommandDescriptions = Command.Commands.SingleOrDefault(x => x.CMD == cmd);
                    NotifyPropertyChanged("CommandDescriptions");
                    NotifyPropertyChanged("CMD");
                    if (cmd == CMDs.UTN)
                    {
                        CommandDescriptions.IsListRemark = true;
                    }
                }
            }
        }

        public CMDs cmd
        {
            get;
            set;
        }

        [JsonIgnore]
        public CMDs SetCMD
        {
            get { return cmd; }
            set
            {
                cmd = value;
                NotifyPropertyChanged("CMD");
                CommandDescriptions = Command.Commands.SingleOrDefault(x => x.CMD == cmd);
                NotifyPropertyChanged("CommandDescriptions");
            }
        }

        private CommandDescriptions _CommandDescriptions = new CommandDescriptions();

        // [JsonIgnore] MUST sit here, on the PUBLIC property - System.Text.Json only looks at public members, so
        // the attribute this used to carry on the private backing field above was a silent no-op.
        //
        // This is not just file size. CommandDescriptions is the static command TABLE (Command.Commands), derived
        // from cmd by the CMD/SetCMD setters - it is not per-step data. Persisting it froze a COPY of the table
        // into every step, and on load that stale copy OVERRODE the canonical one: in the production ok.vmdl, 2 of
        // 5 SND steps came back with Condition2 = "not use" while the real table says "ROIs", i.e. they were
        // running on metadata from before SND ROIs existed. Ignoring it lets every step get the current table.
        [JsonIgnore]
        public CommandDescriptions CommandDescriptions
        {
            get { return _CommandDescriptions; }
            set
            {
                if (value != null && value != _CommandDescriptions)
                {
                    _CommandDescriptions = value;
                    NotifyPropertyChanged("CommandDescriptions");
                    Condition1 = CommandDescriptions.Condition1 == "not use" ? "" : Condition1;
                    Condition2 = CommandDescriptions.Condition2 == "not use" ? "" : Condition2;
                    Oper = CommandDescriptions.Oper == "not use" ? "" : Oper;
                    Min = CommandDescriptions.Min == "not use" ? "" : Min;
                    Max = CommandDescriptions.Max == "not use" ? "" : Max;
                    Spect = CommandDescriptions.Spect == "not use" ? "" : Spect;
                    Mode = CommandDescriptions.Mode == "not use" ? "" : Mode;
                    Count = CommandDescriptions.Count == "not use" ? "0" : Count;
                }
            }
        }

        private string _condition1;

        [JsonIgnore]
        public string Condition1Tooltip { get; set; } = "";

        public string Condition1
        {
            get
            {
                if (CommandDescriptions.Condition1 == "NAMING")
                {
                    var selected = Naming.txDatas.Where(o => o.Name == _condition1).FirstOrDefault();
                    if (selected != null)
                        Condition1Tooltip = selected.Data;
                }

                if (CommandDescriptions.Condition1 == "RX DATA NAME")
                {
                    var selected = Naming.rxDatas.Where(o => o.Name == _condition1).FirstOrDefault();
                    if (selected != null)
                    {
                        Condition1Tooltip = selected.ToTooltipString();
                    }
                }
                return _condition1;
            }
            set
            {
                _condition1 = value;
                NotifyPropertyChanged(nameof(Condition1));
            }
        }

        private string _Oper;

        public string Oper
        {
            get { return _Oper; }
            set
            {
                if (value != null && value != _Oper)
                {
                    _Oper = value;
                    NotifyPropertyChanged("Oper");
                }
            }
        }

        private string _Condition2;

        public string Condition2
        {
            get { return _Condition2; }
            set
            {
                if (value != null && value != _Condition2)
                {
                    _Condition2 = value;
                    NotifyPropertyChanged("Condition2");
                }
            }
        }

        private string _Spect;

        public string Spect
        {
            get { return _Spect; }
            set
            {
                if (value != null && value != _Spect)
                {
                    _Spect = value;
                    NotifyPropertyChanged("Spect");
                }
            }
        }

        [JsonIgnore]
        public string Min_Max
        {
            get
            {
                if (CommandDescriptions.Min == "MIN" && CommandDescriptions.Max == "MAX")
                {
                    return min + "~" + max;
                }
                else if (cmd == CMDs.UTN)
                {
                    return Condition1;
                }
                else if (CommandDescriptions.Spect != "not use")
                {
                    return Spect;
                }
                else if (CommandDescriptions.Oper != "not use")
                {
                    return Oper;
                }
                else
                {
                    return Condition2;
                }
            }
        }

        private string min;

        public string Min
        {
            get { return min; }
            set
            {
                if (value != min)
                {
                    min = value;
                    NotifyPropertyChanged(nameof(Min));
                }
            }
        }

        private string max;

        public string Max
        {
            get { return max; }
            set
            {
                if (value != max)
                {
                    max = value;
                    NotifyPropertyChanged(nameof(Max));
                }
            }
        }

        private string ValueGet1data = "";
        private string ValueGet2data = "";
        private string ValueGet3data = "";
        private string ValueGet4data = "";

        private string Result1data = "";
        private string Result2data = "";
        private string Result3data = "";
        private string Result4data = "";

        private string mode;

        public string Mode
        {
            get
            {
                return mode;
            }
            set
            {
                mode = value;
                NotifyPropertyChanged("Mode");
            }
        }

        private int count;

        public string Count
        {
            get { return count.ToString(); }
            set
            {
                if (int.TryParse(value, out int cnt))
                {
                    count = cnt;
                    NotifyPropertyChanged("Count");
                }
            }
        }

        public string Mem { get; set; }
        public int E_Jump { get; set; }
        private string remark;

        public string Remark
        {
            get { return remark; }
            set
            {
                if (value != remark)
                {
                    remark = value;
                    NotifyPropertyChanged("Remark");
                }
            }
        }

        public string ELoc { get; set; }
        public bool Skipdata { get; set; }

        // Result / Value / ResultValue are TEST-RUN outputs, not part of the model template. They are runtime only
        // and never serialized; on load they take their defaults (Result = false, Value = null, ResultValue = null).
        [JsonIgnore]
        public bool Result { get; set; }
        private string value_save;

        [JsonIgnore]
        public string Value
        {
            get { return value_save; }
            set
            {
                if (value != null && value != "")
                {
                    value_save = value;
                }
            }
        }

        private string resultValue;

        [JsonIgnore]
        public string ResultValue
        {
            get { return resultValue; }
            set
            {
                if (value != null && value != "")
                {
                    resultValue = value;
                }
            }
        }

        [JsonIgnore]
        public string ValueGet1
        {
            get { return ValueGet1data; }
            set
            {
                if (value == null) value = "-";
                if (value != this.ValueGet1data)
                {
                    this.ValueGet1data = value;
                    NotifyPropertyChanged(nameof(ValueGet1));
                }
            }
        }

        [JsonIgnore]
        public string ValueGet2
        {
            get { return ValueGet2data; }
            set
            {
                if (value == null) value = "-";
                if (value != this.ValueGet2data)
                {
                    if (value == null) value = "-";
                    this.ValueGet2data = value;
                    NotifyPropertyChanged(nameof(ValueGet2));
                }
            }
        }

        [JsonIgnore]
        public string ValueGet3
        {
            get { return ValueGet3data; }
            set
            {
                if (value == null) value = "-";
                if (value != this.ValueGet3data)
                {
                    this.ValueGet3data = value;
                    NotifyPropertyChanged(nameof(ValueGet3));
                }
            }
        }

        [JsonIgnore]
        public string ValueGet4
        {
            get { return ValueGet4data; }
            set
            {
                if (value == null) value = "-";
                if (value != this.ValueGet4data)
                {
                    this.ValueGet4data = value;
                    NotifyPropertyChanged(nameof(ValueGet4));
                }
            }
        }

        [JsonIgnore]
        public string Result1
        {
            get { return Result1data.ToString(); }
            set
            {
                if (value != this.Result1data)
                {
                    this.Result1data = value;
                    NotifyPropertyChanged();
                }
            }
        }

        [JsonIgnore]
        public string Result2
        {
            get { return Result2data; }
            set
            {
                if (value != this.Result2data)
                {
                    this.Result2data = value;
                    NotifyPropertyChanged();
                }
            }
        }

        [JsonIgnore]
        public string Result3
        {
            get { return Result3data; }
            set
            {
                if (value != this.Result3data)
                {
                    this.Result3data = value;
                    NotifyPropertyChanged();
                }
            }
        }

        [JsonIgnore]
        public string Result4
        {
            get { return Result4data; }
            set
            {
                if (value != this.Result4data)
                {
                    this.Result4data = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool Skip
        {
            get { return Skipdata; }
            set
            {
                if (value != this.Skipdata)
                {
                    this.Skipdata = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void InitialFND()
        {
            // Idempotent on purpose: it must be safe to call on a step that already has a store (e.g. one just
            // loaded from a file), or it would append a second set of 7 and grow the model on every save.
            if (FNDsBoard0.Count >= VisionModel.CHARNUMBER) return;

            FNDsBoard0.Clear();
            RectFNDsBoard0.Clear();
            UseFNDsBoard0.Clear();
            for (int index = 0; index < VisionModel.CHARNUMBER; index++)
            {
                FNDsBoard0.Add(new FND(index));
                RectFNDsBoard0.Add(new Rect((VisionModel.FND_WIDTH + 10) * index + 10, 10, VisionModel.FND_WIDTH, VisionModel.FND_HEIGHT));
                // Placeholder/unassigned step: every ROI slot starts NOT used, so a fresh step shows no active
                // boxes. The user enables the digit positions they need when configuring an FND step.
                UseFNDsBoard0.Add(false);
            }
        }

        // Does this step's cmd actually use vision ROIs at all?
        [JsonIgnore]
        public bool IsVisionCmd
        {
            get { return cmd == CMDs.FND || cmd == CMDs.LCD || cmd == CMDs.LED; }
        }

        // Is there a COMPLETE FND store here to index into?
        //
        // Every consumer loops a hardcoded CHARNUMBER times over FNDsBoard0 / RectFNDsBoard0 / UseFNDsBoard0,
        // bounded by the LIVE model rather than by the step's own Count, so a short list is an
        // ArgumentOutOfRangeException - including at TEST TIME (AutoPage/ManualPage.UpdateFndRoi). Since a
        // non-FND step's store is now empty BY DESIGN, and a hand-edited or half-written file can leave any of
        // the three lists short independently (they are three separate public setters and STJ replaces each
        // wholesale), every one of those sites tests this first.
        //
        // The cmd check is the policy; this is the airbag. Both, deliberately: the policy has been wrong before.
        [JsonIgnore]
        public bool HasFndStore
        {
            get
            {
                return FNDsBoard0 != null && FNDsBoard0.Count >= VisionModel.CHARNUMBER
                    && RectFNDsBoard0 != null && RectFNDsBoard0.Count >= VisionModel.CHARNUMBER
                    && UseFNDsBoard0 != null && UseFNDsBoard0.Count >= VisionModel.CHARNUMBER;
            }
        }

        // Drop every ROI family this step's cmd does not use, so a step only ever carries its own data.
        //
        // Measured on real models, this is where the file size lives: SOLEINOID has 26/26 steps carrying a full
        // 7-char FND store with ZERO FND steps, ok.vmdl 5/5 with zero, ROT 38/38 with only 8. FNDsBoard0 alone is
        // ~88% of the Steps section.
        //
        // NOT called from the cmd setter, and it must never be: `cmd` and `CMD` are both assigned by the
        // deserializer, and the key order differs between models written by different app versions - in ok.vmdl
        // CMD sits at position 14, AFTER LedList(5)/FNDsBoard0(9)/LCDRoiValue0(10), so clearing from that setter
        // would wipe the ROIs the file had just loaded. Call it from the UI (a real cmd change) and at save.
        //
        // LCDRoiValue0..3 are Rect? - a plain Rect could not be null and a zeroed one still serialized as a
        // ~283-byte object of zeros (System.Windows.Rect writes all 15 of its computed properties). Null writes
        // 4 bytes and says what it means: this step has no LCD ROI. Old models carrying a full Rect deserialize
        // into the nullable unchanged.
        public void ScopeRoisToCmd()
        {
            if (cmd != CMDs.FND)
            {
                FNDsBoard0.Clear();
                RectFNDsBoard0.Clear();
                UseFNDsBoard0.Clear();
            }
            else
            {
                InitialFND();   // an FND step must have its 7 slots
            }

            if (cmd != CMDs.LED) LedList.Clear();

            if (cmd != CMDs.LCD)
            {
                LCDRoiValue0 = null;
                LCDRoiValue1 = null;
                LCDRoiValue2 = null;
                LCDRoiValue3 = null;
            }
        }

        public Step()
        {
            InitialFND();
        }

        public Step(int Index)
        {
            No = Index;
            InitialFND();
        }
    }
}
