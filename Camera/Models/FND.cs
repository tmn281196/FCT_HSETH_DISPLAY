using Utility;
using OpenCvSharp;
using OpenCvSharp.Flann;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rect = System.Windows.Rect;

namespace Camera
{
    public class FND : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event EventHandler Selected;

        private void OnSelected()
        {
            Selected?.Invoke(this, null);
        }

        private Mat mat { get; set; } = new Mat();

        private string _SpectString = "8";

        [JsonIgnore]
        public string SpectString
        {
            get { return _SpectString; }
            set
            {
                if (value != null || value != _SpectString)
                    _SpectString = value;
                OnPropertyChanged("SpectString");
            }
        }

        private string _DetectedString;

        [JsonIgnore]
        public string DetectedString
        {
            get { return _DetectedString; }
            set
            {
                if (value != null || value != _DetectedString)
                {
                    _DetectedString = value;
                    OnPropertyChanged("DetectedString");
                    IsPass = DetectedString == SpectString;
                }
            }
        }

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
            }
        }

        private List<Models.SettingParram> _Param = new List<Models.SettingParram>();

        [JsonIgnore]
        public List<Models.SettingParram> Param
        {
            get { return _Param; }
            set
            {
                if (value != null || value != _Param) _Param = value;
                OnPropertyChanged("Param");
            }
        }

        private int _NoiseSize = 1;

        // Detection-tuning knobs: live-only, never persisted to the model file (the detector uses its own defaults).
        [JsonIgnore]
        public int NoiseSize
        {
            get { return _NoiseSize; }
            set
            {
                if (value != _NoiseSize && value > 1)
                {
                    _NoiseSize = value;
                    OnPropertyChanged("NoiseSize");
                }
            }
        }

        private double _Threshold = 100;

        [JsonIgnore]
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

        [JsonIgnore]
        public double Blur
        {
            get { return _Blur; }
            set
            {
                if (value % 2 != 0 || value != _Blur) _Blur = value;
                OnPropertyChanged("Blur");
            }
        }

        private enum TurnningState
        {
            wait,
            turning,
            end
        }

        private TurnningState turnningState = FND.TurnningState.wait;

        private bool _IsTurning;

        [JsonIgnore]
        public bool IsTurning
        {
            get { return _IsTurning; }
            set
            {
                if (value != _IsTurning)
                {
                    _IsTurning = value;
                    if (value)
                    {
                        TurningProgress = 0;
                    }
                    OnPropertyChanged("IsTurning");
                }
            }
        }

        private double _TurningProgress;

        [JsonIgnore]
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

        private Visibility _Visibility;

        public Visibility Visibility
        {
            get { return _Visibility; }
            set
            {
                if (value != _Visibility) _Visibility = value;
                Label.Visibility = value;
                if (_Visibility != Visibility.Visible)
                {
                    LabelBotLeft.Visibility = value;
                    LabelBotMid.Visibility = value;
                    LabelBotRight.Visibility = value;
                    LabelMidLeft.Visibility = value;
                    LabelMidRight.Visibility = value;
                    LabelTopLeft.Visibility = value;
                    LabelTopMid.Visibility = value;
                    LabelTopRight.Visibility = value;
                }

                // Cascade to the 7 segment probes: a probe shows only when the box is shown AND that probe is
                // used. An unused probe stays Hidden - never drawn grey. (Use is uniform per char via the header
                // checkbox, so in practice a char's probes show or hide together; this also does the right thing
                // for older mixed data by simply not drawing the unused ones.)
                // Must be set here, not left to the segment's own Use setter: that setter can't know whether the
                // FND family is even on screen, so without this an unrelated step would still show used probes.
                if (PointSegments != null)
                    foreach (var seg in PointSegments.LEDs)
                        seg.Visibility = (value == Visibility.Visible && seg.Use) ? Visibility.Visible : Visibility.Hidden;

                OnPropertyChanged("Visibility");
            }
        }

        public bool _Use = false;

        public bool Use
        {
            get { return _Use; }
            set
            {
                if (value != _Use) _Use = value;
                if (value)
                {
                    Visibility = Visibility.Visible;
                }
                else
                {
                    Visibility = Visibility.Hidden;
                }
                OnPropertyChanged("Use");
            }
        }

        private Canvas _ParentCanvas;

        private Canvas ParentCanvas
        {
            get { return _ParentCanvas; }
            set
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

        private Rect _ParentCanvasSize;

        // MUST STAY SERIALIZED - see LCD.ParentCanvasSize. The trailing `Rect` key needs a real canvas size to
        // get past the Rect setter's guard; [JsonIgnore] here made every ROI reload at its field initializer.
        public Rect ParentCanvasSize
        {
            get { return _ParentCanvasSize; }
            set { _ParentCanvasSize = value; }
        }

        public Label Label = new Label()
        {
            // FND ROI box: no border colour, translucent WHITE backdrop (~0.3) so the segment probes stand out.
            Background = new SolidColorBrush(Color.FromArgb(77, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.Red),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            // 0 thickness/padding so the dark background fills rect EXACTLY - a 1px (transparent) border used to inset
            // the box from rect while the handles centre on rect, which the canvas zoom magnified into a visible gap.
            BorderThickness = new Thickness(0),
            Focusable = true,
            Padding = new Thickness(0),
            Cursor = Cursors.SizeAll,
            VerticalContentAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

        public Image CropImageHolder = new Image();

        public Label LabelTopLeft = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 3,
            Height = 3,
            Cursor = Cursors.SizeNWSE,
            Focusable = true,
        };

        public Label LabelTopMid = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 3,
            Height = 3,
            Cursor = Cursors.SizeNS,
            Focusable = true,
        };

        public Label LabelTopRight = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 3,
            Height = 3,
            Cursor = Cursors.SizeNESW,
            Focusable = true,
        };

        public Label LabelMidLeft = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 3,
            Height = 3,
            Cursor = Cursors.SizeWE,
            Focusable = true,
        };

        public Label LabelMidRight = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 3,
            Height = 3,
            Cursor = Cursors.SizeWE,
            Focusable = true,
        };

        public Label LabelBotLeft = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 3,
            Height = 3,
            Cursor = Cursors.SizeNESW,
            Focusable = true,
        };

        public Label LabelBotMid = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 3,
            Height = 3,
            Cursor = Cursors.SizeNS,
            Focusable = true,
        };

        public Label LabelBotRight = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 3,
            Height = 3,
            Cursor = Cursors.SizeNWSE,
            Focusable = true,
        };

        private Rect OffsetMove;

        public Rect rect = new Rect()
        {
            Location = new System.Windows.Point(5, 5),
            Size = new System.Windows.Size(100, 50)
        };

        public LED _PointSegments = new LED();

        public LED PointSegments
        {
            get { return _PointSegments; }
            set { _PointSegments = value; }
        }

        public Rect Rect
        {
            get { return rect; }
            set
            {
                if (rect != value)
                {
                    if (value.X > 0 && value.X < ParentCanvasSize.Width - value.Width)
                    {
                        rect.X = value.X;
                        rect.Width = value.Width;
                        Label.Width = value.Width;
                    }

                    if (value.Y > 0 && value.Y < ParentCanvasSize.Height - value.Height)
                    {
                        rect.Y = value.Y;
                        rect.Height = value.Height;
                        Label.Height = value.Height;
                    }
                    SetPosition();
                }
            }
        }

        // Move this char box by (dx, dy). Bypasses the bounds-guarded `Rect` property above - see LCD.Translate.
        // NOTE this moves the BOX only. The 7 segment points in PointSegments are independent absolute
        // coordinates, not box-relative, so the caller must translate them by the same delta or the box and its
        // own segments drift apart.
        public void Translate(double dx, double dy)
        {
            rect = new Rect(System.Math.Round(rect.X + dx, 2), System.Math.Round(rect.Y + dy, 2), rect.Width, rect.Height);   // 2 dp
            SetPosition();
            OnPropertyChanged("Rect");
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
                    //Label.Content = name;
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

                    Label.MouseLeftButtonDown -= Label_MouseLeftButtonDown;
                    Label.MouseMove -= Label_MouseMove;
                    Label.MouseUp -= Label_MouseUp;

                    LabelBotLeft.Visibility = Visibility.Hidden;
                    LabelBotMid.Visibility = Visibility.Hidden;
                    LabelBotRight.Visibility = Visibility.Hidden;
                    LabelMidLeft.Visibility = Visibility.Hidden;
                    LabelMidRight.Visibility = Visibility.Hidden;
                    LabelTopLeft.Visibility = Visibility.Hidden;
                    LabelTopMid.Visibility = Visibility.Hidden;
                    LabelTopRight.Visibility = Visibility.Hidden;

                    LabelBotLeft.MouseMove -= LabelBotLeft_MouseMove;
                    LabelBotMid.MouseMove -= LabelBotMid_MouseMove;
                    LabelBotRight.MouseMove -= LabelBotRight_MouseMove;
                    LabelMidLeft.MouseMove -= LabelMidLeft_MouseMove;
                    LabelMidRight.MouseMove -= LabelMidRight_MouseMove;
                    LabelTopLeft.MouseMove -= LabelTopLeft_MouseMove;
                    LabelTopMid.MouseMove -= LabelTopMid_MouseMove;
                    LabelTopRight.MouseMove -= LabelTopRight_MouseMove;

                    LabelBotLeft.MouseDown -= LabelResize_MouseDown;
                    LabelBotMid.MouseDown -= LabelResize_MouseDown;
                    LabelBotRight.MouseDown -= LabelResize_MouseDown;
                    LabelMidLeft.MouseDown -= LabelResize_MouseDown;
                    LabelMidRight.MouseDown -= LabelResize_MouseDown;
                    LabelTopLeft.MouseDown -= LabelResize_MouseDown;
                    LabelTopMid.MouseDown -= LabelResize_MouseDown;
                    LabelTopRight.MouseDown -= LabelResize_MouseDown;

                    LabelBotLeft.LostKeyboardFocus -= Label_LostKeyboardFocus;
                    LabelBotMid.LostKeyboardFocus -= Label_LostKeyboardFocus;
                    LabelBotRight.LostKeyboardFocus -= Label_LostKeyboardFocus;
                    LabelMidLeft.LostKeyboardFocus -= Label_LostKeyboardFocus;
                    LabelMidRight.LostKeyboardFocus -= Label_LostKeyboardFocus;
                    LabelTopLeft.LostKeyboardFocus -= Label_LostKeyboardFocus;
                    LabelTopMid.LostKeyboardFocus -= Label_LostKeyboardFocus;
                    LabelTopRight.LostKeyboardFocus -= Label_LostKeyboardFocus;

                    LabelBotLeft.GotKeyboardFocus -= Label_GotKeyboardFocus;
                    LabelBotMid.GotKeyboardFocus -= Label_GotKeyboardFocus;
                    LabelBotRight.GotKeyboardFocus -= Label_GotKeyboardFocus;
                    LabelMidLeft.GotKeyboardFocus -= Label_GotKeyboardFocus;
                    LabelMidRight.GotKeyboardFocus -= Label_GotKeyboardFocus;
                    LabelTopLeft.GotKeyboardFocus -= Label_GotKeyboardFocus;
                    LabelTopMid.GotKeyboardFocus -= Label_GotKeyboardFocus;
                    LabelTopRight.GotKeyboardFocus -= Label_GotKeyboardFocus;
                }
            }
        }

        public FND()
        {
            Label.ToolTip = CropImageHolder;

            Label.GotKeyboardFocus += Label_GotKeyboardFocus;
            Label.LostKeyboardFocus += Label_LostKeyboardFocus;

            Label.KeyDown += Label_KeyDown;

            Label.MouseLeftButtonDown += Label_MouseLeftButtonDown;
            Label.MouseMove += Label_MouseMove;
            Label.MouseUp += Label_MouseUp;
            Label.MouseRightButtonDown += Label_MouseRightDown;

            LabelBotLeft.Visibility = Visibility.Hidden;
            LabelBotMid.Visibility = Visibility.Hidden;
            LabelBotRight.Visibility = Visibility.Hidden;
            LabelMidLeft.Visibility = Visibility.Hidden;
            LabelMidRight.Visibility = Visibility.Hidden;
            LabelTopLeft.Visibility = Visibility.Hidden;
            LabelTopMid.Visibility = Visibility.Hidden;
            LabelTopRight.Visibility = Visibility.Hidden;

            LabelBotLeft.MouseMove += LabelBotLeft_MouseMove;
            LabelBotMid.MouseMove += LabelBotMid_MouseMove;
            LabelBotRight.MouseMove += LabelBotRight_MouseMove;
            LabelMidLeft.MouseMove += LabelMidLeft_MouseMove;
            LabelMidRight.MouseMove += LabelMidRight_MouseMove;
            LabelTopLeft.MouseMove += LabelTopLeft_MouseMove;
            LabelTopMid.MouseMove += LabelTopMid_MouseMove;
            LabelTopRight.MouseMove += LabelTopRight_MouseMove;

            LabelBotLeft.MouseDown += LabelResize_MouseDown;
            LabelBotMid.MouseDown += LabelResize_MouseDown;
            LabelBotRight.MouseDown += LabelResize_MouseDown;
            LabelMidLeft.MouseDown += LabelResize_MouseDown;
            LabelMidRight.MouseDown += LabelResize_MouseDown;
            LabelTopLeft.MouseDown += LabelResize_MouseDown;
            LabelTopMid.MouseDown += LabelResize_MouseDown;
            LabelTopRight.MouseDown += LabelResize_MouseDown;

            LabelBotLeft.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelBotMid.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelBotRight.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelMidLeft.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelMidRight.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelTopLeft.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelTopMid.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelTopRight.LostKeyboardFocus += Label_LostKeyboardFocus;

            LabelBotLeft.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelBotMid.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelBotRight.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelMidLeft.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelMidRight.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelTopLeft.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelTopMid.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelTopRight.GotKeyboardFocus += Label_GotKeyboardFocus;

            for (int j = 3; j < 11; j++)
            {
                for (int i = 1; i < 254; i++)
                {
                    for (int z = 3; z < 11; z++)
                    {
                        Param.Add(new Models.SettingParram()
                        {
                            Threshold = i,
                            Blur = j,
                            Noise = z,
                            IsPass = false
                        });
                    }
                }
            }
        }

        public FND(int index)
        {
            rect = new Rect((VisionModel.FND_WIDTH + 10) * index + 10, 10, VisionModel.FND_WIDTH, VisionModel.FND_HEIGHT);
            Name = "FND" + (index + 1);

            PointSegments = new LED(new System.Windows.Point(rect.X, rect.Y), VisionModel.FND_DIAMETER_POINT, VisionModel.FND_THRESH_POINT);

            PointSegments.Arrange7Segment(VisionModel.FND_DIAMETER_POINT, rect);

            Label.ToolTip = CropImageHolder;

            Label.GotKeyboardFocus += Label_GotKeyboardFocus;
            Label.LostKeyboardFocus += Label_LostKeyboardFocus;

            Label.KeyDown += Label_KeyDown;

            Label.MouseLeftButtonDown += Label_MouseLeftButtonDown;
            Label.MouseRightButtonDown += Label_MouseRightDown;

            Label.MouseMove += Label_MouseMove;
            Label.MouseUp += Label_MouseUp;

            LabelBotLeft.Visibility = Visibility.Hidden;
            LabelBotMid.Visibility = Visibility.Hidden;
            LabelBotRight.Visibility = Visibility.Hidden;
            LabelMidLeft.Visibility = Visibility.Hidden;
            LabelMidRight.Visibility = Visibility.Hidden;
            LabelTopLeft.Visibility = Visibility.Hidden;
            LabelTopMid.Visibility = Visibility.Hidden;
            LabelTopRight.Visibility = Visibility.Hidden;

            LabelBotLeft.MouseMove += LabelBotLeft_MouseMove;
            LabelBotMid.MouseMove += LabelBotMid_MouseMove;
            LabelBotRight.MouseMove += LabelBotRight_MouseMove;
            LabelMidLeft.MouseMove += LabelMidLeft_MouseMove;
            LabelMidRight.MouseMove += LabelMidRight_MouseMove;
            LabelTopLeft.MouseMove += LabelTopLeft_MouseMove;
            LabelTopMid.MouseMove += LabelTopMid_MouseMove;
            LabelTopRight.MouseMove += LabelTopRight_MouseMove;

            LabelBotLeft.MouseDown += LabelResize_MouseDown;
            LabelBotMid.MouseDown += LabelResize_MouseDown;
            LabelBotRight.MouseDown += LabelResize_MouseDown;
            LabelMidLeft.MouseDown += LabelResize_MouseDown;
            LabelMidRight.MouseDown += LabelResize_MouseDown;
            LabelTopLeft.MouseDown += LabelResize_MouseDown;
            LabelTopMid.MouseDown += LabelResize_MouseDown;
            LabelTopRight.MouseDown += LabelResize_MouseDown;

            LabelBotLeft.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelBotMid.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelBotRight.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelMidLeft.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelMidRight.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelTopLeft.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelTopMid.LostKeyboardFocus += Label_LostKeyboardFocus;
            LabelTopRight.LostKeyboardFocus += Label_LostKeyboardFocus;

            LabelBotLeft.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelBotMid.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelBotRight.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelMidLeft.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelMidRight.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelTopLeft.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelTopMid.GotKeyboardFocus += Label_GotKeyboardFocus;
            LabelTopRight.GotKeyboardFocus += Label_GotKeyboardFocus;

            for (int j = 3; j < 11; j++)
            {
                for (int i = 1; i < 254; i++)
                {
                    for (int z = 3; z < 11; z++)
                    {
                        Param.Add(new Models.SettingParram()
                        {
                            Threshold = i,
                            Blur = j,
                            Noise = z,
                            IsPass = false
                        });
                    }
                }
            }
        }

        //Parent and positions

        public void SetParentCanvas(Canvas placeCanvas)
        {
            if (ParentCanvas != null)
            {
                ParentCanvas.Children.Remove(Label);
                ParentCanvas.Children.Remove(LabelTopLeft);
                ParentCanvas.Children.Remove(LabelTopMid);
                ParentCanvas.Children.Remove(LabelTopRight);
                ParentCanvas.Children.Remove(LabelMidLeft);
                ParentCanvas.Children.Remove(LabelMidRight);
                ParentCanvas.Children.Remove(LabelBotLeft);
                ParentCanvas.Children.Remove(LabelBotMid);
                ParentCanvas.Children.Remove(LabelBotRight);
            }

            this.ParentCanvas = placeCanvas;

            Label.Width = rect.Width;
            Label.Height = rect.Height;

            Canvas.SetTop(this.Label, rect.Y);
            Canvas.SetLeft(this.Label, rect.X);

            // Each handle sits OUTSIDE the box, touching the matching rect edge/corner (no overlap, no gap). Corner
            // handles meet the box at the corner point (e.g. top-left is shifted left by its width and up by its
            // height); edge handles sit against the far edge, centred on it. Keep in sync with SetPosition() below.
            const double H = 3;   // handle size (LabelTopLeft.Width/Height)
            Canvas.SetTop(this.LabelTopLeft, rect.Y - H);
            Canvas.SetLeft(this.LabelTopLeft, rect.X - H);

            Canvas.SetTop(this.LabelTopMid, rect.Y - H);
            Canvas.SetLeft(this.LabelTopMid, rect.X + rect.Width / 2 - H / 2);

            Canvas.SetTop(this.LabelTopRight, rect.Y - H);
            Canvas.SetLeft(this.LabelTopRight, rect.X + rect.Width);

            Canvas.SetTop(this.LabelMidLeft, rect.Y + rect.Height / 2 - H / 2);
            Canvas.SetLeft(this.LabelMidLeft, rect.X - H);

            Canvas.SetTop(this.LabelMidRight, rect.Y + rect.Height / 2 - H / 2);
            Canvas.SetLeft(this.LabelMidRight, rect.X + rect.Width);

            Canvas.SetTop(this.LabelBotLeft, rect.Y + rect.Height);
            Canvas.SetLeft(this.LabelBotLeft, rect.X - H);

            Canvas.SetTop(this.LabelBotMid, rect.Y + rect.Height);
            Canvas.SetLeft(this.LabelBotMid, rect.X + rect.Width / 2 - H / 2);

            Canvas.SetTop(this.LabelBotRight, rect.Y + rect.Height);
            Canvas.SetLeft(this.LabelBotRight, rect.X + rect.Width);

            placeCanvas.Children.Add(Label);

            placeCanvas.Children.Add(LabelTopLeft);
            placeCanvas.Children.Add(LabelTopMid);
            placeCanvas.Children.Add(LabelTopRight);

            placeCanvas.Children.Add(LabelMidLeft);
            placeCanvas.Children.Add(LabelMidRight);

            placeCanvas.Children.Add(LabelBotLeft);
            placeCanvas.Children.Add(LabelBotMid);
            placeCanvas.Children.Add(LabelBotRight);

            foreach (var led_segment in PointSegments.LEDs)
            {
                led_segment.SetParentCanvas(placeCanvas);
            }
        }

        private void Label_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            Rect oldCoor = new Rect(Rect.X, Rect.Y, Rect.Width, Rect.Height);
            Rect newCoor = Rect;

            double distanceMove = 1;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                distanceMove = 30;
            }
            switch (e.Key)
            {
                case Key.Left:
                    newCoor.X = rect.X - distanceMove;
                    break;

                case Key.Up:
                    newCoor.Y = rect.Y - distanceMove;
                    break;

                case Key.Right:
                    newCoor.X = rect.X + distanceMove;
                    break;

                case Key.Down:
                    newCoor.Y = rect.Y + distanceMove;
                    break;
            }
            Rect = newCoor;

            foreach (var led_segment in PointSegments.LEDs)
            {
                led_segment.Rect = new Rect(led_segment.rect.X + newCoor.X - oldCoor.X, led_segment.rect.Y + newCoor.Y - oldCoor.Y, led_segment.rect.Width, led_segment.rect.Height);
            }

            SetPosition();
        }

        private void LabelResize_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            Keyboard.Focus(sender as Label);
            Keyboard.Focus(sender as Label);
        }

        private void LabelTopRight_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.Handled == false)
            {
                Rect areaRect = Rect;
                areaRect.Width = Math.Abs(e.GetPosition(ParentCanvas).X - rect.X);
                areaRect.Height = Math.Abs(areaRect.Height + rect.Y - e.GetPosition(ParentCanvas).Y);
                areaRect.Y = e.GetPosition(ParentCanvas).Y;
                Rect = areaRect;

                PointSegments.Arrange7Segment(VisionModel.FND_DIAMETER_POINT, rect);
                SetPosition();
            }
        }

        private void LabelTopMid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.Handled == false)
            {
                Rect areaRect = Rect;
                areaRect.Height = Math.Abs(areaRect.Height + rect.Y - e.GetPosition(ParentCanvas).Y);
                areaRect.Y = e.GetPosition(ParentCanvas).Y;
                Rect = areaRect;

                PointSegments.Arrange7Segment(VisionModel.FND_DIAMETER_POINT, rect);
                SetPosition();
            }
        }

        private void LabelTopLeft_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.Handled == false)
            {
                Rect areaRect = Rect;
                areaRect.Width = Math.Abs(areaRect.Width + rect.X - e.GetPosition(ParentCanvas).X);
                areaRect.Height = Math.Abs(areaRect.Height + rect.Y - e.GetPosition(ParentCanvas).Y);
                areaRect.Y = e.GetPosition(ParentCanvas).Y;
                areaRect.X = e.GetPosition(ParentCanvas).X;
                Rect = areaRect;

                PointSegments.Arrange7Segment(VisionModel.FND_DIAMETER_POINT, rect);
                SetPosition();
            }
        }

        private void LabelMidRight_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.Handled == false)
            {
                Rect areaRect = Rect;
                areaRect.Width = Math.Abs(e.GetPosition(ParentCanvas).X - areaRect.X);
                //areaRect.Height = Math.Abs(areaRect.Height + rect.Y - e.GetPosition(ParentCanvas).Y);
                //areaRect.Y = e.GetPosition(ParentCanvas).Y;
                //areaRect.X = e.GetPosition(ParentCanvas).X;
                Rect = areaRect;

                PointSegments.Arrange7Segment(VisionModel.FND_DIAMETER_POINT, rect);
                SetPosition();
            }
        }

        private void LabelMidLeft_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.Handled == false)
            {
                Rect areaRect = Rect;
                areaRect.Width = Math.Abs(areaRect.Width + rect.X - e.GetPosition(ParentCanvas).X);
                //areaRect.Height = Math.Abs(areaRect.Height + rect.Y - e.GetPosition(ParentCanvas).Y);
                //areaRect.Y = e.GetPosition(ParentCanvas).Y;
                areaRect.X = e.GetPosition(ParentCanvas).X;
                Rect = areaRect;

                PointSegments.Arrange7Segment(VisionModel.FND_DIAMETER_POINT, rect);
                SetPosition();
            }
        }

        private void LabelBotRight_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.Handled == false)
            {
                Rect areaRect = Rect;
                areaRect.Width = Math.Abs(e.GetPosition(ParentCanvas).X - rect.X);
                areaRect.Height = Math.Abs(e.GetPosition(ParentCanvas).Y - rect.Y);
                Rect = areaRect;

                PointSegments.Arrange7Segment(VisionModel.FND_DIAMETER_POINT, rect);
                SetPosition();
            }
        }

        private void LabelBotMid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.Handled == false)
            {
                Rect areaRect = Rect;
                //areaRect.Width = Math.Abs(areaRect.Width + rect.X - e.GetPosition(ParentCanvas).X);
                areaRect.Height = Math.Abs(e.GetPosition(ParentCanvas).Y - areaRect.Y);
                //areaRect.Y = e.GetPosition(ParentCanvas).Y;
                //areaRect.X = e.GetPosition(ParentCanvas).X;
                Rect = areaRect;

                PointSegments.Arrange7Segment(VisionModel.FND_DIAMETER_POINT, rect);
                SetPosition();
            }
        }

        private void LabelBotLeft_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.Handled == false)
            {
                Rect areaRect = Rect;
                areaRect.Width = Math.Abs(areaRect.Width + rect.X - e.GetPosition(ParentCanvas).X);
                areaRect.Height = Math.Abs(e.GetPosition(ParentCanvas).Y - rect.Y);
                //areaRect.Y = e.GetPosition(ParentCanvas).Y;
                areaRect.X = e.GetPosition(ParentCanvas).X;
                Rect = areaRect;

                PointSegments.Arrange7Segment(VisionModel.FND_DIAMETER_POINT, rect);
                SetPosition();
            }
        }

        private void Label_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!e.Handled)
            {
                LabelBotLeft.MouseMove -= LabelBotLeft_MouseMove;
                LabelBotMid.MouseMove -= LabelBotMid_MouseMove;
                LabelBotRight.MouseMove -= LabelBotRight_MouseMove;
                LabelMidLeft.MouseMove -= LabelMidLeft_MouseMove;
                LabelMidRight.MouseMove -= LabelMidRight_MouseMove;
                LabelTopLeft.MouseMove -= LabelTopLeft_MouseMove;
                LabelTopMid.MouseMove -= LabelTopMid_MouseMove;
                LabelTopRight.MouseMove -= LabelTopRight_MouseMove;

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
            // Hard block on read-only (Auto/Manual pages) - see the note in SingleLED.Label_MouseMove.
            if (IsReadOnly) return;
            //Console.WriteLine("Label raise event");
            if (e.LeftButton == MouseButtonState.Pressed && !e.Handled && Label.IsKeyboardFocused)
            {
                LabelBotLeft.MouseMove -= LabelBotLeft_MouseMove;
                LabelBotMid.MouseMove -= LabelBotMid_MouseMove;
                LabelBotRight.MouseMove -= LabelBotRight_MouseMove;
                LabelMidLeft.MouseMove -= LabelMidLeft_MouseMove;
                LabelMidRight.MouseMove -= LabelMidRight_MouseMove;
                LabelTopLeft.MouseMove -= LabelTopLeft_MouseMove;
                LabelTopMid.MouseMove -= LabelTopMid_MouseMove;
                LabelTopRight.MouseMove -= LabelTopRight_MouseMove;

                e.Handled = true;
                Rect oldCoor = new Rect(Rect.X, Rect.Y, Rect.Width, Rect.Height);
                Rect newCoor = Rect;

                newCoor.X = e.GetPosition(ParentCanvas).X - OffsetMove.Width;
                newCoor.Y = e.GetPosition(ParentCanvas).Y - OffsetMove.Height;
                Rect = newCoor;

                // Move the segments by the same delta as the box, via Translate() - NOT the `Rect` property.
                // The Rect setter clamps against ParentCanvasSize, which on a freshly built live model is 0x0
                // until the canvas is measured, so it silently dropped the move: dragging the box left the
                // segments behind (and they then reverted on the next step switch). Translate offsets rect
                // directly and syncs the X/Y cache the writeback reads, so the move sticks. It also redraws
                // (SetPosition) at the new position without touching the ellipse radius.
                double segDx = newCoor.X - oldCoor.X;
                double segDy = newCoor.Y - oldCoor.Y;
                foreach (var led_segment in PointSegments.LEDs)
                {
                    led_segment.Translate(segDx, segDy);
                }

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

        private void Label_MouseRightDown(object sender, MouseButtonEventArgs e)
        {
            if (!e.Handled)
            {
                LabelBotLeft.MouseMove -= LabelBotLeft_MouseMove;
                LabelBotMid.MouseMove -= LabelBotMid_MouseMove;
                LabelBotRight.MouseMove -= LabelBotRight_MouseMove;
                LabelMidLeft.MouseMove -= LabelMidLeft_MouseMove;
                LabelMidRight.MouseMove -= LabelMidRight_MouseMove;
                LabelTopLeft.MouseMove -= LabelTopLeft_MouseMove;
                LabelTopMid.MouseMove -= LabelTopMid_MouseMove;
                LabelTopRight.MouseMove -= LabelTopRight_MouseMove;

                e.Handled = true;

                PointSegments.Arrange7Segment(VisionModel.FND_DIAMETER_POINT, rect);
                SetPosition();
            }
        }

        private void Label_MouseUp(object sender, MouseButtonEventArgs e)
        {
            LabelBotLeft.MouseMove += LabelBotLeft_MouseMove;
            LabelBotMid.MouseMove += LabelBotMid_MouseMove;
            LabelBotRight.MouseMove += LabelBotRight_MouseMove;
            LabelMidLeft.MouseMove += LabelMidLeft_MouseMove;
            LabelMidRight.MouseMove += LabelMidRight_MouseMove;
            LabelTopLeft.MouseMove += LabelTopLeft_MouseMove;
            LabelTopMid.MouseMove += LabelTopMid_MouseMove;
            LabelTopRight.MouseMove += LabelTopRight_MouseMove;
        }

        private void Label_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            LabelBotLeft.Visibility = Visibility.Hidden;
            LabelBotMid.Visibility = Visibility.Hidden;
            LabelBotRight.Visibility = Visibility.Hidden;
            LabelMidLeft.Visibility = Visibility.Hidden;
            LabelMidRight.Visibility = Visibility.Hidden;
            LabelTopLeft.Visibility = Visibility.Hidden;
            LabelTopMid.Visibility = Visibility.Hidden;
            LabelTopRight.Visibility = Visibility.Hidden;
        }

        private void Label_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            LabelBotLeft.Visibility = Visibility.Visible;
            LabelBotMid.Visibility = Visibility.Visible;
            LabelBotRight.Visibility = Visibility.Visible;
            LabelMidLeft.Visibility = Visibility.Visible;
            LabelMidRight.Visibility = Visibility.Visible;
            LabelTopLeft.Visibility = Visibility.Visible;
            LabelTopMid.Visibility = Visibility.Visible;
            LabelTopRight.Visibility = Visibility.Visible;
        }

        public void SetLEDSegmentPostion()
        {
            foreach (var led_segment in PointSegments.LEDs)
            {
                led_segment.SetPosition();
            }
        }

        public void SetPosition()
        {
            Canvas.SetTop(this.Label, rect.Y);
            Canvas.SetLeft(this.Label, rect.X);
            // Sync the box size to rect HERE too. A move/resize that bypasses the Rect setter (Translate, X/Y setters)
            // left Label.Width stale, so the right/bottom handles - which read Label.Width - drifted off the corners.
            // Handles now key off rect (the single source of truth), identical to the SetParentCanvas block above.
            Label.Width = rect.Width;
            Label.Height = rect.Height;

            // Handles sit OUTSIDE the box touching each rect edge/corner (see the SetParentCanvas block for the rule).
            const double H = 3;   // handle size (LabelTopLeft.Width/Height)
            Canvas.SetTop(this.LabelTopLeft, rect.Y - H);
            Canvas.SetLeft(this.LabelTopLeft, rect.X - H);

            Canvas.SetTop(this.LabelTopMid, rect.Y - H);
            Canvas.SetLeft(this.LabelTopMid, rect.X + rect.Width / 2 - H / 2);

            Canvas.SetTop(this.LabelTopRight, rect.Y - H);
            Canvas.SetLeft(this.LabelTopRight, rect.X + rect.Width);

            Canvas.SetTop(this.LabelMidLeft, rect.Y + rect.Height / 2 - H / 2);
            Canvas.SetLeft(this.LabelMidLeft, rect.X - H);

            Canvas.SetTop(this.LabelMidRight, rect.Y + rect.Height / 2 - H / 2);
            Canvas.SetLeft(this.LabelMidRight, rect.X + rect.Width);

            Canvas.SetTop(this.LabelBotLeft, rect.Y + rect.Height);
            Canvas.SetLeft(this.LabelBotLeft, rect.X - H);

            Canvas.SetTop(this.LabelBotMid, rect.Y + rect.Height);
            Canvas.SetLeft(this.LabelBotMid, rect.X + rect.Width / 2 - H / 2);

            Canvas.SetTop(this.LabelBotRight, rect.Y + rect.Height);
            Canvas.SetLeft(this.LabelBotRight, rect.X + rect.Width);

            SetLEDSegmentPostion();
        }

        //Get and process image

        public void TestImage(Mat source)
        {
            PointSegments.GetValue(source);

            SegementCharacter seg_char = null;

            DetectedString = string.Empty;
            seg_char = SEG_LOOKUP.Where(item => item.digitString.Equals(PointSegments.CalculatorBinaryOutputString)).FirstOrDefault();

            if (seg_char != null)
            {
                DetectedString = seg_char.character.ToString();
            }
            else
            {
                if(PointSegments.CalculatorBinaryOutputString != "0000000")
                {
                    DetectedString = "<invalid>";
                }
            }

            if (IsTurning && turnningState == TurnningState.wait)
            {
                mat?.Dispose();
                mat = source.Clone();
                AutoCalibration();
                turnningState = TurnningState.turning;
                return;
            }
            if (IsTurning)
            {
                mat?.Dispose();
                mat = source.Clone();
                return;
            }

            if (source == null) return;
            if (ParentCanvasSize.Width == 0 || ParentCanvasSize.Height == 0) return;
            double scaleX = source.Width / ParentCanvasSize.Width;
            double scaleY = source.Height / ParentCanvasSize.Height;

            OpenCvSharp.Rect rect = new OpenCvSharp.Rect(
                new OpenCvSharp.Point(this.Rect.X * scaleX, this.Rect.Y * scaleY),
                new OpenCvSharp.Size(this.Rect.Width * scaleX, this.Rect.Height * scaleY));
            using (var croppedMat = new Mat(source, rect))
            {
                using (var processedBitmap = SeventSegmentDetect(croppedMat, Threshold, Blur, NoiseSize, out string data))
                {
                    //this.DetectedString = data;
                    var cropImage = VisionModel.MatToBitmapSource(processedBitmap);
                    CropImageHolder.Dispatcher.BeginInvoke(new Action(() => CropImageHolder.Source = cropImage));
                    CropImage = cropImage;
                }
            }
        }

        public async void AutoCalibration()
        {
            if (mat == null || ParentCanvasSize.Width == 0 || ParentCanvasSize.Height == 0)
            {
                turnningState = TurnningState.wait;
                IsTurning = false;
                return;
            }

            turnningState = TurnningState.turning;

            double scaleX = mat.Width / ParentCanvasSize.Width;
            double scaleY = mat.Height / ParentCanvasSize.Height;

            OpenCvSharp.Rect rect = new OpenCvSharp.Rect(
                new OpenCvSharp.Point(this.Rect.X * scaleX, this.Rect.Y * scaleY),
                new OpenCvSharp.Size(this.Rect.Width * scaleX, this.Rect.Height * scaleY));

            for (int i = 0; i < Param.Count; i++)
            {
                using (var croppedMat = new Mat(mat, rect))
                {
                    TurningProgress = (i / (double)Param.Count * 100);
                    var brightness = Param[i];
                    using (var processedBitmap = SeventSegmentDetect(croppedMat, brightness.Threshold, brightness.Blur, (int)brightness.Noise, out string data))
                    {
                        DetectedString = data;
                        brightness.IsPass = IsPass;
                        var cropImage = VisionModel.MatToBitmapSource(processedBitmap);
                        CropImageHolder.Dispatcher.BeginInvoke(new Action(() => CropImageHolder.Source = cropImage));
                        CropImage = cropImage;
                    }
                    if (i % 100 == 0)
                    {
                        await Task.Delay(5);
                    }
                    Param[i] = brightness;
                }
                TurningProgress = 100;
            }
            var bestSlution = Param.Where(o => o.IsPass).ToList();
            if (bestSlution.Count > 1)
            {
                var bestBrightness = bestSlution.GroupBy(o => o.Threshold).OrderByDescending(s => s.Count()).First().Key;
                var bestBlur = bestSlution.GroupBy(o => o.Blur).OrderByDescending(s => s.Count()).First().Key;
                var bestNoise = bestSlution.GroupBy(o => o.Noise).OrderByDescending(s => s.Count()).First().Key;
                Threshold = bestBrightness;
                Blur = bestBlur;
                NoiseSize = (int)bestNoise;
            }
            turnningState = TurnningState.wait;
            IsTurning = false;
        }

        public static Mat SeventSegmentDetect(Mat source, double brightness, double blur, int noiseSize, out string detectedString)
        {
            detectedString = "";

            if (source.Height < 10 || source.Width < 10)
            {
                return new Mat();
            }

            Mat mInput = source;
            Mat gray = mInput.CvtColor(ColorConversionCodes.RGB2GRAY);

            gray.MinMaxIdx(out _, out double maxval);

            Mat edge = gray.Threshold(brightness, 255, ThresholdTypes.Binary);
            gray.Dispose();
            //Cv2.FastNlMeansDenoising(edge, edge, 0,4);
            Mat blurred = edge.GaussianBlur(new OpenCvSharp.Size(1, 21), 0, blur);
            Mat blurgreen = blurred.Threshold(50, 255, ThresholdTypes.Binary);
            blurred.Dispose();
            blurgreen.Dispose();
            OpenCvSharp.Point[][] contour;
            HierarchyIndex[] hierarchy;
            List<OpenCvSharp.Rect> digitContour = new List<OpenCvSharp.Rect>();

            Mat moutput = edge.CvtColor(ColorConversionCodes.GRAY2RGB);
            edge.Dispose();

            //Cv2.FindContours(blurgreen, out contour, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            //for (int i = 0; i < contour.Length; i++)
            //{
            //    //mInput.DrawContours(contour, i, new Scalar(255, 0, 0));
            //    OpenCvSharp.Rect rect = Cv2.BoundingRect(contour[i]);
            //    if (Cv2.ContourArea(contour[i]) > noiseSize * noiseSize && rect.Height > (blur * 2 + 2))
            //    {
            //        digitContour.Add(rect);
            //    }
            //    else
            //    {
            //        moutput.DrawContours(contour, i, new Scalar(0, 0, 255));
            //    }
            //}
            //digitContour = digitContour.OrderBy(o => o.Left).ToList();
            //if (digitContour.Count < 1) return moutput;
            //int maxHeightValue = digitContour.OrderBy(x => x.Height).ToList()[0].Height;
            //int maxTopValue = digitContour.OrderBy(x => x.Top).ToList()[0].Top;
            //foreach (var item in digitContour)
            //{
            //    //Console.Write(item.Left.ToString() + "->");
            //    var rect = item;
            //    try
            //    {
            //        detectedString += DetectSegmentChar(rect, new Mat(edge, item), moutput, (int)blur, maxHeight: maxHeightValue, minTop: maxTopValue);
            //    }
            //    catch (OpenCvSharp.OpenCvSharpException er)
            //    {
            //        Console.WriteLine(er.Message);
            //    }
            //}
            return moutput;
        }

        private static char DetectSegmentChar(OpenCvSharp.Rect rectg, Mat Matinput, Mat colorMat, int blurOffset, bool IsDoubleDot = false, int maxHeight = 0, int minTop = 0)
        {
            double threadShot = 100;
            //Matinput.MinMaxIdx(out _, out threadShot);
            //threadShot *= 0.5;
            Mat input = Matinput.Threshold(threadShot, 255, ThresholdTypes.Otsu);
            SegementCharacter segement = new SegementCharacter();
            var W = input.Width;
            var H = input.Height;
            Mat matDigit;

            OpenCvSharp.Rect rect = new OpenCvSharp.Rect();

            if (IsDoubleDot)
            {
                W = input.Width;
                H = input.Height;

                rect = new OpenCvSharp.Rect(0, 0, W, H / 2);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > 10)
                {
                    segement.digit[2] = 1;
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                }

                rect = new OpenCvSharp.Rect(0, H / 2, W, H / 2);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > 10)
                {
                    segement.digit[5] = 1;
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                }
            }
            else if ((double)H / W > 2)
            {
                W = input.Width;
                H = input.Height;

                rect = new OpenCvSharp.Rect(0, 0, W, H / 2);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > threadShot)
                {
                    segement.digit[2] = 1;
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                }

                rect = new OpenCvSharp.Rect(0, H / 2, W, H / 2);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > threadShot)
                {
                    segement.digit[5] = 1;
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                }
            }
            else if ((double)W / H > 1.2)
            {
                W = input.Width;
                H = input.Height;

                rect = new OpenCvSharp.Rect(0, 0, W, H);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > threadShot)
                {
                    segement.digit[3] = 1;
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                }
            }
            else if ((double)H / W < 1.2)
            {
                if (rectg.Top < (minTop + maxHeight / 2))
                {
                    input.Resize(new OpenCvSharp.Size(4 * W, 4 * H), 4, 8);
                    W = input.Width;
                    H = input.Height - blurOffset * 2;
                    rect = new OpenCvSharp.Rect(W / 4, blurOffset, W / 2, H / 6);
                    matDigit = new Mat(input, rect);
                    if (matDigit.Mean().Val0 > threadShot)
                    {
                        segement.digit[0] = 1;
                        colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                        //colorMat.PutText("1", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                    }

                    rect = new OpenCvSharp.Rect(0, H / 7 + blurOffset, W / 4, H - H / 6);
                    matDigit = new Mat(input, rect);
                    if (matDigit.Mean().Val0 > threadShot)
                    {
                        segement.digit[1] = 1;
                        // colorMat.PutText("2", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                        colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                    }

                    rect = new OpenCvSharp.Rect(3 * W / 4, H / 7 + blurOffset, W / 4, H - H / 6);
                    matDigit = new Mat(input, rect);
                    if (matDigit.Mean().Val0 > threadShot)
                    {
                        segement.digit[2] = 1;
                        //colorMat.PutText("3", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                        colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                    }

                    rect = new OpenCvSharp.Rect(W / 4, H - H / 4 + blurOffset, W / 2, H / 4);
                    matDigit = new Mat(input, rect);
                    if (matDigit.Mean().Val0 > threadShot)
                    {
                        segement.digit[3] = 1;
                        //colorMat.PutText("4", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                        colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                    }
                }
                else
                {
                    input.Resize(new OpenCvSharp.Size(4 * W, 8 * H), 4, 8);
                    W = input.Width;
                    H = input.Height - blurOffset * 2;

                    rect = new OpenCvSharp.Rect(W / 4, blurOffset, W / 2, H / 6);
                    matDigit = new Mat(input, rect);
                    if (matDigit.Mean().Val0 > threadShot)
                    {
                        segement.digit[3] = 1;
                        colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                        //colorMat.PutText("1", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                    }

                    rect = new OpenCvSharp.Rect(0, H / 7 + blurOffset, W / 4, H - H / 6);
                    matDigit = new Mat(input, rect);
                    if (matDigit.Mean().Val0 > threadShot)
                    {
                        segement.digit[4] = 1;
                        // colorMat.PutText("2", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                        colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                    }

                    rect = new OpenCvSharp.Rect(3 * W / 4, H / 7 + blurOffset, W / 4, H - H / 6);
                    matDigit = new Mat(input, rect);

                    Console.WriteLine(matDigit.Mean().Val0);
                    if (matDigit.Mean().Val0 > threadShot)
                    {
                        segement.digit[5] = 1;
                        //colorMat.PutText("3", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                        colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                    }

                    rect = new OpenCvSharp.Rect(W / 4, H - H / 4 + blurOffset, W / 2, H / 4);
                    matDigit = new Mat(input, rect);
                    if (matDigit.Mean().Val0 > threadShot)
                    {
                        segement.digit[6] = 1;
                        //colorMat.PutText("4", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                        colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                    }
                }
            }
            else
            {
                input.Resize(new OpenCvSharp.Size(4 * W, 8 * H), 4, 8);
                W = input.Width;
                H = input.Height - blurOffset * 2;

                rect = new OpenCvSharp.Rect(W / 4, blurOffset, W / 2, H / 6);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > threadShot)
                {
                    segement.digit[0] = 1;
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                    //colorMat.PutText("1", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                }

                rect = new OpenCvSharp.Rect(0, H / 7 + blurOffset, W / 4, H / 3);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > threadShot)
                {
                    segement.digit[1] = 1;
                    // colorMat.PutText("2", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                }

                rect = new OpenCvSharp.Rect(3 * W / 4, H / 7 + blurOffset, W / 4, H / 3);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > threadShot)
                {
                    segement.digit[2] = 1;
                    //colorMat.PutText("3", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                }

                rect = new OpenCvSharp.Rect(W / 4, H / 2 - H / 12 + blurOffset, W / 2, H / 6);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > threadShot)
                {
                    segement.digit[3] = 1;
                    //colorMat.PutText("4", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                }

                rect = new OpenCvSharp.Rect(0, 4 * H / 7 + blurOffset, W / 4, H / 3);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > threadShot)
                {
                    segement.digit[4] = 1;
                    //colorMat.PutText("5", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                }

                rect = new OpenCvSharp.Rect(3 * W / 4, 4 * H / 7 + blurOffset, W / 4, H / 3);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > threadShot)
                {
                    segement.digit[5] = 1;
                    //colorMat.PutText("6", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                }

                rect = new OpenCvSharp.Rect(W / 4, H - H / 6 + blurOffset, W / 2, H / 6);
                matDigit = new Mat(input, rect);
                if (matDigit.Mean().Val0 > threadShot)
                {
                    segement.digit[6] = 1;
                    //colorMat.PutText("7", new OpenCvSharp.Point(rect.X, rect.Y), HersheyFonts.HersheySimplex, 1, new Scalar(255, 0, 0));
                    colorMat.Rectangle(new OpenCvSharp.Rect(rectg.X + rect.X, rectg.Y + rect.Y, rect.Width, rect.Height), new Scalar(255, 0, 0));
                }
            }

            var returnVal = new SegementCharacter();
            returnVal.digit = segement.digit;
            foreach (var item in SEG_LOOKUP)
            {
                if (item.digitString == returnVal.digitString)
                {
                    returnVal = item;
                    break;
                }
            }
            return returnVal.character;
        }

        public static void InitializeFNDLearning()
        {
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'A', digit = new byte[7] { 1, 1, 1, 0, 1, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'C', digit = new byte[7] { 1, 0, 0, 1, 1, 1, 0 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'E', digit = new byte[7] { 1, 0, 0, 1, 1, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'F', digit = new byte[7] { 1, 0, 0, 0, 1, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'H', digit = new byte[7] { 0, 1, 1, 0, 1, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'L', digit = new byte[7] { 0, 0, 0, 1, 1, 1, 0 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'P', digit = new byte[7] { 1, 1, 0, 0, 1, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'U', digit = new byte[7] { 0, 1, 1, 1, 1, 1, 0 } });

            //SEG_LOOKUP.Add(new SegementCharacter { character = 'b', digit = new byte[7] { 0, 0, 1, 1, 1, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'c', digit = new byte[7] { 0, 0, 0, 1, 1, 0, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'd', digit = new byte[7] { 0, 1, 1, 1, 1, 0, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'h', digit = new byte[7] { 0, 0, 1, 0, 1, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'n', digit = new byte[7] { 0, 0, 1, 0, 1, 0, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'o', digit = new byte[7] { 0, 0, 1, 1, 1, 0, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'r', digit = new byte[7] { 0, 0, 0, 0, 1, 0, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 't', digit = new byte[7] { 0, 0, 0, 1, 1, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'u', digit = new byte[7] { 0, 0, 1, 1, 1, 0, 0 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = 'y', digit = new byte[7] { 0, 1, 1, 1, 0, 1, 1 } });

            //SEG_LOOKUP.Add(new SegementCharacter { character = '0', digit = new byte[7] { 1, 1, 1, 1, 1, 1, 0 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = '1', digit = new byte[7] { 0, 1, 1, 0, 0, 0, 0 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = '2', digit = new byte[7] { 1, 1, 0, 1, 1, 0, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = '3', digit = new byte[7] { 1, 1, 1, 1, 0, 0, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = '4', digit = new byte[7] { 0, 1, 1, 0, 0, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = '5', digit = new byte[7] { 1, 0, 1, 1, 0, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = '6', digit = new byte[7] { 1, 0, 1, 1, 1, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = '7', digit = new byte[7] { 1, 1, 1, 0, 0, 1, 0 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = '8', digit = new byte[7] { 1, 1, 1, 1, 1, 1, 1 } });
            //SEG_LOOKUP.Add(new SegementCharacter { character = '9', digit = new byte[7] { 1, 1, 1, 1, 0, 1, 1 } });
        }

        public static ObservableCollection<SegementCharacter> SEG_LOOKUP { get; set; } = new ObservableCollection<SegementCharacter>()
        {
            //new SegementCharacter(){character = ' ', digit = new byte[7] {0,0,0,0,0,0,0}},
            //new SegementCharacter(){character = '-', digit = new byte[7] {0,0,0,1,0,0,0}},
            //new SegementCharacter(){character = 'N', digit = new byte[7] {0,0,0,1,1,1,0}},
            //new SegementCharacter(){character = 'O', digit = new byte[7] {0,0,0,1,1,1,1}},
            //new SegementCharacter(){character = '0', digit = new byte[7] {1,1,1,0,1,1,1}},
            //new SegementCharacter(){character = '1', digit = new byte[7] {0,0,1,0,0,1,0}},
            //new SegementCharacter(){character = '2', digit = new byte[7] {1,0,1,1,1,0,1}},
            //new SegementCharacter(){character = '3', digit = new byte[7] {1,0,1,1,0,1,1}},
            //new SegementCharacter(){character = '4', digit = new byte[7] {0,1,1,1,0,1,0}},
            //new SegementCharacter(){character = '5', digit = new byte[7] {1,1,0,1,0,1,1}},
            //new SegementCharacter(){character = '6', digit = new byte[7] {1,1,0,1,1,1,1}},
            //new SegementCharacter(){character = '7', digit = new byte[7] {1,0,1,0,0,1,0}},
            //new SegementCharacter(){character = '8', digit = new byte[7] {1,1,1,1,1,1,1}},
            //new SegementCharacter(){character = '9', digit = new byte[7] {1,1,1,1,0,1,1}},
            //new SegementCharacter(){character = 'A', digit = new byte[7] {1,1,1,1,1,1,0}},
            //new SegementCharacter(){character = 'B', digit = new byte[7] {0,1,0,1,1,1,1}},
            //new SegementCharacter(){character = 'C', digit = new byte[7] {1,1,0,0,1,0,1}},
            //new SegementCharacter(){character = 'D', digit = new byte[7] {0,0,1,1,1,1,1}},
            //new SegementCharacter(){character = 'E', digit = new byte[7] {1,1,0,1,1,0,1}},
            //new SegementCharacter(){character = 'F', digit = new byte[7] {1,1,0,1,1,0,0}},
        };
    }

    public class SegementCharacter : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private char _character = ' ';

        public char character
        {
            get { return _character; }
            set
            {
                if (value != _character)
                {
                    _character = value;
                    NotifyPropertyChanged("character");
                }
            }
        }

        private byte[] digits = new byte[7] { 0, 0, 0, 0, 0, 0, 0 };

        public byte[] digit
        {
            get { return digits; }
            set
            {
                if (digits != value)
                {
                    digits = value;
                    digitString = "";
                    foreach (byte b in digits)
                    {
                        digitString += b;
                    }
                }
            }
        }

        public string digitString
        {
            get
            {
                var digitStr = "";
                foreach (byte b in digits)
                {
                    digitStr += b;
                }
                return digitStr;
            }
            private set { }
        }

        public void DigitChange()
        {
            NotifyPropertyChanged("digitString");
            NotifyPropertyChanged("digit");
        }
    }
}