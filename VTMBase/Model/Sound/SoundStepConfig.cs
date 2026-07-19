using Controls.DeviceControl;
using System.Collections.Generic;

namespace VTMBase
{
    // 1 ROI = 1 region (rectangle) in the spectrogram (0..1 relative).
    // If Metric = Template: Tpl holds a 48x64 patch (magnitude normalized 0..1).
    // If Metric = Energy: only compares average magnitude against Min/Max.
    public class SoundRoi
    {
        public string Name { get; set; } = "roi";
        public double X0 { get; set; } = 0.0;
        public double Y0 { get; set; } = 0.0;
        public double X1 { get; set; } = 1.0;
        public double Y1 { get; set; } = 1.0;
        public double Min { get; set; } = 0.0;
        public double Max { get; set; } = 1.0;

        // Template patch (used when Metric = Template). null if not captured yet.
        public float[] Tpl { get; set; }
        public int TplWidth { get; set; }
        public int TplHeight { get; set; }

        // Runtime scoring result (NOT serialized) - used for realtime UI coloring. Both the model save AND the
        // Revert snapshot now go through System.Text.Json (Utility.Extensions), so only its JsonIgnore is needed.
        // Without it, the last pass/fail gets written to the model and reloads pre-colored.
        [System.Text.Json.Serialization.JsonIgnore]
        public bool? LastPass { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public double? LastScore { get; set; }

        // Transient bench-tuning flag (NOT serialized): in CHECK mode the user left-clicks ROIs to
        // designate them (✓); the stop-on-pass flag freezes capture once all designated ROIs pass.
        [System.Text.Json.Serialization.JsonIgnore]
        public bool StopChecked { get; set; }
    }

    // Global sound configuration for the whole model (shared ROIs + FFT/dB/colormap).
    // Serialized inside the model; every SND CHECK step verifies these ROIs.
    public class SoundStepConfig
    {
        public int FftSize { get; set; } = 2048;
        public int SampleRate { get; set; } = 48000;
        public double MaxHz { get; set; } = 8000;
        public double DbFloor { get; set; } = -90;
        public double DbTop { get; set; } = -10;
        public string Metric { get; set; } = "Template";   // fixed to Template (Energy dropped)
        public bool StopOnPass { get; set; } = false;

        public List<SoundRoi> Rois { get; set; } = new List<SoundRoi>();
    }
}
