using VTMControls.DeviceControl.VisionTest;
using DirectShowLib;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using OpenCvSharp.Extensions;
using OpenCvSharp.WpfExtensions;
using Sdcb.PaddleOCR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static System.Net.Mime.MediaTypeNames;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rect = System.Windows.Rect;

namespace VTMControls.DeviceControl
{
    public class LCD : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // (removed) `private static OCRTesseract tesseract = OCRTesseract.Create("tessData", psmode: 13);`
        // Nothing ever read it - LCD reads through PaddleOCR - but being a STATIC field initializer it ran
        // OCRTesseract.Create at type-init, so it loaded the tessData/eng.* files on first touch of this class
        // and threw TypeInitializationException when they were absent. That is what kept ~30 MB of Tesseract
        // training data alive in the build. Deleting the data alone would not have been enough.

        public event EventHandler Selected;

        private void OnSelected()
        {
            Selected?.Invoke(this, null);
        }

        private Mat mat { get; set; } = new Mat();

        private string _SpectString = "8888";

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
                    DetectCaption.Dispatcher.BeginInvoke(new Action(() =>
                        DetectCaption.Content = value   // detected string shown ABOVE the box (see DetectCaption)
                    ));
                }
                OnPropertyChanged("IsPass");
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

        private int _NoiseSize = 5;

        // Detection-tuning knobs: live-only, never persisted to the model file (the detector uses its own defaults).
        [JsonIgnore]
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

        private Visibility _Visibility = Visibility.Visible;

        public Visibility Visibility
        {
            get { return _Visibility; }
            set
            {
                if (value != _Visibility) _Visibility = value;
                Label.Visibility = value;
                DetectCaption.Visibility = value;   // caption follows the box
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
                OnPropertyChanged("Visibility");
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

        // MUST STAY SERIALIZED. It looks like runtime UI state, but the file's trailing `Rect` key is assigned
        // AFTER this one, and the Rect setter's guard needs a real canvas size to accept it. Persisting this is
        // what arms that self-heal; [JsonIgnore] here left the canvas 0x0 at load, the guard then rejected Rect
        // and every Width/Height write, and EVERY ROI reloaded at its 10x10 field initializer - measured on the
        // real ok.vmdl: LED sizes went 32/32 correct (9x9) -> 0/32. LED.TestImage crops from that rect, so it
        // silently changes pass/fail. See [[project-model-serializer-jsonignore]].
        public Rect ParentCanvasSize
        {
            get { return _ParentCanvasSize; }
            set { _ParentCanvasSize = value; }
        }

        public Label Label = new Label()
        {
            // Match the FND box: faint translucent WHITE backdrop (~0.2), no border; detected string shows dark so it
            // stays readable on the white backdrop (white-on-white would be invisible).
            Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.Black),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Focusable = true,
            // Left padding so the detected string isn't flush against the box edge; backdrop still fills the label.
            Padding = new Thickness(5, 0, 0, 0),
            Cursor = Cursors.SizeAll,
            VerticalContentAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

        // Detected string shown ABOVE the box (sitting on the top edge), not inside it. White text, no background.
        // Positioned by SetPosition just above rect.Y; IsHitTestVisible=false so it never steals the mouse from the
        // box/handles underneath.
        public Label DetectCaption = new Label()
        {
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 0, 4, 0),
            Height = 22,
            FontSize = 13,
            IsHitTestVisible = false,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

        public Image CropImageHolder = new Image();

        public Label LabelTopLeft = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 5,
            Height = 5,
            Cursor = Cursors.SizeNWSE,
            Focusable = true,
        };

        public Label LabelTopMid = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 5,
            Height = 5,
            Cursor = Cursors.SizeNS,
            Focusable = true,
        };

        public Label LabelTopRight = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 5,
            Height = 5,
            Cursor = Cursors.SizeNESW,
            Focusable = true,
        };

        public Label LabelMidLeft = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 5,
            Height = 5,
            Cursor = Cursors.SizeWE,
            Focusable = true,
        };

        public Label LabelMidRight = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 5,
            Height = 5,
            Cursor = Cursors.SizeWE,
            Focusable = true,
        };

        public Label LabelBotLeft = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 5,
            Height = 5,
            Cursor = Cursors.SizeNESW,
            Focusable = true,
        };

        public Label LabelBotMid = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 5,
            Height = 5,
            Cursor = Cursors.SizeNS,
            Focusable = true,
        };

        public Label LabelBotRight = new Label()
        {
            Background = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 5,
            Height = 5,
            Cursor = Cursors.SizeNWSE,
            Focusable = true,
        };

        private Rect OfsetMove;

        //public Rect rect = new Rect()
        //{
        //    Location = new System.Windows.Point(5, 5),
        //    Size = new System.Windows.Size(100, 50)
        //};
        //public Rect Rect
        //{
        //    get { return rect; }
        //    set
        //    {
        //        if (rect != value)
        //        {
        //            if (value.X > 0 && value.X < ParentCanvasSize.Width - value.Width)
        //            {
        //                rect.X = value.X;
        //                rect.Width = value.Width;
        //                Label.Width = value.Width;
        //            }

        //            if (value.Y > 0 && value.Y < ParentCanvasSize.Height - value.Height)
        //            {
        //                rect.Y = value.Y;

        //                rect.Height = value.Height;

        //                Label.Height = value.Height;
        //            }
        //        }
        //    }
        //}

        public Rect rect = new Rect()
        {
            Location = new System.Windows.Point(5, 5),
            Size = new System.Windows.Size(100, 50)
        };

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

        // Move this ROI by (dx, dy). Bypasses the `Rect` property above on purpose: its bounds guard silently
        // DROPS a move once the ROI is near a canvas edge, which in a bulk shift would leave this ROI behind
        // while every other one moved. A shift must be all-or-nothing per click, not per ROI.
        public void Translate(double dx, double dy)
        {
            rect = new Rect(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
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
                    // Do NOT stamp the name ("LCD1") into the box - only the detected string is shown (see DetectedString).
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

        public LCD()
        {
            Label.ToolTip = null;   // no hover crop-preview tooltip (removed at user request)

            Label.GotKeyboardFocus += Label_GotKeyboardFocus;
            Label.LostKeyboardFocus += Label_LostKeyboardFocus;

            Label.KeyDown += Label_KeyDown;

            Label.MouseDown += Label_MouseDown;
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

            for (int j = 0; j < 10; j++)
            {
                for (int i = 1; i < 254; i++)
                {
                    Param.Add(new Models.SettingParram()
                    {
                        Threshold = i,
                        Blur = j,
                        IsPass = false
                    });
                }
            }
        }

        public LCD(int index)
        {
            rect = new Rect(660, 10 + 31 * index, 100, 30);
            Name = "LCD" + (index + 1);
            Label.ToolTip = null;   // no hover crop-preview tooltip (removed at user request)

            Label.GotKeyboardFocus += Label_GotKeyboardFocus;
            Label.LostKeyboardFocus += Label_LostKeyboardFocus;

            Label.KeyDown += Label_KeyDown;

            Label.MouseDown += Label_MouseDown;
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

            for (int i = 1; i < 254; i++)
            {
                Param.Add(new Models.SettingParram()
                {
                    Threshold = i,
                    IsPass = false
                });
            }
        }

        //Parent and positions

        public void SetParentCanvas(Canvas placeCanvas)
        {
            if (ParentCanvas != null)
            {
                ParentCanvas.Children.Remove(Label);
                ParentCanvas.Children.Remove(DetectCaption);
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

            // Detected-string caption sits on the box's top edge (just above rect.Y), left-aligned with the box.
            Canvas.SetTop(this.DetectCaption, rect.Y - this.DetectCaption.Height);
            Canvas.SetLeft(this.DetectCaption, rect.X);

            // Handles sit OUTSIDE the box, touching each rect edge/corner (like FND): corner handles meet the corner
            // point, edge handles sit against the far edge centred on it. Keep in sync with the SetPosition() block.
            const double H = 5;   // handle size (LabelTopLeft.Width/Height)
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

            //(Label.Parent as Canvas).Children.Clear();
            //(LabelTopLeft.Parent as Canvas).Children.Clear();
            //(LabelTopMid.Parent as Canvas).Children.Clear();
            //(LabelTopRight.Parent as Canvas).Children.Clear();
            //(LabelMidLeft.Parent as Canvas).Children.Clear();
            //(LabelMidRight.Parent as Canvas).Children.Clear();
            //(LabelBotLeft.Parent as Canvas).Children.Clear();
            //(LabelBotMid.Parent as Canvas).Children.Clear();
            //(LabelBotRight.Parent as Canvas).Children.Clear();

            placeCanvas.Children.Add(Label);
            placeCanvas.Children.Add(DetectCaption);

            placeCanvas.Children.Add(LabelTopLeft);
            placeCanvas.Children.Add(LabelTopMid);
            placeCanvas.Children.Add(LabelTopRight);

            placeCanvas.Children.Add(LabelMidLeft);
            placeCanvas.Children.Add(LabelMidRight);

            placeCanvas.Children.Add(LabelBotLeft);
            placeCanvas.Children.Add(LabelBotMid);
            placeCanvas.Children.Add(LabelBotRight);
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
                SetPosition();
            }
        }

        private void Label_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
                OfsetMove = new Rect()
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
                Rect areaRect = Rect;
                areaRect.X = e.GetPosition(ParentCanvas).X - OfsetMove.Width;
                areaRect.Y = e.GetPosition(ParentCanvas).Y - OfsetMove.Height;
                Rect = areaRect;
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

        public void SetPosition()
        {
            Canvas.SetTop(this.Label, rect.Y);
            Canvas.SetLeft(this.Label, rect.X);
            // Keep the box size in step with rect here too (a move/resize that bypasses the Rect setter left Label.Width
            // stale, drifting the right/bottom handles). Handles key off rect, sitting OUTSIDE and touching each edge.
            Label.Width = rect.Width;
            Label.Height = rect.Height;

            // Detected-string caption sits on the box's top edge (just above rect.Y), left-aligned with the box.
            Canvas.SetTop(this.DetectCaption, rect.Y - this.DetectCaption.Height);
            Canvas.SetLeft(this.DetectCaption, rect.X);

            const double H = 5;   // handle size (LabelTopLeft.Width/Height)
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
        }

        //Get and process image

        // Class level
        // Each OCR instance gets its OWN dedicated LongRunning worker thread, keyed by the OCR instance. This is
        // ESSENTIAL and is the ORIGINAL design: MKL-DNN predictors are thread-affine (the engine must be built AND run
        // on the same thread) AND cache conv-weight layouts by input shape. Running OCR on the VARYING ThreadPool timer
        // threads throws "Filter tensor's layout should be ONEDNN, but got NCHW". Pinning each per-page OCR to ONE
        // stable worker thread (which only ever sees that page's ROI = one shape) fixes both. Do NOT remove this.
        private static readonly ConcurrentDictionary<OCR, BlockingCollection<(Mat mat, Action<string> callback)>> _ocrQueues = new ConcurrentDictionary<OCR, BlockingCollection<(Mat mat, Action<string> callback)>>();

        private static BlockingCollection<(Mat mat, Action<string> callback)> GetOrCreateQueue(OCR ocr)
        {
            return _ocrQueues.GetOrAdd(ocr, key =>
            {
                var queue = new BlockingCollection<(Mat, Action<string>)>(boundedCapacity: 1);
                Task.Factory.StartNew(() =>
                {
                    foreach (var (mat, callback) in queue.GetConsumingEnumerable())
                    {
                        try
                        {
                            var result = DetectStringRegion(key, mat, out string data);
                            callback(result ? data : string.Empty);
                        }
                        finally
                        {
                            mat.Dispose();
                        }
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                return queue;
            });
        }

 
        public void TestImage(OCR ocr, Mat source)
        {
            if (source == null) {

                return;            
            }
            ;
            if (ParentCanvas == null) return;
            double scaleX = source.Width / ParentCanvasSize.Width;
            double scaleY = source.Height / ParentCanvasSize.Height;


            OpenCvSharp.Rect rect = new OpenCvSharp.Rect(
                       new OpenCvSharp.Point(this.Rect.X * scaleX, this.Rect.Y * scaleY),
                       new OpenCvSharp.Size(this.Rect.Width * scaleX, this.Rect.Height * scaleY));

            Mat croppedMat =  new Mat(source, rect);




            using (croppedMat)
            {
                // Hand the crop to THIS ocr's dedicated worker thread (GetOrCreateQueue) and let it set DetectedString
                // when done. Bounded to 1 so a slow OCR just drops intermediate live-preview frames.
                var matForOcr = croppedMat.Clone();
                var queue = GetOrCreateQueue(ocr);
                if (!queue.TryAdd((matForOcr, detectedString => DetectedString = detectedString)))
                {
                    matForOcr.Dispose();
                }

                var cropImage = VisionModel.MatToBitmapSource(croppedMat);

                _ = CropImageHolder.Dispatcher.BeginInvoke(new Action(() => CropImageHolder.Source = cropImage));
                CropImage = cropImage;
            }
        }

        //public async void AutoCalibration()
        //{
        //    if (mat == null || ParentCanvas == null)
        //    {
        //        turnningState = TurnningState.wait;
        //        IsTurning = false;
        //        return;
        //    }

        //    turnningState = TurnningState.turning;

        //    double scaleX = mat.Width / ParentCanvasSize.Width;
        //    double scaleY = mat.Height / ParentCanvasSize.Height;

        //    OpenCvSharp.Rect rect = new OpenCvSharp.Rect(
        //        new OpenCvSharp.Point(this.Rect.X * scaleX, this.Rect.Y * scaleY),
        //        new OpenCvSharp.Size(this.Rect.Width * scaleX, this.Rect.Height * scaleY));

        //    for (int i = 0; i < Param.Count; i++)
        //    {
        //        using (var croppedMat = new Mat(mat, rect))
        //        {
        //            TurningProgress = (i / (double)Param.Count * 100);
        //            var brightness = Param[i];
        //            //var processedBitmap = DetectString(croppedMat, (int)Threshold, Blur, out string data);
        //            var processedBitmap = DetectStringRegion(croppedMat, (int)brightness.Threshold, out string data, SpectString, NoiseSize);
        //            this.DetectedString = data;
        //            DetectedString = data;
        //            brightness.IsPass = IsPass;
        //            var cropImage = processedBitmap.ToBitmapSource();
        //            cropImage.Freeze();
        //            CropImageHolder.Source = cropImage;
        //            CropImage = cropImage;
        //            if (i % 10 == 0)
        //            {
        //                await Task.Delay(5);
        //            }
        //            else
        //            {
        //                await Task.Delay(TimeSpan.FromMilliseconds(1));
        //            }
        //            Param[i] = brightness;
        //        }
        //        TurningProgress = 100;
        //    }
        //    var bestSlution = Param.Where(o => o.IsPass).ToList();
        //    if (bestSlution.Count > 1)
        //    {
        //        var bestBrightness = bestSlution.GroupBy(o => o.Threshold).OrderByDescending(s => s.Count()).First().Key;
        //        var bestBlur = bestSlution.GroupBy(o => o.Blur).OrderByDescending(s => s.Count()).First().Key;
        //        var bestNoise = bestSlution.GroupBy(o => o.Noise).OrderByDescending(s => s.Count()).First().Key;
        //        Threshold = bestBrightness;
        //        Blur = bestBlur;
        //        NoiseSize = (int)bestNoise;
        //    }
        //    turnningState = TurnningState.wait;
        //    IsTurning = false;
        //}

        private static double map(float s, double a1, double a2, double b1, double b2)
        {
            return b1 + (s - a1) * (b2 - b1) / (a2 - a1);
        }

        public static bool Processing = false;
        //public static TesseractEngine engine = new TesseractEngine(@"./tessData", "eng", EngineMode.Default);

        //public static Mat DetectString(Mat source, double Threshold, double blur, out string str)
        //{
        //    engine.SetVariable("debug_file", "NUL");
        //    Processing = true;
        //    DateTime now = DateTime.Now;
        //    Mat mat = source.Clone();
        //    Bitmap output = mat.ToBitmap();
        //    var ocrtext = string.Empty;
        //    try
        //    {
        //        using (var img = PixConverter.ToPix(output))
        //        {
        //            using (var page = engine.Process(img))
        //            {
        //                ocrtext = page.GetText();
        //                var rects = page.GetSegmentedRegions(PageIteratorLevel.Block);
        //                foreach (var item in rects)
        //                {
        //                    mat.Rectangle(new OpenCvSharp.Rect(item.X, item.Y, item.Width, item.Height), new Scalar(0, 255, 0));
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception)
        //    {
        //    }
        //    str = ocrtext.Replace("\r", "").Replace("\n", "");
        //    Processing = false;
        //    return mat;
        //}
        public static bool DetectStringRegion(OCR ocr, Mat source, out string detectString)
        {
            detectString = string.Empty;
            if (source.Width == 0 || source.Height == 0)
            {

                return false;
            }


            Mat sourceToTest = source.Clone();

            try
            {
                PaddleOcrResult result = ocr.Run(sourceToTest);
                string text = result.Text;
                detectString = text.Replace("\r", "\n").Replace("\n", "");

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                detectString = string.Empty;
                sourceToTest?.Dispose();
                return false;
            }
            return true;



        }
    }
}
