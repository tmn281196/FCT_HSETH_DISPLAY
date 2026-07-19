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

namespace VTMControls.CustomControls
{
    /// <summary>
    /// Interaction logic for IntegerUpDown.xaml
    /// </summary>
    public partial class IntegerUpDown : UserControl
    {
        public event EventHandler ValueChanged;

        public IntegerUpDown()
        {
            InitializeComponent();
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }

        // Dependency Property for Value
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value",
            typeof(int),
            typeof(IntegerUpDown),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        // Dependency Property for Minimum
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            "Minimum",
            typeof(int),
            typeof(IntegerUpDown),
            new PropertyMetadata(int.MinValue));

        // Dependency Property for Maximum
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            "Maximum",
            typeof(int),
            typeof(IntegerUpDown),
            new PropertyMetadata(int.MaxValue));

        // Value property using the DependencyProperty
        public int Value
        {
            get { return (int)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        // Minimum property using the DependencyProperty
        public int Minimum
        {
            get { return (int)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        // Maximum property using the DependencyProperty
        public int Maximum
        {
            get { return (int)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (IntegerUpDown)d;
            var newValue = (int)e.NewValue;
            // Ensure the TextBox is updated to reflect the new value
            control.txtNum.Text = newValue.ToString();
            // Invoke the ValueChanged event
            control.ValueChanged?.Invoke(control, EventArgs.Empty);
        }

        private void cmdUp_Click(object sender, RoutedEventArgs e)
        {
            if (Value < Maximum) Value++;
        }

        private void cmdDown_Click(object sender, RoutedEventArgs e)
        {
            if (Value > Minimum) Value--;
        }

        private void txtNum_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(txtNum.Text, out int newValue))
            {
                if (newValue >= Minimum && newValue <= Maximum)
                {
                    Value = newValue;
                }
                else
                {
                    txtNum.Text = Value.ToString(); // Reset to last valid value if out of bounds
                }
            }
        }
    }
}