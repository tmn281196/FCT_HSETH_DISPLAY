using Controls.DevicesControl;
using System.Collections.Generic;
using System.Linq;

namespace Controls.DeviceControl
{
    public class SevenSegment
    {
        public List<bool> LogicLevel { get; set; }

        public int PinNumber = 35;

        public BoardExtension BoardExtension { get; set; }

        public SevenSegment(BoardExtension pBoardExtension)
        {
            BoardExtension = pBoardExtension;
        }

        public List<bool> Digit0;
        public List<bool> Digit1;
        public List<bool> Sign;
        public List<bool> Digit2;
        public List<bool> Digit3;
        public List<bool> Icons;

        public void ParseDigit()
        {
            Digit0 = LogicLevel.Skip(0).Take(7).ToList();
            Digit1 = LogicLevel.Skip(7).Take(7).ToList();
            Digit2 = LogicLevel.Skip(16).Take(7).ToList();

            Sign = LogicLevel.Skip(14).Take(2).ToList();

            Digit3 = new List<bool>();

            for (int i = 0; i < 7; i++)
            {
                Digit3.Add(false);
            }
            Digit3[1] = LogicLevel.Skip(23).Take(2).ToList()[0];
            Digit3[2] = LogicLevel.Skip(23).Take(2).ToList()[1];

            Icons = LogicLevel.Skip(25).Take(10).ToList();

            return;
        }


        public void DigitalRead()
        {
            LogicLevel = Enumerable.Repeat(false, PinNumber).ToList();

            byte[] inputAsk = new byte[2];
            inputAsk[0] = (byte)0x51;
            byte[] Response;

            if (BoardExtension.SerialPort.Port.IsOpen)
            {
                if (BoardExtension.SerialPort.SendAndRead(new byte[] { 0x51 }, 0x51, 1500, out Response))
                {
                    if (Response.Length == 11)
                    {
                        for (int bitIdx = 0; bitIdx < PinNumber; bitIdx++)
                        {
                            // Determine which byte contains the bit
                            int byteIdx = bitIdx / 8;
                            // Determine the position of the bit within that byte
                            int bitOffset = bitIdx % 8;

                            byte byteValue = Response[4 + byteIdx];

                            // Mask to retrieve the specific bit
                            // Create a bitmask to isolate the specific bit
                            byte mask = (byte)(1 << (7 - bitOffset));
                            // Check if the bit is set (true) or clear (false)
                            bool bitValue = (byteValue & mask) != 0;
                            LogicLevel[bitIdx] = bitValue;
                        }


                    }

                }

            }
        }

    }
}
