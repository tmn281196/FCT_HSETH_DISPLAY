namespace VTMProgram
{
    public class AppSettingParam
    {
        public Operations Operations { get; set; } = new Operations();
        public Communication Communication { get; set; } = new Communication();
        public ETCSetting ETCSetting { get; set; } = new ETCSetting();
        public SystemAccess SystemAccess { get; set; } = new SystemAccess();

        // Path of the most recently loaded model. App auto-reloads it at startup if the file still exists.
        public string LastModelPath { get; set; } = "";

        // Spectrogram colormap - global for the whole SoundPage, not saved per-step.
        public string SpectrogramColorMap { get; set; } = "Hot";
    }
}
