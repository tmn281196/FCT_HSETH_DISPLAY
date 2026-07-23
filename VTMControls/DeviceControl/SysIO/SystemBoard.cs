using VTMUtility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Debug = VTMUtility.Debug;

namespace VTMControls.DeviceControl
{
    public class SystemBoard
    {
        public SystemMachineIO MachineIO = new SystemMachineIO();

        private SerialPortDisplay _SerialPort = new SerialPortDisplay();

        public SerialPortDisplay SerialPort
        {
            get { return _SerialPort; }
            set
            {
                if (value != _SerialPort) _SerialPort = value;
            }
        }

        public SystemBoard()
        {
            SerialPort.DeviceName = "SYSTEM";   // full name for the settings UI
            SerialPort.LogTag = "SYS";          // short tag for the log
            SerialPort.Port = new System.IO.Ports.SerialPort
            {
                PortName = "COM10",
                BaudRate = 9600,
                DataBits = 8,
                Parity = System.IO.Ports.Parity.None,
                StopBits = System.IO.Ports.StopBits.One,
                ReceivedBytesThreshold = 1
            };
        }

        // --- Human-readable annotation of a SYSTEM frame for the log (v2): [STX OPCODE KEY VALUE CRC ETX]. ---
        // Fixed-width columns so the eye can scan straight down: a 3-char direction, the frame type, then
        // SIGNAL=H/L. Signal names are all 3 chars, so the '=' lines up too.
        //   OUT REQ    CUP=H   PC -> board : what we asked for
        //   OUT ACK    CUP=H   board -> PC : "your frame arrived" (echoes the request, says nothing about the pin)
        //   OUT VAL    CUP=L   board -> PC : the level the pin ACTUALLY took - here the interlock refused
        //   IN  VAL    SDW=L   board -> PC : input state
        private const int TagWidth = 11;

        // Direction padded to 3 ("OUT"/"IN ") so the type word starts at the same column in every line.
        private static string Tag(string dir, string type) => (dir.PadRight(3) + " " + type).PadRight(TagWidth);

        public static string AnnotateFrame(byte[] f, int n, bool isTx)
        {
            if (f == null || n < 6) return "";
            byte op = f[1], key = f[2];
            string val = f[3] != 0 ? "H" : "L";
            switch (op)
            {
                case SystemComunication.CMD_INPUT: return Tag("IN", "VAL") + InputName(key) + "=" + val;
                case SystemComunication.CMD_OUTPUT: return Tag("OUT", "REQ") + OutputName(key) + "=" + val;
                case SystemComunication.CMD_ACK: return Tag("OUT", "ACK") + OutputName(key) + "=" + val;
                case SystemComunication.CMD_VAL: return Tag("OUT", "VAL") + OutputName(key) + "=" + val;
                default: return "";
            }
        }

        // All names are exactly 3 chars so the '=' lines up in the log. STA/STO are the buttons; STR is the
        // top-right seating sensor - keep them distinct.
        private static string InputName(byte key)
        {
            switch (key)
            {
                case SystemComunication.IN_SS_DOWN: return "SDW";
                case SystemComunication.IN_SS_UP: return "SUP";
                case SystemComunication.IN_BTN_START: return "STA";
                case SystemComunication.IN_BTN_STOP: return "STO";
                case SystemComunication.IN_SW_EMC: return "EMC";
                case SystemComunication.IN_DOOR: return "DOR";
                case SystemComunication.IN_SS_BF: return "SBF";
                case SystemComunication.IN_SS_TF: return "STF";
                case SystemComunication.IN_SS_BL: return "SBL";
                case SystemComunication.IN_SS_TL: return "STL";
                case SystemComunication.IN_SS_BR: return "SBR";
                case SystemComunication.IN_SS_TR: return "STR";
                default: return "K" + key.ToString("X2");
            }
        }

        private static string OutputName(byte key)
        {
            switch (key)
            {
                case SystemComunication.OUT_CLUP: return "CUP";
                case SystemComunication.OUT_AC110: return "110";
                case SystemComunication.OUT_AC220: return "220";
                case SystemComunication.OUT_LPR: return "LPR";
                case SystemComunication.OUT_LPY: return "LPY";
                case SystemComunication.OUT_LPG: return "LPG";
                case SystemComunication.OUT_BZ: return "BUZ";
                default: return "K" + key.ToString("X2");
            }
        }

        public async void CheckCardComunication(string COMNAME)
        {
            SerialPort.SerialDataReciver -= SerialPort_SerialDataReciver;
            // Connect probe: one-signal output write (CLUP at its current value). The board applies it and replies
            // with a CMD_ACK (and streams inputs), so any valid response confirms the link. IsRaw:true = send as-is.
            byte[] probe = SystemComunication.BuildFrame(SystemComunication.CMD_OUTPUT,
                SystemComunication.OUT_CLUP, (byte)(MachineIO.MainUP ? 1 : 0));
            await SerialPort.CheckBoardComPort(COMNAME, 9600, probe, null, 1000, false, true);
            SerialPort.SerialDataReciver += SerialPort_SerialDataReciver;
            lock (_pendingLock) { _boardOutputVal.Clear(); }   // forget the board's state -> next SendControl re-pushes everything
            SendControl();
        }

