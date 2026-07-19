using DirectShowLib;
using VTMUtility;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
    /// Interaction logic for CameraControl.xaml
    /// </summary>
    public partial class CameraControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event EventHandler CamerasCollectionEmplty;

        private Task _previewTask;

        private CancellationTokenSource _cancellationTokenSource;

        // Sliding window: keep old frames alive so vision timers don't access disposed memory.
        // At 30ms/frame, 3 old frames = ~90ms lifetime before disposal.
        private Mat _prevFrame1, _prevFrame2;
        private Mat _LastMatFrame;

        public Mat LastMatFrame
        {
            get { return _LastMatFrame; }
            set
            {
                if (value != null || value != _LastMatFrame)
                    _LastMatFrame = value;
            }
        }

        private BitmapSource lastFrame;

        public BitmapSource LastFrame
        {
            get
            {
                return lastFrame;
            }
            set
            {
                lastFrame = value;
                NotifyPropertyChanged(nameof(LastFrame));
            }
        }

        public int CameraDeviceId { get; private set; }
        public byte[] LastPngFrame { get; private set; }

        public CameraSetting cameraSetting = new CameraSetting();

        public VideoCapture videoCapture = new VideoCapture();

        public enum VideoProperties
        {
            Exposure,
            Brightness,
            Contrast,
            Satuation,
            WhiteBalance,
            Sharpness,
            Focus,
            Zoom,
            Reset,
            Gain,
            Backlight,
        }

        public CameraControl()
        {
            InitializeComponent();
            this.DataContext = this;
            List<CameraDevice> cameras = new List<CameraDevice>();
            try
            {
                cameras = CameraDevicesEnumerator.GetAllConnectedCameras();
            }
            catch (Exception err)
            {
                Console.WriteLine(err.Message);
                return;
            }
            if (cameras.Count >= 1)
            {
                try
                {
                    var selectedCameraDeviceId = cameras[0].OpenCvId;
                    if (CameraDeviceId != selectedCameraDeviceId)
                    {
                        CameraDeviceId = cameras[0].OpenCvId;
                    }
                }
                catch (Exception e)
                {
                    Debug.Write("Camera : No camera detected, check your camera device and restart this software.", Debug.ContentType.Error, 20);
                }
            }
            else
            {
                CamerasCollectionEmplty?.Invoke(null, null);
                CameraDetail.Content = "No camera detectd !";
                Debug.Write("Camera : No camera detected, check your camera device and restart this software.", Debug.ContentType.Error, 20);
            }
        }

        public void START()
        {
            //try
            //{
            //    Task.Run(Start);
            //}
            //catch (Exception)
            //{
            //}
            _ = Start();
        }

        public async Task Start()
        {
            // Never run two parallel tasks for the webcam streaming
            if (_previewTask != null && !_previewTask.IsCompleted)
                return;

            //var initializationSemaphore = new SemaphoreSlim(0, 1);

            _cancellationTokenSource = new CancellationTokenSource();
            _previewTask = Task.Run(async () =>
            {
                try
                {
                    // Creation and disposal of this object should be done in the same thread
                    // because if not it throws disconnectedContext exception
                    videoCapture = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
                    try
                    {
                        videoCapture.FrameWidth = 1920;
                        videoCapture.FrameHeight = 1080;
                        //videoCapture.Fps = 50;

                        videoCapture.BufferSize = 1;
                        videoCapture.FourCC = "MJPG";
                        // Turn OFF auto white balance so a manual WhiteBalanceBlueU write actually sticks. With AutoWB
                        // on, the driver ignores the manual value and Get() reports the auto-computed one (e.g. set=617
                        // read back 300). A test rig wants a fixed WB from the model, not a scene-driven auto value.
                        videoCapture.Set(VideoCaptureProperties.AutoWB, 0);
                        //videoCapture.Set(VideoCaptureProperties.Settings, 1);

                        //videoCapture.AutoExposure = -1;

                        //videoCapture.AutoFocus = false;
                        //videoCapture.Focus = 10;
                        //videoCapture.Brightness = 3;
                        //videoCapture.Contrast = 172;
                        //videoCapture.Exposure = -5;
                        //videoCapture.Saturation = 129;
                        //videoCapture.Sharpness = 255;
                        //videoCapture.Zoom = 104;
                        //videoCapture.WhiteBalanceBlueU = 6000;

                        //Debug.Write(
                        //    "BRIGHTNESS : " + videoCapture.Brightness.ToString() +
                        //    "BACKLIGHT : " + videoCapture.BackLight.ToString() +
                        //    "CONTRAST : " + videoCapture.Contrast.ToString() +
                        //    "EXPOSURE : " + videoCapture.Exposure.ToString() +
                        //    "FOCUS : " + videoCapture.Focus.ToString() +
                        //    "SATURATION : " + videoCapture.Saturation.ToString() +
                        //    "SHARPNESS : " + videoCapture.Sharpness.ToString() +
                        //    "wihe : " + videoCapture.WhiteBalanceBlueU.ToString() +
                        //    "ZOOM : " + videoCapture.Zoom.ToString(), Debug.ContentType.Notify);

                        // Do NOT read the camera into cameraSetting at startup - settings come from the MODEL (loaded
                        // into the sliders on model load), not from whatever the camera happens to power up with.
                        //GetCameraProperties();
                    }
                    catch (Exception err)
                    {
                        string defaltFrame = videoCapture.Get(VideoCaptureProperties.FrameWidth) + " x " + videoCapture.Get(VideoCaptureProperties.FrameHeight);
                        MessageBox.Show("Camera start error!" + err.Message + "\n" + err.StackTrace + "\n Defaul resolution: " + defaltFrame, "Camera start error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    //GetCameraProperties();

                    while (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        // Apply any queued camera-property changes here, on THIS (capture) thread. Setting them from
                        // the UI thread races RetrieveMat below and the change is silently dropped.
                        if (!_pendingCamSets.IsEmpty)
                        {
                            foreach (var key in _pendingCamSets.Keys.ToList())
                            {
                                if (_pendingCamSets.TryRemove(key, out int v))
                                {
                                    // Skip the (slow) DirectShow Set() when the value hasn't changed since we last wrote
                                    // it. Each Set() is a blocking driver round-trip on this capture thread, so re-applying
                                    // the same model on every page-switch froze the preview.
                                    if (_lastWritten.TryGetValue(key, out int last) && last == v)
                                        continue;
                                    SetParammeter(key, v, true);
                                    _lastWritten[key] = v;
                                    // Readback ONLY for White Balance (the property under investigation) - a Get() per
                                    // property is another slow round-trip; doing it for all 10 doubled the apply time.
                                    if (key == VideoProperties.WhiteBalance)
                                    {
                                        int readback = GetParammeter(key);
                                        VTMUtility.Debug.Write("CAMERA: WhiteBalance set=" + v + " readback=" + readback,
                                            VTMUtility.Debug.ContentType.Notify);
                                    }
                                }
                            }
                        }

                        using (Mat frame = videoCapture.RetrieveMat())
                        {
                            if (frame != null && !frame.Empty())
                            {
                                // Dispose frame from 3 cycles ago (~90ms old).
                                // Current consumers (FND 100ms, LCD 500ms timers) finish
                                // reading .Width/.Height well within that window.
                                _prevFrame2?.Dispose();
                                _prevFrame2 = _prevFrame1;
                                _prevFrame1 = _LastMatFrame;
                                _LastMatFrame = frame.Clone();

                                var bi = frame.ToBitmapSource();
                                bi.Freeze();
                                LastFrame = bi;
                            }
                            videoCapture.Grab();
                        }
                        await Task.Delay(30);
                    }

                    videoCapture?.Dispose();
                }
                finally
                {
                    //if (initializationSemaphore != null)
                    //    initializationSemaphore.Release();
                }
            }, _cancellationTokenSource.Token);

            // Async initialization to have the possibility to show an animated loader without freezing the GUI
            // The alternative was the long polling. (while !variable) await Task.Delay
            //await initializationSemaphore.WaitAsync();
            //initializationSemaphore.Dispose();
            //initializationSemaphore = null;

            if (_previewTask.IsFaulted)
            {
                // To let the exceptions exit
                await _previewTask;
            }
        }

        public async Task Stop()
        {
            // If "Dispose" gets called before Stop
            if (_cancellationTokenSource.IsCancellationRequested)
                return;

            if (!_previewTask.IsCompleted)
            {
                _cancellationTokenSource.Cancel();

                // Wait for it, to avoid conflicts with read/write of _lastFrame
                await _previewTask;
            }
        }

        // Latest pending value per camera property. The camera Set() must run on the CAPTURE thread (the streaming
        // loop) because OpenCv VideoCapture is not thread-safe: a Set() from the UI thread races RetrieveMat and is
        // silently dropped - which is exactly why dragging changed nothing even though the log showed CAPTURE=True,
        // vc=True. The dictionary coalesces a fast drag to the newest value per property.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<VideoProperties, int> _pendingCamSets
            = new System.Collections.Concurrent.ConcurrentDictionary<VideoProperties, int>();

        // Last value actually written to the camera per property (capture thread only - no locking needed). Lets the
        // drain SKIP an unchanged Set(), so re-applying the same model on every page-switch doesn't re-do 10 slow
        // DirectShow round-trips and freeze the preview.
        private readonly System.Collections.Generic.Dictionary<VideoProperties, int> _lastWritten
            = new System.Collections.Generic.Dictionary<VideoProperties, int>();

        public void CameraSetting_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Dragging only updates the model-side value (and the on-screen number). It is NOT pushed to the camera
            // live - the operator adjusts freely, then clicks "Apply manual" (ApplyCurrentToCamera) to send them all.
            var slider = sender as Slider;
            if (slider == null) return;
            int val = (int)e.NewValue;
            switch (slider.Name)
            {
                case "slExporsure": cameraSetting.Exposure = val; break;
                case "slBrightness": cameraSetting.Brightness = val; break;
                case "slContrast": cameraSetting.Contrast = val; break;
                case "slFocus": cameraSetting.Focus = val; break;
                case "slWhite": cameraSetting.WBTemperature = val; break;
                case "slSharpness": cameraSetting.Sharpness = val; break;
                case "slZoom": cameraSetting.Zoom = val; break;
                case "slSatuation": cameraSetting.Saturation = val; break;
                case "slGain": cameraSetting.Gain = val; break;
                case "slBacklight": cameraSetting.Backlight = val; break;
                default: break;
            }
        }

        // Queue ALL current settings for the capture thread to apply (thread-safe - see the streaming loop). Used by
        // the "Apply manual" button: the operator sets the sliders, then pushes them to the camera in one go.
        public void ApplyCurrentToCamera()
        {
            _pendingCamSets[VideoProperties.Exposure] = cameraSetting.Exposure;
            _pendingCamSets[VideoProperties.Brightness] = cameraSetting.Brightness;
            _pendingCamSets[VideoProperties.Contrast] = cameraSetting.Contrast;
            _pendingCamSets[VideoProperties.Focus] = cameraSetting.Focus;
            _pendingCamSets[VideoProperties.WhiteBalance] = cameraSetting.WBTemperature;
            _pendingCamSets[VideoProperties.Sharpness] = cameraSetting.Sharpness;
            _pendingCamSets[VideoProperties.Zoom] = cameraSetting.Zoom;
            _pendingCamSets[VideoProperties.Satuation] = cameraSetting.Saturation;
            _pendingCamSets[VideoProperties.Gain] = cameraSetting.Gain;
            _pendingCamSets[VideoProperties.Backlight] = cameraSetting.Backlight;
        }

        // Write a GIVEN settings object (e.g. the model's) to the live camera via the capture-thread queue. Used at
        // test start so the run always uses the model's camera settings, regardless of what's on the sliders.
        public void WriteSettingsToCamera(CameraSetting src)
        {
            if (src == null) return;
            _pendingCamSets[VideoProperties.Exposure] = src.Exposure;
            _pendingCamSets[VideoProperties.Brightness] = src.Brightness;
            _pendingCamSets[VideoProperties.Contrast] = src.Contrast;
            _pendingCamSets[VideoProperties.Focus] = src.Focus;
            _pendingCamSets[VideoProperties.WhiteBalance] = src.WBTemperature;
            _pendingCamSets[VideoProperties.Sharpness] = src.Sharpness;
            _pendingCamSets[VideoProperties.Zoom] = src.Zoom;
            _pendingCamSets[VideoProperties.Satuation] = src.Saturation;
            _pendingCamSets[VideoProperties.Gain] = src.Gain;
            _pendingCamSets[VideoProperties.Backlight] = src.Backlight;
        }

        public void SetParammeter(VideoProperties properties, int Value, bool InTest)
        {
            if (videoCapture == null)
            {
                return;
            }
            switch (properties)
            {
                case VideoProperties.Exposure:
                    videoCapture.Set(VideoCaptureProperties.Exposure, Value);
                    break;

                case VideoProperties.Brightness:
                    videoCapture.Set(VideoCaptureProperties.Brightness, Value);
                    break;

                case VideoProperties.Contrast:
                    videoCapture.Set(VideoCaptureProperties.Contrast, Value);
                    break;

                case VideoProperties.Satuation:
                    videoCapture.Set(VideoCaptureProperties.Saturation, Value);
                    break;

                case VideoProperties.WhiteBalance:
                    //videoCapture.Set(VideoCaptureProperties.WhiteBalanceBlueU, Value);
                    videoCapture.Set(VideoCaptureProperties.WhiteBalanceBlueU, Value);
                    break;

                case VideoProperties.Sharpness:
                    videoCapture.Set(VideoCaptureProperties.Sharpness, Value);
                    break;

                case VideoProperties.Focus:
                    videoCapture.Set(VideoCaptureProperties.Focus, Value);
                    break;

                case VideoProperties.Zoom:
                    videoCapture.Set(VideoCaptureProperties.Zoom, Value);
                    break;

                case VideoProperties.Gain:
                    videoCapture.Set(VideoCaptureProperties.Gain, Value);
                    break;

                case VideoProperties.Backlight:
                    videoCapture.Set(VideoCaptureProperties.BackLight, Value);
                    break;

                default:
                    break;
            }
        }

        public void SetParammeter(VideoProperties properties, int Value)
        {
            if (videoCapture == null)
            {
                return;
            }
            switch (properties)
            {
                case VideoProperties.Exposure:
                    videoCapture.Set(VideoCaptureProperties.Exposure, Value);
                    cameraSetting.Exposure = Value;
                    break;

                case VideoProperties.Brightness:
                    videoCapture.Set(VideoCaptureProperties.Brightness, Value);
                    cameraSetting.Brightness = Value;
                    break;

                case VideoProperties.Contrast:
                    videoCapture.Set(VideoCaptureProperties.Contrast, Value);
                    cameraSetting.Contrast = Value;
                    break;

                case VideoProperties.Satuation:
                    videoCapture.Set(VideoCaptureProperties.Saturation, Value);
                    cameraSetting.Saturation = Value;
                    break;

                case VideoProperties.WhiteBalance:
                    videoCapture.Set(VideoCaptureProperties.WhiteBalanceBlueU, Value);
                    cameraSetting.WBTemperature = Value;
                    break;

                case VideoProperties.Sharpness:
                    videoCapture.Set(VideoCaptureProperties.Sharpness, Value);
                    cameraSetting.Sharpness = Value;
                    break;

                case VideoProperties.Focus:
                    videoCapture.Set(VideoCaptureProperties.Focus, Value);
                    cameraSetting.Focus = Value;
                    break;

                case VideoProperties.Zoom:
                    videoCapture.Set(VideoCaptureProperties.Zoom, Value);
                    cameraSetting.Zoom = Value;
                    break;

                case VideoProperties.Gain:
                    videoCapture.Set(VideoCaptureProperties.Gain, Value);
                    cameraSetting.Gain = Value;
                    break;

                case VideoProperties.Backlight:
                    videoCapture.Set(VideoCaptureProperties.BackLight, Value);
                    cameraSetting.Backlight = Value;
                    break;

                default:
                    break;
            }
        }

        public int GetParammeter(VideoProperties properties)
        {
            if (videoCapture == null)
            {
                return 0;
            }
            switch (properties)
            {
                case VideoProperties.Exposure:
                    return (int)videoCapture.Exposure;

                case VideoProperties.Brightness:
                    return (int)videoCapture.Brightness;

                case VideoProperties.Contrast:
                    return (int)videoCapture.Contrast;

                case VideoProperties.Satuation:
                    return (int)videoCapture.Saturation;

                case VideoProperties.WhiteBalance:
                    // Read the SAME property the slider/queue writes (WBTemperature), not WhiteBalanceBlueU - otherwise
                    // "write setting" and "read setting" disagree (they were two different camera controls).
                    return (int)videoCapture.Get(VideoCaptureProperties.WhiteBalanceBlueU);

                case VideoProperties.Sharpness:
                    return (int)videoCapture.Sharpness;

                case VideoProperties.Focus:
                    return (int)videoCapture.Focus;

                case VideoProperties.Zoom:
                    return (int)videoCapture.Zoom;

                case VideoProperties.Gain:
                    return (int)videoCapture.Gain;

                case VideoProperties.Backlight:
                    return (int)videoCapture.Get(VideoCaptureProperties.BackLight);

                default:
                    return 0;
            }
        }

        public bool SetParammeter(CameraSetting cameraSetting)
        {
            if (cameraSetting != null)
            {
                videoCapture.Exposure = cameraSetting.Exposure;

                videoCapture.Brightness = cameraSetting.Brightness;

                videoCapture.Contrast = cameraSetting.Contrast;

                videoCapture.Saturation = cameraSetting.Saturation;

                videoCapture.Set(VideoCaptureProperties.WhiteBalanceBlueU, cameraSetting.WBTemperature);   // match the slider/read path

                videoCapture.Sharpness = cameraSetting.Sharpness;

                videoCapture.Focus = cameraSetting.Focus;

                videoCapture.Zoom = cameraSetting.Zoom;

                videoCapture.Gain = cameraSetting.Gain;

                videoCapture.Set(VideoCaptureProperties.BackLight, cameraSetting.Backlight);
            }
            return true;
        }

        public CameraSetting GetParammeter()
        {
            return cameraSetting;
        }

        private void GetCameraProperties()
        {
            cameraSetting.Exposure = (int)videoCapture.Exposure;

            cameraSetting.Brightness = (int)videoCapture.Brightness;

            cameraSetting.Contrast = (int)videoCapture.Contrast;

            cameraSetting.Saturation = (int)videoCapture.Saturation;

            cameraSetting.WBTemperature = (int)videoCapture.Get(VideoCaptureProperties.WhiteBalanceBlueU);

            cameraSetting.Sharpness = (int)videoCapture.Sharpness;

            cameraSetting.Focus = (int)videoCapture.Focus;

            cameraSetting.Zoom = (int)videoCapture.Zoom;

            cameraSetting.Gain = (int)videoCapture.Gain;
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _prevFrame2?.Dispose();
            _prevFrame1?.Dispose();
            _LastMatFrame?.Dispose();
            _prevFrame2 = null;
            _prevFrame1 = null;
            _LastMatFrame = null;
        }
    }

    public class CameraDevice
    {
        public int OpenCvId { get; set; }
        public string Name { get; set; }
        public string DeviceId { get; set; }
    }

    public class CameraSetting : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private int _brightness = 0;

        public int Brightness
        {
            get { return _brightness; }
            set
            {
                if (_brightness != value)
                {
                    _brightness = value;
                    NotifyPropertyChanged(nameof(Brightness));
                }
            }
        }

        private int _contrast = 0;

        public int Contrast
        {
            get { return _contrast; }
            set
            {
                if (_contrast != value)
                {
                    _contrast = value;
                    NotifyPropertyChanged(nameof(Contrast));
                }
            }
        }

        private int _saturation = 0;

        public int Saturation
        {
            get { return _saturation; }
            set
            {
                if (_saturation != value)
                {
                    _saturation = value;
                    NotifyPropertyChanged(nameof(Saturation));
                }
            }
        }

        private int _exposure = 0;

        public int Exposure
        {
            get { return _exposure; }
            set
            {
                if (_exposure != value)
                {
                    _exposure = value;
                    NotifyPropertyChanged(nameof(Exposure));
                }
            }
        }

        private int _zoom = 0;

        public int Zoom
        {
            get { return _zoom; }
            set
            {
                if (_zoom != value)
                {
                    _zoom = value;
                    NotifyPropertyChanged(nameof(Zoom));
                }
            }
        }

        private int _backlight = 0;

        public int Backlight
        {
            get { return _backlight; }
            set
            {
                if (_backlight != value)
                {
                    _backlight = value;
                    NotifyPropertyChanged(nameof(Backlight));
                }
            }
        }

        private int _focus = 0;

        public int Focus
        {
            get { return _focus; }
            set
            {
                if (_focus != value)
                {
                    _focus = value;
                    NotifyPropertyChanged(nameof(Focus));
                }
            }
        }

        private int _sharpness = 0;

        public int Sharpness
        {
            get { return _sharpness; }
            set
            {
                if (_sharpness != value)
                {
                    _sharpness = value;
                    NotifyPropertyChanged(nameof(Sharpness));
                }
            }
        }

        private int _wbTemperature = 0;

        public int WBTemperature
        {
            get { return _wbTemperature; }
            set
            {
                if (_wbTemperature != value)
                {
                    _wbTemperature = value;
                    NotifyPropertyChanged(nameof(WBTemperature));
                }
            }
        }

        private int _gain = 0;

        public int Gain
        {
            get { return _gain; }
            set
            {
                if (_gain != value)
                {
                    _gain = value;
                    NotifyPropertyChanged(nameof(Gain));
                }
            }
        }
    }

    public static class CameraDevicesEnumerator
    {
        public static List<CameraDevice> GetAllConnectedCameras()
        {
            var cameras = new List<CameraDevice>();
            var videoInputDevices = DsDevice.GetDevicesOfCat(DirectShowLib.FilterCategory.VideoInputDevice);

            int openCvId = 0;
            return videoInputDevices.Select(v => new CameraDevice()
            {
                DeviceId = v.DevicePath,
                Name = v.Name,
                OpenCvId = openCvId++
            }).ToList();
        }
    }
}
