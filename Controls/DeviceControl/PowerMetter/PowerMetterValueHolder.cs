using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Controls.DevicesControl
{
    public class PowerMettterValueHolder
    {
        public double Voltage_U;
        public double Voltage_W;
        public double Voltage_V;

        public double Voltage_UW;
        public double Voltage_WV;
        public double Voltage_VU;

        public double Current_U;
        public double Current_W;
        public double Current_V;


        public List<double> Voltage_U_Collection =  new List<double>();
        public List<double> Voltage_W_Collection =  new List<double>();
        public List<double> Voltage_V_Collection = new List<double>();

        public List<double> Voltage_UW_Collection =  new List<double>();
        public List<double> Voltage_WV_Collection =  new List<double>();
        public List<double> Voltage_VU_Collection = new List<double>();

        public List<double> Current_U_Collection =  new List<double>();
        public List<double> Current_W_Collection =  new List<double>();
        public List<double> Current_V_Collection = new List<double>();


        public void ClearValueCollection()
        {
            Voltage_U_Collection.Clear();
            Voltage_W_Collection.Clear();
            Voltage_V_Collection.Clear();
            Voltage_UW_Collection.Clear();
            Voltage_WV_Collection.Clear();
            Voltage_VU_Collection.Clear();
            Current_U_Collection.Clear();
            Current_W_Collection.Clear();
            Current_V_Collection.Clear();
        }

        public void ClearValue()
        {
            Voltage_U = 0;
            Voltage_W = 0;
            Voltage_V = 0;

            Voltage_UW = 0;
            Voltage_WV = 0;
            Voltage_VU = 0;

            Current_U = 0;
            Current_W = 0;
            Current_V = 0;
        }
        public bool GetValue(List<byte> bytes)
        {
            if (bytes.Count < 19) return false;

            var byteArray = bytes.ToArray();
            var DPT = byteArray[3];
            var DCT = byteArray[4];

            try
            {
                Voltage_U = BitConverter.ToInt16(new byte[2] { byteArray[8], byteArray[7] }, 0) / 10000.0 * (Math.Pow(10, DPT));
                Voltage_W = BitConverter.ToInt16(new byte[2] { byteArray[10], byteArray[9] }, 0) / 10000.0 * (Math.Pow(10, DPT));
                Voltage_V = BitConverter.ToInt16(new byte[2] { byteArray[12], byteArray[11] }, 0) / 10000.0 * (Math.Pow(10, DPT));

                Voltage_UW = Voltage_U - Voltage_W;
                Voltage_WV = Voltage_W - Voltage_V;
                Voltage_VU = Voltage_V - Voltage_U;

                //Voltage_UW = BitConverter.ToInt16(new byte[2] { byteArray[14], byteArray[13] }, 0) / 10000.0 * (Math.Pow(10, DPT));
                //Voltage_WV = BitConverter.ToInt16(new byte[2] { byteArray[16], byteArray[15] }, 0) / 10000.0 * (Math.Pow(10, DPT));
                //Voltage_VU = BitConverter.ToInt16(new byte[2] { byteArray[18], byteArray[17] }, 0) / 10000.0 * (Math.Pow(10, DPT));

                Voltage_U_Collection.Add(Voltage_U);
                Voltage_W_Collection.Add(Voltage_W);
                Voltage_V_Collection.Add(Voltage_V);

                Voltage_UW_Collection.Add(Voltage_UW);    
                Voltage_WV_Collection.Add(Voltage_WV);
                Voltage_VU_Collection.Add(Voltage_VU);

                Current_U_Collection.Add(Current_U);
                Current_W_Collection.Add(Current_W);
                Current_V_Collection.Add(Current_V);

                //IU = BitConverter.ToInt16(new byte[2] { byteArray[20], byteArray[19] }, 0) / 10000.0 * (Math.Pow(10, DCT));
                //IW = BitConverter.ToInt16(new byte[2] { byteArray[22], byteArray[21] }, 0) / 10000.0 * (Math.Pow(10, DCT));
                //IV = BitConverter.ToInt16(new byte[2] { byteArray[24], byteArray[23] }, 0) / 10000.0 * (Math.Pow(10, DCT));
            }
            catch
            {
                return false;
            }

            return true;
        }
    }
}
