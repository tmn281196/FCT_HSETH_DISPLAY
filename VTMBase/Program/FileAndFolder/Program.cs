using VTMBase;
using Utility;

namespace VTMBase
{
    public partial class Program
    {

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
    }
}