        private string LogName => SerialPort.LogName;   // short LogTag ("SYS"), falls back to DeviceName

        private static byte[] Slice(byte[] src, int start, int end)
        {
            byte[] r = new byte[end - start + 1];
            Array.Copy(src, start, r, 0, r.Length);
            return r;
        }

        private void SerialPort_SerialDataReciver(object sender, EventArgs e)
        {
            if (SerialPort?.Port == null || !SerialPort.Port.IsOpen) return;

            Task.Delay(50).Wait();   // let the whole burst land in the buffer
            int size = SerialPort.Port.BytesToRead;
            if (size <= 0) return;
            byte[] bytes = new byte[size];
            try
            {
                int read = SerialPort.Port.Read(bytes, 0, size);
                if (read <= 0) return;
                if (read < bytes.Length) Array.Resize(ref bytes, read);
            }
            catch (Exception)
            {
                return;
            }

            // Scan the buffer for fixed 6-byte frames [STX OPCODE KEY VALUE CRC ETX]. Apply only frames whose
            // STX/ETX bounds AND CRC match, so serial noise can never flip an input. Every complete frame is
            // applied (there can be several); a partial trailing frame is left for the next read.
            int i = 0;
            while (i + 5 < bytes.Length)
            {
                if (bytes[i] != SystemComunication.STX) { i++; continue; }
                int end = i + 5;   // ETX index
                if (bytes[end] != SystemComunication.ETX) { i++; continue; }
                byte crc = (byte)(bytes[i] ^ bytes[i + 1] ^ bytes[i + 2] ^ bytes[i + 3]);
                if (crc != bytes[i + 4]) { i++; continue; }   // bad CRC - drop, do not apply garbage

                byte op = bytes[i + 1];
                byte key = bytes[i + 2];
                bool on = bytes[i + 3] != 0;

                if (op == SystemComunication.CMD_INPUT)
                {
                    // dispatch by key (only real inputs update -> MainUP untouched)
                    try { MachineIO.ApplyInput(key, on); }
                    catch (Exception ex) { Debug.Write("System Board ApplyInput Warning: " + ex.Message, Debug.ContentType.Notify); }
                }
                else if (op == SystemComunication.CMD_ACK)
                {
                    lock (_pendingLock)
                    {
                        // ACK means ONLY "the frame reached the board" - it echoes the value we asked for and says
                        // nothing about the pin. So it settles the retry and MUST NOT touch _boardOutputVal: the
                        // interlock may have driven the pin elsewhere, and caching the requested value here is
                        // exactly what once made a PASS never raise the cylinder. The pin's real level arrives
                        // separately as CMD_VAL (below), which is the only thing allowed to write the cache.
                        _pendingOut.Remove(key);
                    }
                }
                else if (op == SystemComunication.CMD_VAL)
                {
                    // The board drove this pin - whether we asked (applyOutput) or it acted alone (SDOWN
                    // interlock). The value is the level the pin ACTUALLY took, so this is the single source of
                    // truth for the delta and the only writer of _boardOutputVal. A CLUP:1 the interlock refused
                    // arrives here as 0, so the next SendControl re-sends it by itself once the interlock lets go.
                    // Deliberately does NOT touch _pendingOut: only the matching CMD_ACK settles a write.
                    lock (_pendingLock) { _boardOutputVal[key] = (byte)(on ? 1 : 0); }
                }

                // Every valid frame is logged; identical consecutive lines collapse to "xN" in the log itself.
                if (SerialPort.LogTxRx && (op == SystemComunication.CMD_INPUT || op == SystemComunication.CMD_ACK
                                                                             || op == SystemComunication.CMD_VAL))
                    Debug.Rx(LogName, Slice(bytes, i, end));

                i = end + 1;   // advance past this frame and keep scanning
            }

            RetryStalePendingOutputs();
        }

