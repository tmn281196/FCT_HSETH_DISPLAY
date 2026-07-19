using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VTMControls.DeviceControl
{
    // Microphone capture device (WASAPI via NAudio). Owns the audio sample buffer and hides ALL
    // NAudio / audio-hardware concerns so the sound tester can stay pure DSP / test logic.
    // Producer: OnData (WASAPI capture thread) appends float32 samples. Consumers (SoundTester)
    // read locked snapshots via ToArray / Tail / ReadFrom (or take SyncRoot directly).
    public class Microphone
    {
        private readonly object _gate = new object();
        private WasapiCapture _cap;
        private readonly List<float> _samples = new List<float>();
        private int _sampleRate = 48000;

        // Select microphone by endpoint ID. Empty -> default capture endpoint.
        public string MicrophoneId { get; set; } = "";

        public bool IsCapturing { get; private set; }
        public string LastError { get; private set; }
        public string LastDetail { get; private set; }
        public int SampleRate { get { return _sampleRate; } }

        // The lock guarding the sample buffer. DSP consumers take it while iterating a snapshot.
        public object SyncRoot { get { return _gate; } }
        public int SampleCount { get { lock (_gate) { return _samples.Count; } } }

        // Raised synchronously inside Start()/Stop() (SoundPage relies on the synchronous Start firing).
        public event EventHandler CaptureStarted;
        public event EventHandler CaptureStopped;

        public void Start()
        {
            lock (_gate)
            {
                if (IsCapturing) return;
                LastError = null; LastDetail = "";
                try
                {
                    var en = new MMDeviceEnumerator();
                    MMDevice dev = null;
                    if (!string.IsNullOrWhiteSpace(MicrophoneId))
                    {
                        try { dev = en.GetDevice(MicrophoneId); } catch { }
                    }
                    if (dev == null) dev = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                    if (dev == null) { LastDetail = "No mic"; LastError = "DEVICE ERR"; return; }
                    _cap = new WasapiCapture(dev);
                    _sampleRate = _cap.WaveFormat.SampleRate;
                    _samples.Clear();
                    _cap.DataAvailable += OnData;
                    _cap.StartRecording();
                    IsCapturing = true;
                }
                catch (Exception ex)
                {
                    LastDetail = "Rec start failed: " + ex.Message;
                    LastError = "DEVICE ERR";
                }
            }
            if (IsCapturing) CaptureStarted?.Invoke(this, EventArgs.Empty);
        }

        private void OnData(object s, WaveInEventArgs e)
        {
            var wf = _cap?.WaveFormat;
            if (wf == null) return;
            int ch = wf.Channels, bps = wf.BitsPerSample / 8, frame = bps * ch;
            if (frame <= 0) return;
            lock (_gate)
            {
                for (int i = 0; i + frame <= e.BytesRecorded; i += frame)
                {
                    float v;
                    if (wf.Encoding == WaveFormatEncoding.IeeeFloat && bps == 4) v = BitConverter.ToSingle(e.Buffer, i);
                    else if (bps == 2) v = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8)) / 32768f;
                    else v = 0;
                    _samples.Add(v);
                }
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (!IsCapturing) return;
                try { _cap.StopRecording(); } catch { }
                try { _cap.Dispose(); } catch { }
                _cap = null; IsCapturing = false;
            }
            CaptureStopped?.Invoke(this, EventArgs.Empty);
        }

        // Full locked copy of the captured buffer (used by the one-shot Check()).
        public float[] ToArray()
        {
            lock (_gate) { return _samples.ToArray(); }
        }

        // Locked copy of the last `maxTail` samples (used by the live waveform/spectrogram).
        public float[] Tail(int maxTail)
        {
            lock (_gate)
            {
                int n = _samples.Count;
                if (n == 0) return new float[0];
                int take = Math.Min(maxTail, n);
                int start = n - take;
                var arr = new float[take];
                for (int i = 0; i < take; i++) arr[i] = _samples[start + i];
                return arr;
            }
        }

        // Locked copy of samples from `start` to the current end, for incremental consumers. Reports the
        // total sample count at read time via `total` so the caller can advance its own consumed cursor.
        public float[] ReadFrom(int start, out int total)
        {
            lock (_gate)
            {
                total = _samples.Count;
                if (start < 0) start = 0;
                if (start >= total) return new float[0];
                int len = total - start;
                var arr = new float[len];
                for (int i = 0; i < len; i++) arr[i] = _samples[start + i];
                return arr;
            }
        }

        // List microphone endpoints (WASAPI capture, Active). Used by SettingPage.
        public static List<MicDeviceInfo> List()
        {
            var list = new List<MicDeviceInfo>();
            try
            {
                using (var en = new MMDeviceEnumerator())
                {
                    foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                        list.Add(new MicDeviceInfo { Id = d.ID, Name = d.FriendlyName });
                }
            }
            catch { }
            return list;
        }
    }

    public class MicDeviceInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public override string ToString() { return Name; }
    }
}
