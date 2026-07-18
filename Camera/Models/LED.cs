using Neodynamic.SDK.Printing;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Rect = System.Windows.Rect;

namespace Camera
{
    public class LED
    {
        private Int32 _calculatorOutput;

        public Int32 CalculatorOutput
        {
            get { return _calculatorOutput; }
            set
            {
                _calculatorOutput = value;
                CalculatorOutputString = value.ToString("X");
            }
        }

        private string _calculatorBinaryOutputString;

        public string CalculatorBinaryOutputString
        {
            get { return _calculatorBinaryOutputString; }
            set
            {
                _calculatorBinaryOutputString = value;
            }
        }

        private string _calculatorOutputString;

        public String CalculatorOutputString
        {
            get { return _calculatorOutputString; }
            set
            {
                _calculatorOutputString = value;
                NotifyPropertyChanged(nameof(CalculatorOutputString));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private Visibility visibility;

        public Visibility Visibility
        {
            get { return visibility; }
            set
            {
                visibility = value;
                foreach (SingleLED led in LEDs)
                {
                    led.Visibility = led.Use ? value : Visibility.Hidden;
                }
            }
        }

        private ObservableCollection<SingleLED> leds = new ObservableCollection<SingleLED>();

        public ObservableCollection<SingleLED> LEDs
        {
            get
            {
                return leds;
            }

            set
            {
                if (leds != value)
                {
                    leds = value;
                }
            }
        }

        private bool _IsReadOnly;

        [JsonIgnore]
        public bool IsReadOnly
        {
            get { return _IsReadOnly; }
            set
            {
                if (value != _IsReadOnly) _IsReadOnly = value;
                foreach (var item in LEDs)
                {
                    item.IsReadOnly = IsReadOnly;
                }
            }
        }

        public LED()
        { }

        public LED(System.Windows.Point startLocation, int diameter, int thresh)
        {
            CalculatorOutputString = string.Empty;

            for (int i = 0; i < 7; i++)
            {
                LEDs.Add(new SingleLED(i, startLocation, diameter, thresh));
            }
        }


        public LED(System.Windows.Point startLocation)
        {
            CalculatorOutputString = string.Empty;

            for (int i = 0; i < 32; i++)
            {
                LEDs.Add(new SingleLED(i, startLocation, 9, 90));
            }
        }

        // Lay the 7 segment probes out inside the char box:
        //
        //      ----0----          0/6/3 : horizontal bars, centred on the box's vertical axis
        //     |         |         5/1   : upper verticals, at 1/4 height
        //     5         1         4/2   : lower verticals, at 3/4 height
        //     |         |
        //      ----6----          Every probe is CENTRED on its ideal point: the anchor is the probe's
        //     |         |         top-left corner, so each coordinate is (ideal - DiameterPoint/2).
        //     4         2
        //     |         |         The old version was asymmetric three ways and it showed on screen:
        //      ----3----            * 4/2 subtracted a FULL DiameterPoint while 5/1 subtracted half, so the
        //                             lower verticals sat half a probe higher than the upper ones;
        //                           * a magic "- 2.0" only on the right-hand X and "- 0.5"/"- 1.0" only on the
        //                             middle and bottom, nothing on the left or top;
        //                           * segment 0 sat ON the top edge while 3 was inset by D + 2.
        //                         Measured on the default 20x30 box with D=5: left probe centre x=2.5 vs right
        //                         15.5 (should mirror about 10), top y=2.5 vs bottom 25.5 (should mirror
        //                         about 15). Now every pair mirrors exactly.
        // Two-step layout, per spec:
        //   1. Put each of the 6 outer probes' CENTRE right on its edge (0 top, 3 bottom, 5/4 left, 1/2 right);
        //      the middle probe (6) sits at the box centre.
        //   2. Shift the 6 outer ones INWARD (perpendicular to their edge) by radius + thickness/2, so the circle
        //      clears the edge and sits inset by half its own thickness. Diameter never changes - the centre
        //      moves in, nothing is squeezed.
        // Anchors are probe CENTRES; the stored X/Y is the top-left corner, so subtract the radius at the end.
        public void Arrange7Segment(int DiameterPoint, Rect Rect)
        {
            double radius = DiameterPoint * 0.5;
            double thickness = DiameterPoint;               // the ROI's own size is its thickness
            double inset = radius + thickness * 0.5;        // step 2: how far the centre moves inward

            double edgeLeft = Rect.X, edgeRight = Rect.X + Rect.Width;
            double edgeTop = Rect.Y, edgeBottom = Rect.Y + Rect.Height;
            double cx = Rect.X + Rect.Width * 0.5;           // horizontal centre line (0/6/3)

            // The 7 probes form TWO diamonds sharing the centre probe 6:
            //   top diamond    0(top) 5(left) 1(right) 6(bottom=centre)
            //   bottom diamond 6(top=centre) 4(left) 2(right) 3(bottom)
            // So the left/right probes sit at the VERTICAL MIDDLE of their diamond, not at 1/4 and 3/4 height -
            // that is what pulls 5/1 down and pushes 4/2 up versus a plain grid.
            double topY = edgeTop + inset;                   // probe 0 centre
            double botY = edgeBottom - inset;                // probe 3 centre
            double midY = Rect.Y + Rect.Height * 0.5;        // probe 6 centre (box centre)
            double upperY = (topY + midY) * 0.5;             // 5/1: middle of the top diamond
            double lowerY = (midY + botY) * 0.5;             // 4/2: middle of the bottom diamond

            SetSegCenter(0, cx, topY);                       // top
            SetSegCenter(5, edgeLeft + inset, upperY);       // upper-left
            SetSegCenter(1, edgeRight - inset, upperY);      // upper-right
            SetSegCenter(6, cx, midY);                       // centre (shared diamond vertex)
            SetSegCenter(4, edgeLeft + inset, lowerY);       // lower-left
            SetSegCenter(2, edgeRight - inset, lowerY);      // lower-right
            SetSegCenter(3, cx, botY);                       // bottom
        }

        // Place probe `i` by its CENTRE (top-left = centre - radius). Coordinates kept at 2 decimal places.
        private void SetSegCenter(int i, double centerX, double centerY)
        {
            double radius = LEDs[i].rect.Width * 0.5;
            LEDs[i].X = Math.Round(centerX - radius, 2);
            LEDs[i].Y = Math.Round(centerY - radius, 2);
        }

        public void SetParentCanvas(Canvas ParentCanvas)
        {
            // foreach singleLeds
            foreach (var item in LEDs)
            {
                item.SetParentCanvas(ParentCanvas);

                //if (!item.Use)
                //{
                //    item.Visibility = Visibility.Hidden;
                //}
            }

            //Console.Write("\n");
        }

        public void CALC_THRESH()
        {
            foreach (SingleLED LED in LEDs)
            {
                LED.Thresh = (int)(LED.ON - LED.OFF) / 3 * 2 + LED.OFF;
            }
        }

        public void GetValue(Mat mat)
        {
            string Value = "";
            foreach (SingleLED led in LEDs)
            {
                var output = led.Use ? led.TestImage(mat, true) : "0";
                Value = output.ToString() + Value;
            }

            CalculatorBinaryOutputString = new string(Value.Reverse().ToArray());

            CalculatorOutput = Convert.ToInt32(Value, 2);
        }

        public void GetValue()
        {
            string Value = "";
            foreach (SingleLED led in LEDs)
            {
                var output = led.IsPass ? "1" : "0";
                Value = output + Value;
            }
            CalculatorOutput = Convert.ToInt32(Value, 2);
        }
    }

    public class SingleLED : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event EventHandler Sellected;

        private void OnSelected()
        {
            Sellected?.Invoke(this, null);
        }

        public int Index { get; set; }
        private double _x;

        public double X
        {
            get { return _x; }
            set
            {
                value = Math.Round(value, 2);   // keep the coordinate at 2 decimal places
                if (value != _x)
                {
                    _x = value;
                    rect = new Rect(value, Rect.Y, Rect.Width, Rect.Height);
                    SetPosition();
                }
                OnPropertyChanged("X");
            }
        }

        private double _y;

        public double Y
        {
            get { return _y; }
            set
            {
                value = Math.Round(value, 2);   // keep the coordinate at 2 decimal places
                if (value != _y)
                {
                    _y = value;
                    rect = new Rect(Rect.X, value, Rect.Width, Rect.Height);
                    SetPosition();
                }
                OnPropertyChanged("Y");
            }
        }

        // Move this ROI by (dx, dy).
        // Offsets `rect` - the authoritative position - and re-syncs the _x/_y cache FROM it. Never the other way
        // round: the (index, startLocation, ...) constructor assigns `rect` only, so on a freshly built model _x/_y
        // are still 0 while rect already holds the real coordinate. Reading X back to offset it would teleport
        // every ROI to (dx, dy) instead of shifting it.
        // Also deliberately bypasses the `Rect` property, whose bounds guard silently drops a move near the canvas
        // edge - a bulk shift must apply the same delta to every ROI or they drift apart.
        public void Translate(double dx, double dy)
        {
            rect = new Rect(Math.Round(rect.X + dx, 2), Math.Round(rect.Y + dy, 2), rect.Width, rect.Height);   // 2 dp
            _x = rect.X;
            _y = rect.Y;
            SetPosition();
            OnPropertyChanged("X");
            OnPropertyChanged("Y");
            OnPropertyChanged("Rect");
        }

        private double _w;

        public double Width
        {
            get { return _w; }
            set
            {
                if (value != _w)
                {
                    _w = value;
                    Rect = new Rect(Rect.X, Rect.Y, value, Rect.Height);
                    SetPosition();
                }
                OnPropertyChanged("Width");
            }
        }

        private double _h;

        public double Height
        {
            get { return _h; }
            set
            {
                if (value != _h)
                {
                    _h = value;
                    Rect = new Rect(Rect.X, Rect.Y, Rect.Width, value);
                    SetPosition();
                }
                OnPropertyChanged("Height");
            }
        }

        public int ON { get; set; } = 250;
        public int OFF { get; set; } = 10;

        private int thresh = 80;

        public int Thresh
        {
            get { return thresh; }
            set
            {
                thresh = value;
                OnPropertyChanged(nameof(Thresh));
            }
        }



        public bool HasFND = false;


        private bool use = false;

        /// <summary>
        /// Fix now
        /// </summary>
        public bool Use
        {
            get { return use; }
            set
            {
                if (use != value)
                {
                    use = value;
                    OnPropertyChanged(nameof(Use));

                    if (use)
                    {
                        Visibility = Visibility.Visible;

                        this.Label.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            Label.BorderBrush = new SolidColorBrush(Colors.Red);
                            Label.Foreground = new SolidColorBrush(Colors.Red);
                            elip.Fill = new SolidColorBrush(Colors.Yellow);
                            UseTurnOn = UseTurnOn;
                        }));




                    }
                    else
                    {
                        Visibility = Visibility.Hidden;

                        Label.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            Label.BorderBrush = new SolidColorBrush(Colors.Yellow);
                            Label.Foreground = new SolidColorBrush(Colors.Yellow);
                            elip.Fill = new SolidColorBrush(Colors.Gray);

                        }));


                    }

                }
            }
        }

        private int intens = 180;

        public int Intens
        {
            get { return intens; }
            set
            {
                intens = value;
                OnPropertyChanged("Intens");
                if (use)
                {
                    if (value >= Thresh)
                    {
                        Label.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            elip.Fill = new SolidColorBrush(Colors.Green);
                            Context.Foreground = new SolidColorBrush(Colors.White);
                        }));
                    }
                    else
                    {
                        Label.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            elip.Fill = new SolidColorBrush(Colors.Red);
                            Context.Foreground = new SolidColorBrush(Colors.White);
                        }));
                    }
                }
            }
        }

        private int dir = 10;

        public int Dir
        {
            get { return dir; }
            set
            {
                dir = value;
                Rect recNew = Rect;
                recNew.Width = value;
                recNew.Height = value;
                Rect = recNew;
                OnPropertyChanged(nameof(Dir));
            }
        }

        [JsonIgnore]
        private bool _UseTurnOn;

        [JsonIgnore]
        public bool UseTurnOn
        {
            get { return _UseTurnOn; }
            set
            {
                if (value != _UseTurnOn)
                    _UseTurnOn = value;

                if (Use)
                    if (_UseTurnOn)
                    {
                        elip.Fill = new SolidColorBrush(Colors.Green);
                    }
                    else
                    {
                        elip.Fill = new SolidColorBrush(Colors.Yellow);
                    }
                OnPropertyChanged("UseTurnOn");
            }
        }

        [JsonIgnore]
        private bool _IsPass;

        [JsonIgnore]
        public bool IsPass
        {
            get { return _IsPass; }
            set
            {
                if (value != _IsPass)
                    _IsPass = value;
                OnPropertyChanged("IsPass");
                if (Use)
                    if (value)
                    {
                        elip.Fill = new SolidColorBrush(Colors.Green);
                    }
                    else
                    {
                        elip.Fill = new SolidColorBrush(Colors.Red);
                    }
            }
        }

        private int _NoiseSize = 5;

        public int NoiseSize
        {
            get { return _NoiseSize; }
            set
            {
                if (value != _NoiseSize && value > 5)
                {
                    _NoiseSize = value;
                    OnPropertyChanged("NoiseSize");
                }
            }
        }

        private double _Threshold = 100;

        public double Threshold
        {
            get { return _Threshold; }
            set
            {
                if (value != _Threshold) _Threshold = value;
                OnPropertyChanged("Threshold");
            }
        }

        private double _Blur;

        public double Blur
        {
            get { return _Blur; }
            set
            {
                if (value % 2 != 0 || value != _Blur) _Blur = value;
                OnPropertyChanged("Blur");
            }
        }

        private double _TurningProgress;

        public double TurningProgress
        {
            get { return Math.Round(_TurningProgress, 2); }
            set
            {
                if (value != _TurningProgress) _TurningProgress = value;
                OnPropertyChanged("TurningProgress");
            }
        }

        private BitmapSource _CropImage;

        [JsonIgnore]
        public BitmapSource CropImage
        {
            get { return _CropImage; }
            set
            {
                if (value != null || value != _CropImage) _CropImage = value;
                OnPropertyChanged("CropImage");
            }
        }

        private Canvas _ParentCanvas;

        private Canvas ParentCanvas
        {
            get { return _ParentCanvas; }
            set
            {
                if (value != null)
                {
                    _ParentCanvas = value;
                    ParentCanvasSize = new System.Windows.Rect()
                    {
                        X = 0,
                        Y = 0,
                        Width = value.ActualWidth,
                        Height = value.ActualHeight
                    };
                }
            }
        }

        private Rect _ParentCanvasSize;

        // MUST STAY SERIALIZED, however redundant it looks (3,095 copies = 30.4% of a real 8.7 MB model, 83% of
        // them 0x0). It is load-bearing: the file's trailing `Rect` key is assigned AFTER this one, and the Rect
        // setter's guard (`value.X < ParentCanvasSize.Width - value.Width`) needs a real canvas size to accept
        // it. Persisting this is what arms that self-heal. Adding [JsonIgnore] here left the canvas 0x0 at load,
        // the guard then rejected Rect and every Width/Height write, and EVERY ROI reloaded at its 10x10 field
        // initializer - measured on the real ok.vmdl, LED sizes went 32/32 correct (9x9) -> 0/32. TestImage crops
        // from that rect, so it silently changes pass/fail. Tried and reverted 2026-07-17.
        public Rect ParentCanvasSize
        {
            get { return _ParentCanvasSize; }
            set { _ParentCanvasSize = value; }
        }

        private Visibility _Visibility;

        public Visibility Visibility
        {
            get { return _Visibility; }
            set
            {

                if (value != _Visibility) _Visibility = value;


                Label.Visibility = value;

                OnPropertyChanged("Visibility");
            }
        }

        [JsonIgnore]
        public Grid grid = new Grid()
        {
            Background = null,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        [JsonIgnore]
        public Ellipse elip = new Ellipse()
        {
            Width = 20,
            Height = 20,
            Fill = new SolidColorBrush(Colors.Yellow),
        };

        [JsonIgnore]
        public Label Context = new Label()
        {
            Foreground = new SolidColorBrush(Colors.White),
            Padding = new Thickness(0),
            FontSize = 2,
            Focusable = true,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        [JsonIgnore]
        public Label Label = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 255, 0, 0)),
            Focusable = true,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            FontSize = 9
        };

        [JsonIgnore]
        public Image CropImageHolder = new Image();

        [JsonIgnore]
        private Rect OffsetMove;


        public Rect rect = new Rect()
        {
            Location = new System.Windows.Point(5, 5),
            Size = new System.Windows.Size(10, 10)
        };

        public Rect Rect
        {
            get { return rect; }
            set
            {
                if (rect != value)
                {
                    // No (int) truncation - keep full precision. The cast used to snap the ROI to whole pixels,
                    // so a probe drifted by up to a pixel on every move and never sat exactly where it was placed.
                    if (value.X > 0 && value.X < ParentCanvasSize.Width - value.Width)
                    {
                        X = value.X;
                        Width = value.Width;
                        rect.X = value.X;
                        elip.Width = value.Width;
                        rect.Width = value.Width;
                        Label.Width = value.Width;
                    }

                    if (value.Y > 0 && value.Y < ParentCanvasSize.Height - value.Height)
                    {
                        Y = value.Y;
                        Height = value.Height;
                        rect.Y = value.Y;
                        elip.Height = value.Height;
                        rect.Height = value.Height;
                        Label.Height = value.Height;
                    }
                }
            }
        }

        private string name;

        public string Name
        {
            get { return name; }
            set
            {
                if (name != value)
                {
                    name = value;
                    Context.Content = name;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        private bool _IsReadOnly = false;

        public bool IsReadOnly
        {
            get { return _IsReadOnly; }
            set
            {
                if (value != _IsReadOnly) _IsReadOnly = value;
                if (IsReadOnly)
                {
                    Label.Cursor = Cursors.Arrow;

                    Label.GotKeyboardFocus -= Label_GotKeyboardFocus;
                    Label.LostKeyboardFocus -= Label_LostKeyboardFocus;

                    Label.KeyDown -= Label_KeyDown;

                    Label.MouseDown -= Label_MouseDown;
                    Label.MouseMove -= Label_MouseMove;
                    Label.MouseUp -= Label_MouseUp;
                    Label.MouseDoubleClick -= Label_MouseDoubleClick;
                }
            }
        }

        public SingleLED()
        {
            grid.Children.Add(elip);
            grid.Children.Add(Context);

            Label.Content = grid;

            Label.ToolTip = CropImageHolder;

            Label.GotKeyboardFocus += Label_GotKeyboardFocus;
            Label.LostKeyboardFocus += Label_LostKeyboardFocus;

            Label.KeyDown += Label_KeyDown;

            Label.MouseDown += Label_MouseDown;
            Label.MouseMove += Label_MouseMove;
            Label.MouseUp += Label_MouseUp;

            Label.MouseDoubleClick += Label_MouseDoubleClick;
            Label.MouseRightButtonDown += Label_MouseRightButtonDown;

        }


        public SingleLED(int index, System.Windows.Point startLocation, int diameter, int threshold)
        {

            Visibility = Visibility.Hidden;

            rect = new Rect(startLocation.X + index * 20, startLocation.Y, diameter, diameter);
            // Keep the X/Y cache in step with rect. Assigning rect alone left _x/_y at 0 while rect held the real
            // coordinate, so anything reading X back (the LED grid, or a relative move) saw 0 instead of the truth.
            _x = rect.X;
            _y = rect.Y;

            Name = "L" + (index + 1).ToString();
            Name = index.ToString();
            Index = index;

            Label.ToolTip = CropImageHolder;

            Label.MouseUp -= Label_MouseUp;

            Label.GotKeyboardFocus += Label_GotKeyboardFocus;
            Label.LostKeyboardFocus += Label_LostKeyboardFocus;

            Label.KeyDown += Label_KeyDown;

            dir = diameter;
            thresh = threshold;


            elip.Width = diameter - 2;
            elip.Height = diameter - 2;


            grid.Children.Add(elip);
            grid.Children.Add(Context);


            Label.Content = grid;

            Label.MouseDown += Label_MouseDown;
            Label.MouseMove += Label_MouseMove;
            Label.MouseUp += Label_MouseUp;
            Label.MouseDoubleClick += Label_MouseDoubleClick;

            Label.MouseRightButtonDown += Label_MouseRightButtonDown;

        }

        //Parent and positions

        public Label Label2 = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 255, 0, 0)),
            Focusable = true,
            Content = "HI",
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            FontSize = 9
        };

        public void SetParentCanvas(Canvas placeCanvas)
        {
            // check here
            if (ParentCanvas != null)
            {
                ParentCanvas.Children.Remove(Label);
            }
            this.ParentCanvas = placeCanvas;

            Label.Width = rect.Width;
            Label.Height = rect.Height;

            Canvas.SetTop(Label, rect.Y);
            Canvas.SetLeft(Label, rect.X);

            placeCanvas.Children.Add(Label);
        }

        private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            IsPass = !IsPass;
        }

        private void Label_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Disabled: right-click used to set Use=false, i.e. turn a single probe off. Removed for FND, where
            // segments are all-or-nothing (a 7-seg digit needs all 7). The handler is shared with the 32-LED
            // family, so it is neutered here rather than unhooked; restore this line if the LED family ever
            // wants right-click-to-disable back.
            //Use = false;
        }

        private void Label_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            Rect areaRect = Rect;
            double distanceMove = 1;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                distanceMove = 30;
            }

            switch (e.Key)
            {
                case Key.Left:
                    areaRect.X = rect.X - distanceMove;
                    break;

                case Key.Up:
                    areaRect.Y = rect.Y - distanceMove;
                    break;

                case Key.Right:
                    areaRect.X = rect.X + distanceMove;
                    break;

                case Key.Down:
                    areaRect.Y = rect.Y + distanceMove;
                    break;
            }
            Rect = areaRect;
            SetPosition();
        }

        private void Label_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                Label.Cursor = Cursors.SizeAll;
                Keyboard.Focus(Label);
                OffsetMove = new Rect()
                {
                    Width = Math.Max(Math.Abs(e.GetPosition(ParentCanvas).X - rect.X), 5),
                    Height = Math.Max(Math.Abs(e.GetPosition(ParentCanvas).Y - rect.Y), 5),
                };
                OnSelected();
            }
        }

        private void Label_MouseMove(object sender, MouseEventArgs e)
        {
            // Hard block on read-only (Auto/Manual pages). The IsReadOnly setter also unsubscribes this handler,
            // but this guard makes movement impossible regardless of any re-subscription/timing gap. In the editor
            // (VisionBuilder) IsReadOnly stays false, so this is a no-op there and editing is unaffected.
            if (IsReadOnly) return;
            //Console.WriteLine("Label raise event");
            if (e.LeftButton == MouseButtonState.Pressed && !e.Handled && Label.IsKeyboardFocused)
            {
                e.Handled = true;
                Rect areaRect = Rect;
                areaRect.X = e.GetPosition(ParentCanvas).X - OffsetMove.Width;
                areaRect.Y = e.GetPosition(ParentCanvas).Y - OffsetMove.Height;
                Rect = areaRect;


                //Find the good postion
                //Console.WriteLine($"############# Size:{rect.Width} x {rect.Height}");

                SetPosition();
            }

            if (e.LeftButton == MouseButtonState.Pressed && (e.Source as FrameworkElement) == Label)
            {
                var focusElement = Keyboard.FocusedElement;
                if (focusElement != null && focusElement.GetType() == typeof(Label))
                {
                    focusElement.RaiseEvent(e);
                }
            }
        }

        private void Label_MouseUp(object sender, MouseButtonEventArgs e)
        {
        }

        private void Label_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
        }

        private void Label_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
        }

        public void SetPosition()
        {
            Canvas.SetTop(this.Label, rect.Y);
            Canvas.SetLeft(this.Label, rect.X);
            // Keep the drawn size in step with rect on EVERY reposition, not only when set through the Rect
            // property. Translate() and the X/Y setters move rect without touching the visuals, so a drag / box
            // move / step load left the ellipse at a stale size. rect is the single source of truth (probe
            // diameter); the ellipse is inset 2px like the constructor draws it.
            Label.Width = rect.Width;
            Label.Height = rect.Height;
            elip.Width = System.Math.Max(0, rect.Width - 2);
            elip.Height = System.Math.Max(0, rect.Height - 2);
        }

        public void TestImage(Mat source)
        {
            if (source == null) return;
            if (ParentCanvasSize.Width == 0 || ParentCanvasSize.Height == 0) return;
            double scaleX = source.Width / ParentCanvasSize.Width;
            double scaleY = source.Height / ParentCanvasSize.Height;

            OpenCvSharp.Rect rect = new OpenCvSharp.Rect(
                new OpenCvSharp.Point(this.Rect.X * scaleX, this.Rect.Y * scaleY),
                new OpenCvSharp.Size(this.Rect.Width * scaleX, this.Rect.Height * scaleY));

            using (var croppedMat = new Mat(source, rect))
            using (Mat gray = croppedMat.CvtColor(ColorConversionCodes.RGB2GRAY))
            {
                Intens = (int)gray.Mean().Val0;
                var cropImage = VisionModel.MatToBitmapSource(gray);
                CropImageHolder.Dispatcher.BeginInvoke(new Action(() => CropImageHolder.Source = cropImage));
                CropImage = cropImage;
            }
        }

        public string TestImage(Mat source, bool OutString = true)
        {
            if (source == null) return "0";
            if (ParentCanvasSize.Width == 0 || ParentCanvasSize.Height == 0) return "0";

            double scaleX = source.Width / ParentCanvasSize.Width;
            double scaleY = source.Height / ParentCanvasSize.Height;

            OpenCvSharp.Rect rect = new OpenCvSharp.Rect(
                new OpenCvSharp.Point(this.Rect.X * scaleX, this.Rect.Y * scaleY),
                new OpenCvSharp.Size(this.Rect.Width * scaleX, this.Rect.Height * scaleY));

            using (var croppedMat = new Mat(source, rect))
            using (Mat gray = croppedMat.CvtColor(ColorConversionCodes.BGR2GRAY))
            {
                Intens = (int)gray.Mean().Val0;
                var cropImage = VisionModel.MatToBitmapSource(gray);
                CropImageHolder.Dispatcher.BeginInvoke(new Action(() => CropImageHolder.Source = cropImage));
                CropImage = cropImage;
            }

            if (Intens >= Thresh)
            {
                return "1";
            }
            else
            {
                return "0";
            }
        }
    }
}