using VTMControls.DeviceControl;
using System;
using System.Collections.Generic;
using VTMBase;

namespace VTMBase
{
    // Sound tester ported from ZEROC BuzzerCheck (D:\1ABC\TNG\#LATEST\ZEROC\HANSOL---ZEROC\ZeroC\BuzzerCheck.cs).
    // PURE DSP / TEST LOGIC: it does not touch the audio hardware. All WASAPI capture, device selection and the
    // sample buffer live in VTMControls.DeviceControl.Microphone (this.Mic); SoundTester only reads sample
    // snapshots from it and scores them.
    // Reads the model's global SoundStepConfig (ROIs, Metric, FFT size, MaxHz, dB range). Each SND CHECK step
    // verifies a subset of those ROIs (chosen via its Condition2 index list, passed into the Check overload).
    // Check() builds the spectrogram and slides over Cols=900, scores each ROI (Energy = mean magnitude,
    // Template = NCC), passes when some tau has all-ROI margin >= 0.
    public class SoundTester
    {
        public const int Bins = 512;
        public const int Cols = 900;
        public const int TplW = 64;
        public const int TplH = 48;
        private const int SlideStep = 1;

        // The capture device. Capture control (Start/Stop/MicrophoneId/IsCapturing/CaptureStarted...) lives here.
        public Microphone Mic { get; } = new Microphone();

        public bool HasResult { get; private set; }
        public bool LastPass { get; private set; }
        public string LastError { get; private set; }
        public string LastDetail { get; private set; }
        public float[][] LastCols { get; private set; }
        public int LastTau { get; private set; }
        public int ResultSeq { get; private set; }
        // Per-ROI result at LastTau (may be null if not checked yet).
        public bool[] LastRoiPass { get; private set; }
        public double[] LastRoiScore { get; private set; }

        public int CurrentSampleRate { get { return Mic.SampleRate; } }

        public SoundTester()
        {
            // A fresh capture clears the buffer; reset our live-spectrogram cursor to match so we don't index
            // past the (now empty) buffer. Mirrors the old Start() which reset both together.
            Mic.CaptureStarted += (s, e) =>
            {
                lock (_liveGate) { _liveCols.Clear(); _liveConsumed = 0; _liveFft = -1; }
            };
        }

        // Reset run - call before each test run to clear the buffer and the previous run's result state.
        public void ResetRun()
        {
            Mic.ClearBuffer();
            HasResult = false; LastError = null; LastDetail = "";
        }

        // Analyze the buffer with the step's SoundStepConfig.
        // Iterate over each ROI, mode Energy (mean magnitude vs Min/Max) or Template (NCC vs Tpl, >= Min).
        // Slide the recorded block over Cols=900 columns, pick the tau with the best "weakest margin".
        // Pass when a tau exists where every ROI has margin >= 0.
        // Check every ROI in the config.
        public virtual bool Check(SoundStepConfig config)
        {
            return Check(config, config?.Rois);
        }

