using Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Controls
{
    public static class SystemComunication
    {
        public static byte Prefix1 = 0x44;
        public static byte Prefix2 = 0x45;
        public static byte Suffix = 0x56;

        // ================= System-board protocol (v2) - one signal per frame =================
        // Fixed 6-byte frame:  [STX][OPCODE][KEY][VALUE][CRC][ETX]
        //   STX = 0x02, ETX = 0x03
        //   OPCODE = CMD_INPUT / CMD_OUTPUT / CMD_ACK
        //   KEY    = which signal (IN_* / OUT_* below)
        //   VALUE  = 0 = OFF, non-zero = ON
        //   CRC    = XOR of STX ^ OPCODE ^ KEY ^ VALUE
        // Inputs are addressed BY KEY so the PC only ever updates the signals the board actually reports -
        // phantom bits (SW_UP/SW_DOWN) are never sent, so an input frame can't clobber MainUP.
        public const byte STX = 0x02;
        public const byte ETX = 0x03;
        public const byte CMD_INPUT = 0x49;   // board -> PC : one input state (on change + periodic re-send)
        public const byte CMD_OUTPUT = 0x4F;  // PC -> board : one output to write
        public const byte CMD_ACK = 0x41;     // board -> PC : echo of the output just set (confirmation)

        // Input keys (board -> PC). The current firmware only reports SS_DOWN + the two buttons; the rest are
        // reserved for when the seating/lock sensors get wired.
        public const byte IN_SS_DOWN = 0x01;
        public const byte IN_SS_UP = 0x02;
        public const byte IN_BTN_START = 0x03;
        public const byte IN_BTN_STOP = 0x04;
        public const byte IN_SW_EMC = 0x05;
        public const byte IN_DOOR = 0x06;
        public const byte IN_SS_BF = 0x10;
        public const byte IN_SS_TF = 0x11;
        public const byte IN_SS_BL = 0x12;
        public const byte IN_SS_TL = 0x13;
        public const byte IN_SS_BR = 0x14;
        public const byte IN_SS_TR = 0x15;

        // Output keys (PC -> board). These are the outputs the system board physically drives.
        public const byte OUT_CLUP = 0x01;   // MainUP / cylinder up (reset)
        public const byte OUT_AC110 = 0x02;
        public const byte OUT_AC220 = 0x03;
        public const byte OUT_LPR = 0x04;
        public const byte OUT_LPY = 0x05;
        public const byte OUT_LPG = 0x06;
        public const byte OUT_BZ = 0x07;

        // Build a fixed 6-byte one-signal frame: [STX][OPCODE][KEY][VALUE][CRC][ETX].
        public static byte[] BuildFrame(byte opcode, byte key, byte value)
        {
            byte crc = (byte)(STX ^ opcode ^ key ^ value);
            return new byte[] { STX, opcode, key, value, crc, ETX };
        }

        // True if a 6-byte buffer is a valid v2 frame (STX/ETX bounds + CRC). Used by the log's CRC check too.
        public static bool FrameOk(byte[] f, int n)
        {
            if (f == null || n < 6) return false;
            return f[0] == STX && f[5] == ETX && (byte)(f[0] ^ f[1] ^ f[2] ^ f[3]) == f[4];
        }

        //public static byte[] GetFrame(byte[] datas)
        //{
        //    if (datas == null) return null;

        //    List<byte> dataToSend = datas.ToList();
        //    if (datas.Length > 1)
        //    {
        //        dataToSend.Insert(0, (byte)(dataToSend.Count + 1));
        //    }
        //    else
        //    {
        //        dataToSend.Add(0x00);
        //    }
        //    dataToSend.Insert(0, Prefix2);
        //    dataToSend.Insert(0, Prefix1);
        //    var checksum = CheckSum.Get(dataToSend.ToArray(), CheckSumType.XOR);
        //    dataToSend.Add(checksum);
        //    dataToSend.Add(Suffix);

        //    //dataToSend.Insert(2, (Byte)(dataToSend.Count - 3) );
        //    foreach (var item in dataToSend)
        //    {
        //        Console.Write(item.ToString("X2") + " ");
        //    }
        //    Console.WriteLine(" ");
        //    return dataToSend.ToArray();
        //}

        public static byte[] GetFrame(byte[] datas, bool IsNoSize = false)
        {
            if (datas == null) return null;

            List<byte> dataToSend = datas.ToList();
            if (!IsNoSize)
            {
                if (datas.Length > 1)
                {
                    dataToSend.Insert(0, (byte)(dataToSend.Count + 1));
                }
                else
                {
                    dataToSend.Add(0x00);
                }
            }
            dataToSend.Insert(0, Prefix2);
            dataToSend.Insert(0, Prefix1);
            var checksum = CheckSum.Get(dataToSend.ToArray(), CheckSumType.XOR);
            dataToSend.Add(checksum);
            dataToSend.Add(Suffix);

            //dataToSend.Insert(2, (Byte)(dataToSend.Count - 3) );
            //foreach (var item in dataToSend)
            //{
            //    Console.Write(item.ToString("X2") + " ");
            //}
            //Console.WriteLine(" ");
            return dataToSend.ToArray();
        }

        // check sensor down
        public static bool GetResponse(byte[] datas, byte[] compare)
        {
            if (datas == null) return false;

            List<byte> dataToSend = datas.ToList();
            dataToSend.Insert(0, Prefix2);
            dataToSend.Insert(0, Prefix1);
            var checksum = CheckSum.Get(dataToSend.ToArray(), CheckSumType.XOR);
            dataToSend.Add(checksum);

            dataToSend.Add(Suffix);

            //dataToSend.Insert(2, (Byte)(dataToSend.Count - 3) )-;
            //foreach (var item in dataToSend)
            //{
            //    Console.Write(item.ToString("X2") + " ");
            //}
            //Console.WriteLine(" ");
            //foreach (var item in compare)
            //{
            //    Console.Write(item.ToString("X2") + " ");
            //}

            var result = true;
            var byteData = dataToSend.ToArray();
            for (int i = 0; i < compare.Length; i++)
            {
                if (i < byteData.Length)
                {
                    result = byteData[i] == compare[i];
                }
            }

            return result;
        }
    }
}