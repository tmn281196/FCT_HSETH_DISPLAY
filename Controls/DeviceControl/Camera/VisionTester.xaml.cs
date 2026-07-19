using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Controls.DevicesControl
{
    /// <summary>
    /// Interaction logic for VisionTester.xaml
    /// </summary>
    public partial class VisionTester : UserControl
    {
        private VisionModel _Models = new VisionModel();
        public VisionModel Models
        {
            get { return _Models; }
            set
            {
                if (value != null || value != _Models)
                {
                    _Models = value;
                    FuntionsUpdate();
                }

            }
        }

        public VisionTester()
        {
            InitializeComponent();
            functionCanvas.Children.Clear();
        }

        public void FuntionsUpdate()
        {
            functionCanvas.Children.Clear();
            foreach (var item in Models.FNDs)
            {
                foreach (var  item2 in item)
                {
                    item2.SetParentCanvas(functionCanvas);
                    item2.IsReadOnly = true;
                    // Honor Use for visibility like LED/GLED below. Without this, Model.UpdateLayout (fired on load
                    // via PCB_COUNT_CHANGE) forces every board-0 FND box Visible regardless of Use, so the FND ROIs
                    // appear on Manual/Auto before any step is selected. A step selection re-drives Use via UpdateFndRoi.
                    item2.Visibility = item2.Use ? Visibility.Visible : Visibility.Collapsed;
                }

            }
            foreach (var item in Models.LCDs)
            {
                item.SetParentCanvas(functionCanvas);
                item.IsReadOnly = true;
                // LCD has no Use flag (unlike FND/LED/GLED), so hide it on attach - no step is selected yet.
                // UpdateLcdRoi/ApplyLcdRoi re-shows only the LCDs the selected step actually uses (LCDRoiValue.HasValue).
                item.Visibility = Visibility.Collapsed;
            }
            foreach (var item in Models.GLED)
            {
                item.SetParentCanvas(functionCanvas);
                item.IsReadOnly = true;
                foreach (var gled in item.GLEDs)
                {
                    gled.Visibility = gled.Use ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            foreach (var item in Models.LED)
            {
                item.SetParentCanvas(functionCanvas);
                item.IsReadOnly = true;
                foreach (var led in item.LEDs)
                {
                    led.Visibility = led.Use ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        public void FndFunctionUpdate()
        {
            functionCanvas.Children.Clear();

            foreach (var FNDchar in Models.FNDs)
            {
                foreach (var FNDchar_BoardN in FNDchar)
                {
                    FNDchar_BoardN.SetParentCanvas(functionCanvas);
                    FNDchar_BoardN.IsReadOnly = true;

                }
            }
        }

        public void LcdFunctionUpdate()
        {
            functionCanvas.Children.Clear();

            foreach (var item in Models.LCDs)
            {
                item.SetParentCanvas(functionCanvas);
                item.IsReadOnly = true;
            }
        }

        public void LedFunctionUpdate()
        {
            functionCanvas.Children.Clear();

            foreach (var item in Models.LED)
            {
                item.SetParentCanvas(functionCanvas);
                item.IsReadOnly = true;

                foreach (var led in item.LEDs)
                {
                    led.Visibility = led.Use ? Visibility.Visible : Visibility.Collapsed;
                }
            }


        }

        public void UpdateParentCanvas()
        {
            functionCanvas.UpdateLayout();
        }

        private void functionCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            FuntionsUpdate();
        }

     
    }
}