        // Check only the given subset of ROIs (config still provides FFT/dB/MaxHz). Used by SND CHECK,
        // where the step's Condition2 selects which of the model's global ROIs to verify.
        public virtual bool Check(SoundStepConfig config, System.Collections.Generic.List<SoundRoi> rois)
        {
            HasResult = true; LastPass = false; LastCols = null; LastError = null;
            // Clear per-ROI results up front so an early return (bad config / missing template) leaves them
            // null instead of leaking the previous run's pass/fail onto ROIs this run never scored.
            LastRoiPass = null; LastRoiScore = null;

            if (config == null)
            {
                LastDetail = "No config"; LastError = "COMMAND ERR"; ResultSeq++; return false;
            }
            if (rois == null || rois.Count == 0)
            {
                LastDetail = "No ROI selected"; LastError = "COMMAND ERR"; ResultSeq++; return false;
            }
            // Template mode only: each ROI must have a captured Tpl.
            foreach (var r in rois)
            {
                if (r.Tpl == null || r.Tpl.Length != TplH * TplW)
                {
                    LastDetail = $"ROI '{r.Name}' has no template"; LastError = "COMMAND ERR"; ResultSeq++; return false;
                }
            }

            try
            {
                var cols = BuildColumns(Mic.ToArray(), config);
                LastCols = cols.ToArray();
                int r = cols.Count;
                LastTau = Math.Max(0, Cols - r);
                if (r == 0) { LastDetail = "No audio recorded"; LastError = "DEVICE ERR"; return false; }

                int tauLo = -(r - 1);
                int tauHi = Cols - 1;

                bool found = false;
                int bestPassTau = 0; double bestPassMargin = double.NegativeInfinity;
                int bestAnyTau = tauLo; double bestAnyMargin = double.NegativeInfinity;
                for (int tau = tauLo; tau <= tauHi; tau += SlideStep)
                {
                    bool all = true;
                    double worst = double.PositiveInfinity;
                    foreach (var d in rois)
                    {
                        double sc = EvalRoiAt(d, cols, r, tau);
                        double margin = (d.Tpl != null && d.Tpl.Length == TplH * TplW)
                            ? (sc - d.Min)
                            : Math.Min(sc - d.Min, d.Max - sc);
                        if (margin < worst) worst = margin;
                        if (margin < 0) all = false;
                    }
                    if (worst > bestAnyMargin) { bestAnyMargin = worst; bestAnyTau = tau; }
                    if (all && worst > bestPassMargin) { found = true; bestPassMargin = worst; bestPassTau = tau; }
                }
                LastPass = found;
                LastTau = found ? bestPassTau : bestAnyTau;
                LastDetail = found ? ("MATCH @" + LastTau) : "No match";

                // Per-ROI status at the chosen tau (so the UI colors each box green/red)
                LastRoiPass = new bool[rois.Count];
                LastRoiScore = new double[rois.Count];
                for (int i = 0; i < rois.Count; i++)
                {
                    var d = rois[i];
                    double sc = EvalRoiAt(d, cols, r, LastTau);
                    double margin = sc - d.Min;
                    LastRoiScore[i] = sc;
                    LastRoiPass[i] = margin >= 0;
                }
                return LastPass;
            }
            finally { ResultSeq++; }
        }

        // Public helpers for live rendering in SoundPage.
        public float[] SnapshotSamples(int maxTail)
        {
            return Mic.Tail(maxTail);
        }

        public List<float[]> BuildLiveColumns(float[] samples, SoundStepConfig cfg)
        {
            return BuildColumns(samples, cfg);
        }

        // ---- Incremental live columns (only FFT NEW columns per frame -> no lag) ----
        // Keeps a rolling buffer of up to Cols columns. Each poll only computes hops added since last call.
        private readonly object _liveGate = new object();
        private readonly List<float[]> _liveCols = new List<float[]>();
        private int _liveConsumed;   // samples already turned into columns
        private int _liveFft = -1;
        private double _liveMaxHz = -1, _liveDbFloor = double.NaN, _liveDbTop = double.NaN;

