using VTMBase;
using Controls.DeviceControl;
using Utility;

namespace VTMBase
{
    public partial class Program
    {
        public StepViewer StepViewer = new StepViewer();
        public async void Machine_Init()
        {
            CreatAppFolder();
            LoadAppSetting();
            CheckComnunication();
            EscapTimer.Elapsed += EscapTimer_Elapsed;
            EscapTimer.Stop();
            await global::System.Threading.Tasks.Task.Delay(50);
        }
    }
}
