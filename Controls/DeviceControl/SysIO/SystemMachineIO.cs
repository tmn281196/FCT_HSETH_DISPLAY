using Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Controls
{
    public class SystemMachineIO : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static readonly object _logLock = new object();
        private static void LogDiag(string category, string message)
        {
            try
            {
                lock (_logLock)
                {
                    string dir = @"C:\log";
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(dir, $"diag_{DateTime.Now:yyyy-MM-dd}.txt"),
                        $"{DateTime.Now:HH:mm:ss.fff} [{category}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }

        public event EventHandler OnStartRequest;

        public event EventHandler OnManualStartRequest;

        public event EventHandler OnCancleRequest;

        public event EventHandler OnDoorStateChange;

        public event EventHandler OnUpDown;

        // state define
        public const bool ON = true;

        public const bool OFF = false;

        private bool _BoardA;

        public bool BoardA
        {
            get { return _BoardA; }
            set
            {
                if (value != _BoardA) _BoardA = value;
                OnPropertyChanged("BoardA");
            }
        }

        private bool _BoardB;

        public bool BoardB
        {
            get { return _BoardB; }
            set
            {
                if (value != _BoardB) _BoardB = value;
                OnPropertyChanged("BoardB");
            }
        }

        private bool _BoardC;

        public bool BoardC
        {
            get { return _BoardC; }
            set
            {
                if (value != _BoardC) _BoardC = value;
                OnPropertyChanged("BoardC");
            }
        }

        private string _BoardD;

        public string BoardD
        {
            get { return _BoardD; }
            set
            {
                if (value != _BoardD) _BoardD = value;
                OnPropertyChanged("BoardD");
            }
        }

        // Machine SYSTEM INPUT
        // The SW_* switch properties are gone: the board never reported them (the v1 frame hardcoded those bits
        // to 0) and nothing read them. Their setters drove MainUP (SW_UP -> MainUP=value, SW_DOWN -> MainUP=!value),
        // which is exactly what made a periodic input decode cut UUT power. v2 addresses inputs by key, so the
        // hazard is removed at the source.

        /// <summary>
        /// Emc button
        /// </summary>
        private bool _SW_EMC;

        public bool SW_EMC
        {
            get { return _SW_EMC; }
            set
            {
                if (value != _SW_EMC)
                    _SW_EMC = value;
                OnPropertyChanged("SW_EMC");
                OnPropertyChanged("NotEMC");
                OnPropertyChanged("CanCommandCylinder");
            }
        }

        public bool NotEMC
        {
            get { return !_SW_EMC; }
        }

        /// <summary>
        /// Sensor door open/close true/false;
        /// </summary>
        private bool _IsDoorOpen;

        public bool IsDoorOpen
        {
            get { return _IsDoorOpen; }
            set
            {
                if (value != _IsDoorOpen)
                {
                    _IsDoorOpen = value;
                    OnPropertyChanged("IsDoorOpen");
                }
                OnDoorStateChange?.Invoke(_IsDoorOpen, null);
            }
        }

        /// <summary>
        /// button start pressed to fire manual testing
        /// </summary>
        private bool _BTN_START_MANUAL;

        public bool BTN_START_MANUAL
        {
            get { return _BTN_START_MANUAL; }
            set
            {
                if (value != _BTN_START_MANUAL)
                {
                    if (_BTN_START_MANUAL == OFF)
                    {
                        OnManualStartRequest?.Invoke(null, null);
                    }

                    _BTN_START_MANUAL = value;
                    OnPropertyChanged("BTN_START_MANUAL");
                }
            }
        }

        /// <summary>
        /// button stop pressed to stop testing (AUTO/MANUAL)
        /// </summary>
        private bool _BTN_STOP_ALL;

        public bool BTN_STOP_ALL
        {
            get { return _BTN_STOP_ALL; }
            set
            {
                if (value != _BTN_STOP_ALL)
                {
                    if (_BTN_STOP_ALL == OFF)
                    {
                        OnCancleRequest?.Invoke(null, null);
                    }

                    _BTN_STOP_ALL = value;
                    OnPropertyChanged("BTN_STOP_ALL");
                }
            }
        }

        /// <summary>
        /// Sensor main cylinder upstate
        /// </summary>
        private bool _SS_UP;

        public bool SS_UP
        {
            get { return _SS_UP; }
            set
            {
                if (value != _SS_UP)
                {
                    LogDiag("SS_UP", $"{_SS_UP}→{value}");
                    _SS_UP = value;
                }
                OnPropertyChanged("SS_UP");
            }
        }

        /// <summary>
        /// Sensor main cylinder down state
        /// </summary>
        private bool _SS_DOWN;

        public bool SS_DOWN
        {
            get { return _SS_DOWN; }
            set
            {
                if (value != _SS_DOWN)
                {
                    LogDiag("SS_DOWN", $"{_SS_DOWN}→{value}");
                    if (_SS_DOWN == OFF)
                    {
                        LogDiag("SS_DOWN", "firing OnStartRequest");
                        OnStartRequest?.Invoke(null, null);
                    }
                    if (_SS_DOWN == ON)
                    {
                        LogDiag("SS_DOWN", "firing OnCancleRequest");
                        OnCancleRequest?.Invoke(null, null);
                    }
                    _SS_DOWN = value;
                    OnPropertyChanged("SS_DOWN");
                    OnPropertyChanged("MainUP_Actual");
                    OnPropertyChanged("CanCommandCylinder");

                    // Mirror firmware else branch: when SDOWN is LOW, force MainUP to false
                    if (!_SS_DOWN && _MainUP)
                    {
                        _MainUP = false;
                        OnPropertyChanged("MainUP");
                        OnPropertyChanged("MainUP_Actual");
                    }
                }
                OnUpDown?.Invoke("DOWN", null);
            }
        }

        /// <summary>
        /// Sensor card release on Bot side
        /// </summary>
        private bool _SS_BR;

        public bool SS_BR
        {
            get { return _SS_BR; }
            set
            {
                if (value != _SS_BR) _SS_BR = value;
                OnPropertyChanged("SS_BR");
            }
        }

        /// <summary>
        /// Sensor card inserted on Bot side
        /// </summary>
        private bool _SS_BF;

        public bool SS_BF
        {
            get { return _SS_BF; }
            set
            {
                if (value != _SS_BF) _SS_BF = value;
                Card_BOT_LOCK = value;
                OnPropertyChanged("SS_BF");
            }
        }

        /// <summary>
        /// Sensor card release on Top side
        /// </summary>
        private bool _SS_TR;

        public bool SS_TR
        {
            get { return _SS_TR; }
            set
            {
                if (value != _SS_TR) _SS_TR = value;
                OnPropertyChanged("SS_TR");
            }
        }

        /// <summary>
        /// Sensor card inserted on Top side
        /// </summary>
        private bool _SS_TF;

        public bool SS_TF
        {
            get { return _SS_TF; }
            set
            {
                if (value != _SS_TF) _SS_TF = value;
                Card_TOP_LOCK = value;
                OnPropertyChanged("SS_TF");
            }
        }

        /// <summary>
        /// Sensor lock JIG on Bot side
        /// </summary>
        private bool _SS_BL;

        public bool SS_BL
        {
            get { return _SS_BL; }
            set
            {
                if (value != _SS_BL)
                    _SS_BL = value;
                JIG_BOT_LOCK = value;
                OnPropertyChanged("SS_BL");
            }
        }

        /// <summary>
        /// Sensor JIG locked on Top side
        /// </summary>
        private bool _SS_TL;

        public bool SS_TL
        {
            get { return _SS_TL; }
            set
            {
                if (value != _SS_TL)
                    _SS_TL = value;
                JIG_TOP_LOCK = value;
                OnPropertyChanged("SS_TL");
            }
        }

        /// <summary>
        /// Board A Microphone
        /// </summary>
        ///

        public List<int> SamplesMicA { get; set; } = new List<int>();

        private int _MIC_A = 0;

        public int MIC_A
        {
            get { return _MIC_A; }
            set
            {
                if (value != _MIC_A) _MIC_A = value;
                OnPropertyChanged("MIC_A");
                OnPropertyChanged("MIC_A_PercentOn");
                OnPropertyChanged("MIC_A_PercentOff");
            }
        }

        public GridLength MIC_A_PercentOn
        {
            get { return new GridLength((double)(_MIC_A / 1024.0), GridUnitType.Star); }
        }

        public GridLength MIC_A_PercentOff
        {
            get { return new GridLength(1 - (double)(_MIC_A / 1024.0), GridUnitType.Star); }
        }

        /// <summary>
        /// Boad B microphone
        /// </summary>
        ///
        public List<int> SamplesMicB { get; set; } = new List<int>();

        private int _MIC_B = 0;

        public int MIC_B
        {
            get { return _MIC_B; }
            set
            {
                if (value != _MIC_B) _MIC_B = value;
                OnPropertyChanged("MIC_B");
                OnPropertyChanged("MIC_B_PercentOn");
                OnPropertyChanged("MIC_B_PercentOff");
            }
        }

        public GridLength MIC_B_PercentOn
        {
            get { return new GridLength((double)(_MIC_B / 1024.0), GridUnitType.Star); }
        }

        public GridLength MIC_B_PercentOff
        {
            get { return new GridLength(1 - (double)(_MIC_B / 1024.0), GridUnitType.Star); }
        }

        public void ClearSamples()
        {
            SamplesMicA.Clear();
            SamplesMicB.Clear();
        }

        private int _SampleRate = 100;

        public int SampleRate
        {
            get { return _SampleRate; }
            set
            {
                if (value != _SampleRate) _SampleRate = value;

                OnPropertyChanged("SampleRate");
            }
        }

        // Machine SYSTEM OUTPUT
        // Only one property for the main cylinder command: MainUP.
        // MainUP = true  <=> CLUP HIGH (reset up).
        // MainUP = false <=> CLUP LOW (down).
        // The DOWN button in XAML binds inverted via InverseBooleanConverter.
        private bool _MainUP;

        public bool MainUP
        {
            get { return _MainUP; }
            set
            {
                if (_MainUP == value) return;
                // NO SS_DOWN guard here on purpose. The BOARD owns the interlock (applyOutput forces CLUP LOW
                // while its own SDOWN is LOW), and it is the authority. _SS_DOWN here is only a cached copy that
                // can be stale or momentarily wrong - second-guessing the board with it would swallow a perfectly
                // valid raise (e.g. the operator presses down before scanning) and leave the jig stuck down.
                // So always send the command: if the board refuses it, its ACK reports the real pin state and
                // SystemBoard's ACK-retry re-sends, then logs "REQUEST TIMEOUT CLUP:1" if it never takes.
                _MainUP = value;
                if (_MainUP)
                {
                    // Switching to UP: cut UUT power + door lock for safety
                    AC0 = false;
                    AC110 = false;
                    AC220 = false;
                    BC0 = false;
                    BC110 = false;
                    BC220 = false;
                    DoorLock = false;
                }
                OnPropertyChanged("MainUP");
                OnPropertyChanged("MainUP_Actual");
            }
        }

        // Actual CLUP after the firmware interlock: firmware only allows CLUP HIGH when SDOWN permits.
        // MainUP_Actual = MainUP AND SS_DOWN (SDOWN pin HIGH), equivalent to the real CLUP.
        // Getter only, UI binding one-way.
        public bool MainUP_Actual
        {
            get { return _MainUP && _SS_DOWN; }
        }

        // Allow the UP/DOWN toggle only when not EMC and SDOWN permits.
        // Firmware else branch (SDOWN LOW) forces CLUP=LOW, so toggling would be meaningless.
        public bool CanCommandCylinder
        {
            get { return NotEMC && _SS_DOWN; }
        }

        /// <summary>
        /// Software switch lock card on top
        /// </summary>
        private bool _Card_TOP_LOCK;

        public bool Card_TOP_LOCK
        {
            get { return _Card_TOP_LOCK; }
            set
            {
                if (value != _Card_TOP_LOCK) _Card_TOP_LOCK = value;
                OnPropertyChanged("Card_TOP_LOCK");
            }
        }

        /// <summary>
        /// Software switch lock card on bot
        /// </summary>
        private bool _Card_BOT_LOCK;

        public bool Card_BOT_LOCK
        {
            get { return _Card_BOT_LOCK; }
            set
            {
                if (value != _Card_BOT_LOCK) _Card_BOT_LOCK = value;
                OnPropertyChanged("Card_BOT_LOCK");
            }
        }

        /// <summary>
        /// Software switch lock JIG on top
        /// </summary>
        private bool _JIG_TOP_LOCK;

        public bool JIG_TOP_LOCK
        {
            get { return _JIG_TOP_LOCK; }
            set
            {
                if (value != _JIG_TOP_LOCK) _JIG_TOP_LOCK = value;
                OnPropertyChanged("JIG_TOP_LOCK");
            }
        }

        /// <summary>
        /// Software switch lock JIG on top
        /// </summary>
        private bool _JIG_BOT_LOCK;

        public bool JIG_BOT_LOCK
        {
            get { return _JIG_BOT_LOCK; }
            set
            {
                if (value != _JIG_BOT_LOCK) _JIG_BOT_LOCK = value;
                OnPropertyChanged("JIG_BOT_LOCK");
            }
        }

        /// <summary>
        /// Tower lamps RED light
        /// </summary>
        private bool _LPR;

        public bool LPR
        {
            get { return _LPR; }
            set
            {
                if (value != _LPR)
                    _LPR = value;
                if (value)
                {
                    LPG = false;
                    LPY = false;
                }
                OnPropertyChanged("LPR");
            }
        }

        /// <summary>
        /// Tower lamps YELLOW light
        /// </summary>
        private bool _LPY;

        public bool LPY
        {
            get { return _LPY; }
            set
            {
                if (value != _LPY)
                    _LPY = value;
                if (value)
                {
                    LPG = false;
                    LPR = false;
                }
                OnPropertyChanged("LPY");
            }
        }

        /// <summary>
        /// Tower lamps GREEN light
        /// </summary>
        private bool _LPG;

        public bool LPG
        {
            get { return _LPG; }
            set
            {
                if (value != _LPG)
                    _LPG = value;
                if (value)
                {
                    LPR = false;
                    LPY = false;
                    BUZZER = false;
                }
                OnPropertyChanged("LPG");
            }
        }

        /// <summary>
        /// Tower lamps Buzzer
        /// </summary>
        private bool _BUZZER;

        public bool BUZZER
        {
            get { return _BUZZER; }
            set
            {
                if (value != _BUZZER) _BUZZER = value;
                OnPropertyChanged("BUZZER");
            }
        }

        /// <summary>
        /// AC power 110V site A/C, on power and on AC0....load to site (A,C)
        /// </summary>
        private bool _AC110;

        public bool AC110
        {
            get { return _AC110; }
            set
            {
                if (value != _AC110) _AC110 = value;
                if (value)
                {
                    ADSC = false;
                    AC220 = false;
                    AC0 = true;
                }
                OnPropertyChanged("AC110");
            }
        }

        private bool _DoorLock;

        public bool DoorLock
        {
            get { return _DoorLock; }
            set
            {
                if (value != _DoorLock) _DoorLock = value;

                OnPropertyChanged("DoorLock");
            }
        }

        /// <summary>
        /// AC power on site A/C, on power and on AC0....load to site (A,C)
        /// </summary>
        private bool _AC0;

        public bool AC0
        {
            get { return _AC0; }
            set
            {
                if (value != _AC0) _AC0 = value;
                if (value)
                    ADSC = false;
                OnPropertyChanged("AC0");
            }
        }

        /// <summary>
        /// AC power 220V site A/C, on power and on AC0....load to site (A,C)
        /// </summary>
        private bool _AC220;

        public bool AC220
        {
            get { return _AC220; }
            set
            {
                if (value != _AC220)
                    _AC220 = value;
                if (value)
                {
                    ADSC = false;
                    AC110 = false;
                    AC0 = true;
                }
                OnPropertyChanged("AC220");
            }
        }

        /// <summary>
        /// Discharge site A/C
        /// </summary>
        private bool _ADSC1;

        public bool ADSC1
        {
            get { return _ADSC1; }
            set
            {
                if (value != _ADSC1)
                    _ADSC1 = value;
                OnPropertyChanged("ADSC1");
            }
        }

        /// <summary>
        /// Discharge site A/C
        /// </summary>
        private bool _ADSC2;

        public bool ADSC2
        {
            get { return _ADSC2; }
            set
            {
                if (value != _ADSC2) _ADSC2 = value;
                OnPropertyChanged("ADSC2");
            }
        }

        public bool ADSC
        {
            get { return ADSC1 || ADSC2; }
            set
            {
                ADSC1 = value;
                ADSC2 = value;
                if (value)
                {
                    AC0 = false;
                    AC110 = false;
                    AC220 = false;
                }
                OnPropertyChanged("ADSC1");
                OnPropertyChanged("ADSC2");
                OnPropertyChanged("ADSC");
            }
        }

        /// <summary>
        /// AC power 110V site B/D, on power and on BC0....load to site (B,D)
        /// </summary>
        private bool _BC110;

        public bool BC110
        {
            get { return _BC110; }
            set
            {
                if (value != _BC110) _BC110 = value;
                if (value)
                {
                    BC220 = false;
                    BDSC = false;
                    BC0 = true;
                }
                OnPropertyChanged("BC110");
            }
        }

        /// <summary>
        /// AC power on site B/D, on power and on BC0....load to site (B,D)
        /// </summary>
        private bool _BC0;

        public bool BC0
        {
            get { return _BC0; }
            set
            {
                if (value != _BC0) _BC0 = value;
                if (value)
                    BDSC = false;
                OnPropertyChanged("BC0");
            }
        }

        /// <summary>
        /// AC power 220V site B/D, on power and on BC0....load to site (B,D)
        /// </summary>
        private bool _BC220;

        public bool BC220
        {
            get { return _BC220; }
            set
            {
                if (value != _BC220) _BC220 = value;
                if (value)
                {
                    BDSC = false;
                    BC110 = false;
                    BC0 = true;
                }
                OnPropertyChanged("BC220");
            }
        }

        /// <summary>
        /// Discharge site B/D
        /// </summary>
        private bool _BDSC1;

        public bool BDSC1
        {
            get { return _BDSC1; }
            set
            {
                if (value != _BDSC1) _BDSC1 = value;
                OnPropertyChanged("BDSC1");
            }
        }

        /// <summary>
        /// Discharge site B/D
        /// </summary>
        private bool _BDSC2;

        public bool BDSC2
        {
            get { return _BDSC2; }
            set
            {
                if (value != _BDSC2) _BDSC2 = value;
                OnPropertyChanged("BDSC2");
            }
        }

        public bool BDSC
        {
            get { return BDSC1 || BDSC2; }
            set
            {
                BDSC1 = value;
                BDSC2 = value;
                if (value)
                {
                    BC0 = false;
                    BC110 = false;
                    BC220 = false;
                }
                OnPropertyChanged("BDSC1");
                OnPropertyChanged("BDSC2");
                OnPropertyChanged("BDSC");
            }
        }

        // Machine SYSTEM GEN
        /// <summary>
        /// Generation a frequency
        /// </summary>
        private Int32 _A1_GEN;

        public Int32 A1_GEN
        {
            get { return _A1_GEN; }
            set
            {
                if (value != _A1_GEN) _A1_GEN = value;
                OnPropertyChanged("A1_GEN");
            }
        }

        /// <summary>
        /// Generation a frequency
        /// </summary>
        private Int32 _A2_GEN;

        public Int32 A2_GEN
        {
            get { return _A2_GEN; }
            set
            {
                if (value != _A2_GEN) _A2_GEN = value;
                OnPropertyChanged("A2_GEN");
            }
        }

        /// <summary>
        /// Generation a frequency
        /// </summary>
        private Int32 _B1_GEN;

        public Int32 B1_GEN
        {
            get { return _B1_GEN; }
            set
            {
                if (value != _B1_GEN) _B1_GEN = value;
                OnPropertyChanged("B1_GEN");
            }
        }

        /// <summary>
        /// Generation a frequency
        /// </summary>
        private Int32 _B2_GEN;

        public Int32 B2_GEN
        {
            get { return _B2_GEN; }
            set
            {
                if (value != _B2_GEN) _B2_GEN = value;
                OnPropertyChanged("B2_GEN");
            }
        }

        private List<byte> _GEN_BYTES = new List<byte>(13);

        public List<byte> GEN_BYTES
        {
            get { return _GEN_BYTES; }
            set
            {
                if (value != null && value != _GEN_BYTES)
                {
                    _GEN_BYTES = value;
                    OnPropertyChanged("GEN_BYTES");
                }
            }
        }


        // ---- Key-value protocol (v2) ----
        // Key-value form of the outputs the system board physically drives. Fixed order; value 0 = OFF, 1 = ON.
        public void OutputKV(out byte[] keys, out byte[] values)
        {
            keys = new byte[]
            {
                SystemComunication.OUT_CLUP,
                SystemComunication.OUT_AC110,
                SystemComunication.OUT_AC220,
                SystemComunication.OUT_LPR,
                SystemComunication.OUT_LPY,
                SystemComunication.OUT_LPG,
                SystemComunication.OUT_BZ,
            };
            values = new byte[]
            {
                (byte)(MainUP ? 1 : 0),
                (byte)(AC110 ? 1 : 0),
                (byte)(AC220 ? 1 : 0),
                (byte)(LPR ? 1 : 0),
                (byte)(LPY ? 1 : 0),
                (byte)(LPG ? 1 : 0),
                (byte)(BUZZER ? 1 : 0),
            };
        }

        // Apply one input state received from the board, addressed by key. Unknown keys are ignored.
        // There is deliberately no SW_UP/SW_DOWN here: an input frame can never drive MainUP, which was the
        // phantom-bit hazard of the old full-bitmask DataToIO (SW_DOWN=0 -> MainUP=true -> power cut).
        public void ApplyInput(byte key, bool value)
        {
            switch (key)
            {
                case SystemComunication.IN_SS_DOWN: SS_DOWN = value; break;
                case SystemComunication.IN_SS_UP: SS_UP = value; break;
                case SystemComunication.IN_BTN_START: BTN_START_MANUAL = value; break;
                case SystemComunication.IN_BTN_STOP: BTN_STOP_ALL = value; break;
                case SystemComunication.IN_SW_EMC: SW_EMC = value; break;
                case SystemComunication.IN_DOOR: IsDoorOpen = value; break;
                case SystemComunication.IN_SS_BF: SS_BF = value; break;
                case SystemComunication.IN_SS_TF: SS_TF = value; break;
                case SystemComunication.IN_SS_BL: SS_BL = value; break;
                case SystemComunication.IN_SS_TL: SS_TL = value; break;
                case SystemComunication.IN_SS_BR: SS_BR = value; break;
                case SystemComunication.IN_SS_TR: SS_TR = value; break;
            }
        }
    }

    public class IntegerValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            int result;
            bool isValid = int.TryParse(value as string, out result);

            if (!isValid)
                return new ValidationResult(false, "Please enter a valid integer.");

            return new ValidationResult(true, null);
        }
    }
}