        public List<float[]> PollLiveColumns(SoundStepConfig cfg)
        {
            int fftN = cfg.FftSize >= 256 ? cfg.FftSize : 2048;
            int hop = fftN / 4;
            double maxHz = cfg.MaxHz > 100 ? cfg.MaxHz : 8000;
            double dbFloor = cfg.DbFloor;
            double dbTop = cfg.DbTop > cfg.DbFloor ? cfg.DbTop : cfg.DbFloor + 1;
            int sampleRate = Mic.SampleRate;

            lock (_liveGate)
            {
                int total = Mic.SampleCount;

                // Config changed -> rebuild from scratch (only keep the last Cols columns)
                if (fftN != _liveFft || maxHz != _liveMaxHz || dbFloor != _liveDbFloor || dbTop != _liveDbTop)
                {
                    _liveCols.Clear();
                    _liveFft = fftN; _liveMaxHz = maxHz; _liveDbFloor = dbFloor; _liveDbTop = dbTop;
                    // Start near the tail so we don't FFT the whole history
                    int window = Cols * hop + fftN;
                    _liveConsumed = Math.Max(0, total - window);
                    // Align _liveConsumed to a hop boundary from 0
                    _liveConsumed -= (_liveConsumed % hop);
                }

                var hann = _hannCache != null && _hannCache.Length == fftN ? _hannCache : (_hannCache = MakeHann(fftN));
                int ny = fftN / 2 - 1;
                var re = _reCache != null && _reCache.Length == fftN ? _reCache : (_reCache = new double[fftN]);
                var im = _imCache != null && _imCache.Length == fftN ? _imCache : (_imCache = new double[fftN]);

                // Pull only the samples we have not consumed yet (bounded per frame).
                float[] tail = Mic.ReadFrom(_liveConsumed, out total);
                int baseIdx = _liveConsumed;             // absolute index of tail[0]
                int off = 0;                             // offset into tail of the next FFT window
                while (baseIdx + off + fftN <= total)
                {
                    for (int i = 0; i < fftN; i++) { re[i] = tail[off + i] * hann[i]; im[i] = 0; }
                    Fft(re, im);
                    var v = new float[Bins];
                    for (int row = 0; row < Bins; row++)
                    {
                        double freq = maxHz * (double)(Bins - 1 - row) / (Bins - 1);
                        int b = (int)Math.Round(freq * fftN / (double)sampleRate);
                        if (b < 0) b = 0; else if (b > ny) b = ny;
                        double mag = 2.0 * Math.Sqrt(re[b] * re[b] + im[b] * im[b]) / fftN;
                        double db = 20.0 * Math.Log10(mag + 1e-9);
                        v[row] = (float)Clamp01((db - dbFloor) / (dbTop - dbFloor));
                    }
                    _liveCols.Add(v);
                    off += hop;
                }
                _liveConsumed = baseIdx + off;

                // Trim down to the last Cols columns
                if (_liveCols.Count > Cols) _liveCols.RemoveRange(0, _liveCols.Count - Cols);

                return new List<float[]>(_liveCols);
            }
        }

        private double[] _hannCache;
        private double[] _reCache, _imCache;
        private static double[] MakeHann(int n)
        {
            var h = new double[n];
            for (int i = 0; i < n; i++) h[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
            return h;
        }

        // ---- spectrogram builder ----
        private List<float[]> BuildColumns(float[] s, SoundStepConfig cfg)
        {
            int fftN = cfg.FftSize >= 256 ? cfg.FftSize : 2048;
            int hop = fftN / 4;
            double maxHz = cfg.MaxHz > 100 ? cfg.MaxHz : 8000;
            double dbFloor = cfg.DbFloor;
            double dbTop = cfg.DbTop > cfg.DbFloor ? cfg.DbTop : cfg.DbFloor + 1;
            int sampleRate = Mic.SampleRate;

            var cols = new List<float[]>();
            if (s == null || s.Length < fftN) return cols;
            var hann = new double[fftN];
            for (int i = 0; i < fftN; i++) hann[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftN - 1)));
            int ny = fftN / 2 - 1;
            for (int start = 0; start + fftN <= s.Length; start += hop)
            {
                var re = new double[fftN]; var im = new double[fftN];
                for (int i = 0; i < fftN; i++) { re[i] = s[start + i] * hann[i]; im[i] = 0; }
                Fft(re, im);
                var v = new float[Bins];
                for (int row = 0; row < Bins; row++)
                {
                    double freq = maxHz * (double)(Bins - 1 - row) / (Bins - 1);
                    int b = (int)Math.Round(freq * fftN / (double)sampleRate);
                    if (b < 0) b = 0; else if (b > ny) b = ny;
                    double mag = 2.0 * Math.Sqrt(re[b] * re[b] + im[b] * im[b]) / fftN;
                    double db = 20.0 * Math.Log10(mag + 1e-9);
                    v[row] = (float)Clamp01((db - dbFloor) / (dbTop - dbFloor));
                }
                cols.Add(v);
            }
            return cols;
        }

