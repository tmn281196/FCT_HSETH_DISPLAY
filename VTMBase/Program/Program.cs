using VTMBase;
using VTMControls.DeviceControl;
using VTMUtility;
using System;

namespace VTMBase
{
    // Small cross-cutting pieces of the Program partial class: startup, app folders + settings, and the
    // edit-model holder. Merged here from the former FileManagement\Program.cs and ModelBuilder\Program.cs,
    // which were 24- and 36-line files. The big concern-specific pieces stay in their own files:
    // Devices\Program.cs (device wiring), Boards\Program.cs (boards + barcode), ModelTester\Program.cs (state machine).
    public partial class Program
    {
        // ---- Startup ----

        public StepViewer StepViewer = new StepViewer();

        public async void Machine_Init()
        {
            CreatAppFolder();
            LoadAppSetting();
            CheckComnunication();
            EscapTimer.Elapsed += EscapTimer_Elapsed;
            EscapTimer.Stop();
            // global:: is required: the System device property (proxy to Devices.System) shadows the System
            // namespace inside this class, so a bare System.Threading.Tasks.Task would not resolve.
            await global::System.Threading.Tasks.Task.Delay(50);
        }

        // ---- App folders + settings (was FileManagement\Program.cs) ----

        public FolderMap FolderMap = new FolderMap();

        public void CreatAppFolder()
        {
            FolderMap.TryCreatFolderMap();
        }

        public AppSettingParam appSetting = new AppSettingParam();

        public void LoadAppSetting()
        {
            appSetting = Extensions.OpenFromFile<AppSettingParam>("Config.cfg");
            appSetting = appSetting ?? new AppSettingParam();
        }

        // ---- Edit model (was ModelBuilder\Program.cs) ----

        public event EventHandler EditModel_OnSave;

        public void OnEditModelSave()
        {
            EditModel_OnSave?.Invoke(EditModel, null);
        }

        public event EventHandler EditModel_OnLoaded;

        public void OnEditModelLoaded()
        {
            EditModel_OnLoaded?.Invoke(EditModel, null);
        }

        private Model editModel = new Model();

        public Model EditModel
        {
            get { return editModel; }
            set
            {
                if (value != editModel)
                {
                    editModel = value;
                }
            }
        }
    }
}
