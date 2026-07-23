using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    /// Interaction logic for CameraSettingPanel.xaml
    /// </summary>
    public partial class CameraSettingPanel : UserControl
    {
        private CameraControl _Capture;

        public CameraControl Capture
        {
            get { return _Capture; }
            set
            {
                if (value != null || value != _Capture) _Capture = value;
                this.DataContext = Capture.cameraSetting;
            }
        }

        private bool _AllowControl = true;

        public bool AllowControl
        {
            get { return _AllowControl; }
            set
            {
                if (value != _AllowControl) _AllowControl = value;
            }
        }

        public CameraSettingPanel()
        {
            InitializeComponent();
            //this.DataContext = Capture.cameraSetting;
            settingButton.Click += SettingButton_Click;
        }

        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            Capture.videoCapture.Set(VideoCaptureProperties.Settings, 1);
        }

        private void CameraSetting_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Only updates the model value + the on-screen number; the camera is pushed on "Apply manual".
            Capture?.CameraSetting_ValueChanged(sender, e);
        }

        // "Apply manual" button target: push all current slider values to the camera.
        public void ApplyManual()
        {
            Capture?.ApplyCurrentToCamera();
            VTMUtility.Debug.Write("CAMERA: settings applied to camera", VTMUtility.Debug.ContentType.Notify);
        }

        public CameraSetting GetParammeter()
        {
            GetCameraProperties();

            return Capture?.cameraSetting;
        }

        public void GetcameraSettingValue()
        {
            if (Capture?.cameraSetting != null)
            {
                GetCameraProperties();
                slExporsure.Value = Capture.cameraSetting.Exposure;
                slBrightness.Value = Capture.cameraSetting.Brightness;
                slContrast.Value = Capture.cameraSetting.Contrast;
                slFocus.Value = Capture.cameraSetting.Focus;
                slWhite.Value = Capture.cameraSetting.WBTemperature;
                slSharpness.Value = Capture.cameraSetting.Sharpness;
                slZoom.Value = Capture.cameraSetting.Zoom;
                slSatuation.Value = Capture.cameraSetting.Saturation;
                slGain.Value = Capture.cameraSetting.Gain;
                slBacklight.Value = Capture.cameraSetting.Backlight;
                SyncSettingFromSliders();   // camera may report out-of-range (e.g. WB = -1); keep cameraSetting = clamped slider
            }
        }

        // Load a model's saved camera settings into the panel. Sliders are set DIRECTLY from the model (not by
        // reading the camera back - that round-trip is racy while the camera streams, which made a just-saved model
        // look like it "didn't save" on reopen). The values are pushed to the live camera through the capture-thread
        // queue (reliable), same path as "Write Setting to Camera".
        public void ApplyModelSettings(CameraSetting src)
        {
            if (Capture?.cameraSetting == null || src == null) return;
            var dst = Capture.cameraSetting;
            dst.Exposure = src.Exposure;
            dst.Brightness = src.Brightness;
            dst.Contrast = src.Contrast;
            dst.Saturation = src.Saturation;
            dst.WBTemperature = src.WBTemperature;
            dst.Sharpness = src.Sharpness;
            dst.Focus = src.Focus;
            dst.Zoom = src.Zoom;
            dst.Gain = src.Gain;
            dst.Backlight = src.Backlight;
            slExporsure.Value = dst.Exposure;
            slBrightness.Value = dst.Brightness;
            slContrast.Value = dst.Contrast;
            slFocus.Value = dst.Focus;
            slWhite.Value = dst.WBTemperature;
            slSharpness.Value = dst.Sharpness;
            slZoom.Value = dst.Zoom;
            slSatuation.Value = dst.Saturation;
            slGain.Value = dst.Gain;
            slBacklight.Value = dst.Backlight;
            // Mirror the CLAMPED slider values back into cameraSetting so it never keeps an out-of-range value: a stale
            // WBTemperature of -1 clamps to the White slider's minimum, otherwise it round-trips to the model as -1
            // even though the slider shows the clamped value.
            SyncSettingFromSliders();
            // Push the model's settings to the live camera - the camera is configured ONCE, on model load. Page
            // switches no longer re-write it; the manual "Write Setting to Camera" button still can. (Delta-skip in
            // the capture-thread drain means the 3 panels sharing one camera only apply the values once.)
            Capture.ApplyCurrentToCamera();
        }

        // Copy the (clamped) slider values into cameraSetting. Each slider enforces its property's Min/Max, so this is
        // the clamp that keeps the model-side value in range and identical to what the operator sees on the slider.
        private void SyncSettingFromSliders()
        {
            if (Capture?.cameraSetting == null) return;
            var dst = Capture.cameraSetting;
            dst.Exposure = (int)slExporsure.Value;
            dst.Brightness = (int)slBrightness.Value;
            dst.Contrast = (int)slContrast.Value;
            dst.Focus = (int)slFocus.Value;
            dst.WBTemperature = (int)slWhite.Value;
            dst.Sharpness = (int)slSharpness.Value;
            dst.Zoom = (int)slZoom.Value;
            dst.Saturation = (int)slSatuation.Value;
            dst.Gain = (int)slGain.Value;
            dst.Backlight = (int)slBacklight.Value;
        }

        //public bool SetParammeter(CameraSetting cameraSetting)
        //{
        //    if (cameraSetting != null)
        //    {
        //        slExporsure.Value = cameraSetting.Exposure;
        //        slBrightness.Value = cameraSetting.Brightness;
        //        slContrast.Value = cameraSetting.Contrast;
        //        slFocus.Value = cameraSetting.Saturation;
        //        slWhite.Value = cameraSetting.WBTemperature;
        //        slSharpness.Value = cameraSetting.Sharpness;
        //        slZoom.Value = cameraSetting.Zoom;

        //    }
        //    return true;
        //}

        private void GetCameraProperties()
        {
            Capture.cameraSetting.Exposure = (int)Capture.videoCapture.Get(VideoCaptureProperties.Exposure);

            Capture.cameraSetting.Brightness = (int)Capture.videoCapture.Get(VideoCaptureProperties.Brightness);

            Capture.cameraSetting.Contrast = (int)Capture.videoCapture.Get(VideoCaptureProperties.Contrast);

            Capture.cameraSetting.Saturation = (int)Capture.videoCapture.Get(VideoCaptureProperties.Saturation);

            // WBTemperature (Kelvin), NOT WhiteBalanceBlueU (blue-channel gain, device units).
            Capture.cameraSetting.WBTemperature = (int)Capture.videoCapture.Get(VideoCaptureProperties.WBTemperature);

            Capture.cameraSetting.Sharpness = (int)Capture.videoCapture.Get(VideoCaptureProperties.Sharpness);

            Capture.cameraSetting.Focus = (int)Capture.videoCapture.Get(VideoCaptureProperties.Focus);

            Capture.cameraSetting.Zoom = (int)Capture.videoCapture.Get(VideoCaptureProperties.Zoom);

            Capture.cameraSetting.Gain = (int)Capture.videoCapture.Get(VideoCaptureProperties.Gain);

            Capture.cameraSetting.Backlight = (int)Capture.videoCapture.Get(VideoCaptureProperties.BackLight);
        }
    }
}