        private static float Value(List<float[]> cols, int r, int tau, int c, int row)
        {
            int ri = c - tau;
            if (ri < 0 || ri >= r) return 0f;
            var v = cols[ri];
            return (row >= 0 && row < v.Length) ? v[row] : 0f;
        }

        private static double EvalRoiAt(SoundRoi d, List<float[]> cols, int r, int tau)
        {
            double x0 = Math.Min(d.X0, d.X1), x1 = Math.Max(d.X0, d.X1);
            double y0 = Math.Min(d.Y0, d.Y1), y1 = Math.Max(d.Y0, d.Y1);
            int rowA = (int)(y0 * Bins), rowB = (int)(y1 * Bins);
            if (rowA < 0) rowA = 0;
            if (rowB > Bins) rowB = Bins;
            if (rowB <= rowA) rowB = Math.Min(Bins, rowA + 1);
            int c0 = (int)(x0 * Cols), c1 = (int)(x1 * Cols);
            if (c0 < 0) c0 = 0;
            if (c1 >= Cols) c1 = Cols - 1;
            if (c1 < c0) c1 = c0;

            if (d.Tpl != null && d.Tpl.Length == TplH * TplW)
            {
                int rows = rowB - rowA, wcols = c1 - c0 + 1;
                var patch = new float[TplH * TplW];
                for (int ty = 0; ty < TplH; ty++)
                {
                    int srr = rowA + (int)((ty + 0.5) / TplH * rows); if (srr >= Bins) srr = Bins - 1;
                    for (int tx = 0; tx < TplW; tx++)
                    {
                        int sc = c0 + (int)((tx + 0.5) / TplW * wcols); if (sc >= Cols) sc = Cols - 1;
                        patch[ty * TplW + tx] = Value(cols, r, tau, sc, srr);
                    }
                }
                return Ncc(patch, d.Tpl);
            }
            double sum = 0; int n = 0;
            for (int c = c0; c <= c1; c++)
                for (int row = rowA; row < rowB; row++) { sum += Value(cols, r, tau, c, row); n++; }
            return n > 0 ? sum / n : 0.0;
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        private static double Ncc(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length || a.Length == 0) return 0;
            int n = a.Length; double ma = 0, mb = 0;
            for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
            ma /= n; mb /= n;
            double num = 0, da = 0, db = 0;
            for (int i = 0; i < n; i++) { double x = a[i] - ma, y = b[i] - mb; num += x * y; da += x * x; db += y * y; }
            double den = Math.Sqrt(da * db);
            if (den <= 1e-9) return 0;
            double rr = num / den;
            return rr < 0 ? 0 : (rr > 1 ? 1 : rr);
        }

        // Cooley-Tukey iterative FFT in-place
        private static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                double wlenR = Math.Cos(ang), wlenI = Math.Sin(ang);
                int half = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    double wR = 1, wI = 0;
                    for (int k = 0; k < half; k++)
                    {
                        double vR = re[i + k + half] * wR - im[i + k + half] * wI;
                        double vI = re[i + k + half] * wI + im[i + k + half] * wR;
                        double uR = re[i + k], uI = im[i + k];
                        re[i + k] = uR + vR; im[i + k] = uI + vI;
                        re[i + k + half] = uR - vR; im[i + k + half] = uI - vI;
                        double nwR = wR * wlenR - wI * wlenI; wI = wR * wlenI + wI * wlenR; wR = nwR;
                    }
                }
            }
        }
    }
}
