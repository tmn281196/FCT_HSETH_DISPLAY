using VTMControls.CustomControls;
using Microsoft.Office.Interop.Excel;
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

namespace VTMControls.DeviceControl
{
    /// <summary>
    /// Interaction logic for VisionModelOption.xaml
    /// </summary>
    public partial class VisionModelOption : UserControl
    {
        public LCD lcd = new LCD();
        public FND fnd = new FND();

        private bool isFnd = true;

        // Raised when the operator clicks "Copy detect to Oper + Spect". The panel sets its own component's
        // SpectString directly; the step's Oper lives on VisionPage, so it subscribes and writes it there.
        public event EventHandler<string> CopyToOperRequested;

        public VisionModelOption()
        {
            InitializeComponent();
        }

        private void CopyDetectToOper_Click(object sender, RoutedEventArgs e)
        {
            string detected = (isFnd ? fnd.DetectedString : lcd.DetectedString) ?? "";
            // Teach this component's expected string...
            if (isFnd) fnd.SpectString = detected;
            else lcd.SpectString = detected;
            // ...and let VisionPage write the step's Oper (it owns the step).
            CopyToOperRequested?.Invoke(this, detected);
        }

        // Propagate the Spect string to the step's Oper - but ONLY on a real USER edit (keyboard focus in the box).
        // A PROGRAMMATIC change must not write back: SetDataContext binds a newly selected component through the
        // TwoWay binding, and if that component's SpectString is still its default ("8" for FND, "8888" for LCD/GLED)
        // the old unconditional handler wrote that default into the current step's Oper - clobbering the model's
        // loaded Oper the moment a step was selected (this is the "LED/FND Oper jumps to 8 on load" bug). The LCD path
        // only escaped it because VisionPage pre-syncs SpectString = Oper before binding. The Copy buttons still set
        // Oper explicitly (CopyToOperRequested), so gating on focus loses nothing.
        private void SpectText_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null || !tb.IsKeyboardFocusWithin) return;
            CopyToOperRequested?.Invoke(this, tb.Text ?? "");
        }

        // Brightness Threshold / Noise size filter / Blur spinners were removed from the panel, so their
        // ValueChanged handlers and the code that pushed values into them are gone too. Detection still reads the
        // component's Threshold / NoiseSize / Blur from the saved model (or defaults) - only manual tuning is gone.
        public void SetDataContext(object sender)
        {
            FND tryCatchFND = (sender as FND);
            if (tryCatchFND != null)
            {
                DataContext = tryCatchFND;
                this.fnd = tryCatchFND;
                isFnd = true;
            }
            LCD tryCatchLCD = (sender as LCD);
            if (tryCatchLCD != null)
            {
                DataContext = tryCatchLCD;
                this.lcd = tryCatchLCD;
                isFnd = false;
            }
        }
    }
}
