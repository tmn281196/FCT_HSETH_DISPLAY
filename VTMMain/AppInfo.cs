using System;
using System.IO;
using System.Reflection;

namespace VTMMain
{
    // Global app metadata. Edit Version here to change it in one place, applied everywhere.
    public static class AppInfo
    {
        public static string Version = "2.8";
        public static string CompanyName = "T.N.G Tech";

        // Build timestamp = when the running .exe was produced (its file write time). No manual bump needed - it
        // updates every build. Empty if the location can't be read (e.g. single-file/embedded, designer host).
        public static DateTime BuildDate
        {
            get
            {
                try
                {
                    var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                    var path = asm != null ? asm.Location : null;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        return File.GetLastWriteTime(path);
                }
                catch { }
                return DateTime.MinValue;
            }
        }

        // Formatted build date/time for display (dd/MM/yyyy HH:mm:ss); empty string when unknown.
        public static string BuildDateString
        {
            get
            {
                var d = BuildDate;
                return d == DateTime.MinValue ? "" : d.ToString("dd/MM/yyyy HH:mm:ss");
            }
        }
    }
}