        // Re-send any output whose ACK hasn't arrived within AckTimeoutMs (up to MaxOutTries). Driven by incoming
        // frames: with the firmware polling flag on, frames arrive at least every polling interval, so this runs
        // regularly without a dedicated timer.
        private void RetryStalePendingOutputs()
        {
            List<KeyValuePair<byte, byte>> resend = null;
            List<KeyValuePair<byte, byte>> giveUp = null;
            lock (_pendingLock)
            {
                foreach (var kv in _pendingOut)
                {
                    PendingOut p = kv.Value;
                    if ((DateTime.Now - p.sentAt).TotalMilliseconds < AckTimeoutMs) continue;
                    if (p.tries >= MaxOutTries)
                    {
                        if (giveUp == null) giveUp = new List<KeyValuePair<byte, byte>>();
                        giveUp.Add(new KeyValuePair<byte, byte>(kv.Key, p.value));
                        continue;
                    }
                    p.tries++;
                    p.sentAt = DateTime.Now;
                    if (resend == null) resend = new List<KeyValuePair<byte, byte>>();
                    resend.Add(new KeyValuePair<byte, byte>(kv.Key, p.value));
                }
                if (giveUp != null) foreach (var g in giveUp) _pendingOut.Remove(g.Key);
            }
            if (resend != null)
                foreach (var r in resend)
                    SerialPort.SendBytes(SystemComunication.BuildFrame(SystemComunication.CMD_OUTPUT, r.Key, r.Value));
            // The ACK is a pure transport receipt, so it arrives even when the interlock refused the value. Its
            // absence therefore means one thing only: the board never received the frame AT ALL - a lost frame or
            // a dead link. This warning is now unambiguous; a refusal shows up as CMD_VAL, never as a timeout.
            if (giveUp != null)
                foreach (var g in giveUp)
                    Debug.Write("SYS: REQUEST TIMEOUT " + OutputName(g.Key) + "=" + (g.Value != 0 ? "H" : "L"),
                                Debug.ContentType.Warning);
        }

        // What the BOARD last told us each output actually is (from its CMD_VAL frames), NOT what we last sent
        // and NOT what its ACK echoed. This distinction is the whole point: a send is optimistic - the board's
        // SDOWN interlock can refuse it - so caching "what we sent" poisons the delta. Once a refused CLUP:1 was
        // cached as 1, every later CLUP:1 looked "unchanged" and was silently skipped, so a PASS would never
        // raise the cylinder. Only the CMD_VAL branch may write this.
        // Guarded by _pendingLock (written from the serial RX thread, read by the state machine).
        // Clear it to force a full re-push (done on connect).
        private readonly Dictionary<byte, byte> _boardOutputVal = new Dictionary<byte, byte>();

        // ACK-retry: outputs are delta (only changes are sent) so a lost output frame would not self-heal.
        // Each write is tracked "pending" until its matching ACK arrives; RetryStalePendingOutputs() (driven by
        // incoming frames, which arrive at least every polling interval) re-sends any that go unacknowledged.
        private sealed class PendingOut { public byte value; public DateTime sentAt; public int tries; }
        private readonly Dictionary<byte, PendingOut> _pendingOut = new Dictionary<byte, PendingOut>();
        private readonly object _pendingLock = new object();
        private const int AckTimeoutMs = 150;
        private const int MaxOutTries = 10;

        public void SendControl()
        {
            if (SerialPort?.Port == null || !SerialPort.Port.IsOpen) return;
            byte[] keys, values;
            MachineIO.OutputKV(out keys, out values);
            for (int i = 0; i < keys.Length; i++)
            {
                byte known;
                bool boardAlreadyHasIt;
                lock (_pendingLock) { boardAlreadyHasIt = _boardOutputVal.TryGetValue(keys[i], out known) && known == values[i]; }
                if (boardAlreadyHasIt) continue;   // the board itself confirmed this value - nothing to do
                SendOutput(keys[i], values[i]);    // NOTE: the cache is updated by the ACK, never here
            }
        }

        // Send one output frame and arm ACK-retry for it.
        private void SendOutput(byte key, byte value)
        {
            SerialPort.SendBytes(SystemComunication.BuildFrame(SystemComunication.CMD_OUTPUT, key, value));
            lock (_pendingLock) { _pendingOut[key] = new PendingOut { value = value, sentAt = DateTime.Now, tries = 1 }; }
        }

        public void DoorLockControl(bool value)
        {
            MachineIO.DoorLock = value;
            SendControl();
        }

        public void PowerRelease()
        {
            MachineIO.AC0 = false;
            MachineIO.BC0 = false;
            MachineIO.ADSC = false;
            MachineIO.BDSC = false;
            MachineIO.LPG = false;
            MachineIO.LPY = false;
            MachineIO.LPR = false;
            MachineIO.BUZZER = false;
            SendControl();
        }

        public bool GEN(int value, List<int> Channel)
        {
            SerialPort.SerialDataReciver -= SerialPort_SerialDataReciver;
            byte[] bytes = new byte[13];
            if (MachineIO.GEN_BYTES.Count() == 13)
            {
                bytes = MachineIO.GEN_BYTES.ToArray();
            }
            bytes[0] = 0x47;
            byte[] intBytes = BitConverter.GetBytes(value);
            Array.Reverse(intBytes);
            byte[] result = intBytes;
            foreach (var item in Channel)
            {
                for (int i = 1; i < 4; i++)
                {
                    bytes[(item - 1) * 3 + i] = result[i];
                }
            }

            Console.Write("GEN: ");
            MachineIO.GEN_BYTES = bytes.ToList();
            bool IsOK = SerialPort.SendAndRead(bytes, 0x47, 1000, out _, false);
            SerialPort.SerialDataReciver += SerialPort_SerialDataReciver;
            return IsOK;
        }
    }
}