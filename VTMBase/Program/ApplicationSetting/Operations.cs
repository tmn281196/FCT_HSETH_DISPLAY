using System;
using VTMControls.DeviceControl;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VTMBase
{
    public class Operations
    {
        public bool NoTest_PressUp { get; set; } = true;
        public int StartDelaytime { get; set; } = 500;
        public int TestPressUpTime { get; set; } = 500;

        public bool FailContinue { get; set; } = false;
        public bool FailStopAll { get; set; } = false;
        public bool FailStopPCB { get; set; } = true;

        public bool FailResistanceStopAll { get; set; } = true;
        public int ErrorJumpCount { get; set; } = 3;


        public bool SaveFailPCB { get; set; } = true;
        public bool UsePre_endSignal { get; set; } = true;

        // Master switch for writing the end-of-test .lgd log into Communication.LogDirectory.
        // Defaults to true, and an older Config.cfg without this key keeps that default - so upgrading a machine
        // can never silently stop logging. Note this is NOT SaveFailPCB above: that one only chooses WHICH
        // artifact is written (.lgd vs .vtmh history), it never disables logging.
        public bool ExportLog { get; set; } = true;


        public int RetryCount { get; set; } = 2;
        public bool PassSkipPCB { get; set; } = true;
        public bool UseRetryUpdown { get; set; } = false;
        public int RetryUpdownTime { get; set; } = 500;

    }
}
