using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VTMControls
{
    // Convert bool to a SolidColorBrush for the indicator LED.
    // Parameter format: "TrueColor|FalseColor" - e.g. "LimeGreen|Gray".
    // If no parameter is given, default to green / gray.
    public class BoolToBrushConverter : IValueConverter
    {
        private static readonly BrushConverter _bc = new BrushConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool bv && bv;
            string p = parameter as string;
            string trueColor = "LimeGreen";
            string falseColor = "#4A4A4A";
            if (!string.IsNullOrEmpty(p))
            {
                var parts = p.Split('|');
                if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0])) trueColor = parts[0].Trim();
                if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1])) falseColor = parts[1].Trim();
            }
            try
            {
                return (Brush)_bc.ConvertFromString(b ? trueColor : falseColor);
            }
            catch
            {
                return b ? Brushes.LimeGreen : Brushes.Gray;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
