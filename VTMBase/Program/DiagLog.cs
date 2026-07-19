using VTMControls.DeviceControl;
using System;
using System.IO;

namespace VTMBase
{
    public static class DiagLog
    {
        private static readonly object _lock = new object();
        private static readonly string _baseDir = @"C:\log";

        public static void Write(string category, string message)
        {
            try
            {
                lock (_lock)
                {
                    if (!Directory.Exists(_baseDir))
                        Directory.CreateDirectory(_baseDir);

                    string filePath = Path.Combine(_baseDir, $"diag_{DateTime.Now:yyyy-MM-dd}.txt");
                    string line = $"{DateTime.Now:HH:mm:ss.fff} [{category}] {message}";
                    File.AppendAllText(filePath, line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
