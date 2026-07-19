using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using System;

namespace Controls.DevicesControl.VisionTest
{

    public class OCR : IDisposable
    {

        private PaddleOcrAll _ocr;

        public void Init()
        {
            if (_ocr == null)
                _ocr = BuildEngine();
        }

        public PaddleOcrResult Run(Mat src)
        {
            if (_ocr == null)
                _ocr = BuildEngine();
            Mat prepared = Prepare(src);   // may return src itself (no copy) - only dispose it if it's a new Mat
            try
            {
                try
                {
                    return _ocr.Run(prepared);
                }
                catch (Exception)
                {
                    // Safety net: MKL-DNN ALSO caches conv-weight layouts BY INPUT SHAPE, so a new ROI shape (window
                    // resized, model/ROI changed) on this thread's engine can throw the same "ONEDNN vs NCHW". A fresh
                    // engine on THIS thread always works, so rebuild + retry once.
                    try { _ocr?.Dispose(); } catch { }
                    _ocr = BuildEngine();
                    return _ocr.Run(prepared);
                }
            }
            finally { if (prepared != src) prepared.Dispose(); }
        }

        // Condition the ROI for PaddleOCR: normalise to BGR/8U (it expects that - EnsureBgr8U used to be dead code,
        // the raw frame went in as-is), then UPSCALE a small crop. The recogniser resizes text to ~48px tall, so a
        // tiny LCD/FND ROI arrives blurry and mis-reads; cubic-upscaling to ~48px (capped 3x) gives it real pixels
        // to read. Returns src unchanged when nothing is needed (caller must NOT dispose a returned == src).
        private static Mat Prepare(Mat src)
        {
            Mat bgr = EnsureBgr8U(src);
            const int targetH = 48;
            if (bgr.Height > 0 && bgr.Height < targetH)
            {
                double scale = Math.Min(3.0, (double)targetH / bgr.Height);
                Mat up = new Mat();
                Cv2.Resize(bgr, up, new OpenCvSharp.Size(), scale, scale, InterpolationFlags.Cubic);
                if (bgr != src) bgr.Dispose();
                return up;
            }
            return bgr;   // either src itself, or the freshly converted Mat from EnsureBgr8U
        }

        private static PaddleOcrAll BuildEngine()
        {
            return new PaddleOcrAll(LocalFullModels.EnglishV4, PaddleDevice.Mkldnn())
            {
                // LCD/FND text is horizontal and fixed-orientation, so don't let the detector hunt for rotated
                // boxes or flip 180 - that only adds mis-reads (and time) on a display that never rotates.
                AllowRotateDetection = false,
                Enable180Classification = false,
            };
        }

        private static Mat EnsureBgr8U(Mat src)
        {
            int channels = src.Channels();
            int depth = src.Depth(); // 0 = CV_8U

            // Already correct format
            if (channels == 3 && depth == 0)
                return src;

            Mat dst = new Mat();

            if (channels == 1)
                Cv2.CvtColor(src, dst, ColorConversionCodes.GRAY2BGR);
            else if (channels == 4)
                Cv2.CvtColor(src, dst, ColorConversionCodes.BGRA2BGR);
            else
                src.CopyTo(dst);

            // Convert depth to 8U if needed (e.g. 16U camera frames)
            if (dst.Depth() != 0)
            {
                Mat dst8u = new Mat();
                dst.ConvertTo(dst8u, MatType.CV_8UC3, 255.0 / 65535.0);
                dst.Dispose();
                return dst8u;
            }

            return dst;
        }

        public void Dispose()
        {
            // The engine is [ThreadStatic] (one per worker thread) and can only be disposed on its owning thread, so
            // there is nothing safe to release here; the OS reclaims them at process exit (pages live the whole session).
        }
    }
}
