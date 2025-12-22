using System;
using System.IO;

namespace AICAD.Services
{
    /// <summary>
    /// Simple local logger that writes to a file inside the project folder so the user can open it easily.
    /// </summary>
    public static class LocalLogger
    {
        // Project folder path used elsewhere in the codebase. Intentionally hard-coded to workspace root.
        private static readonly string ProjectFolder = @"D:\SolidWorks Project\Rule-Based-CAD-Modeling-System-Using-C-and-SolidWorks-API";

        private static readonly string LogFilePath = System.IO.Path.Combine(ProjectFolder, "aicad_log.txt");

        public static string LogPath => LogFilePath;

        public static void Log(string message)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(LogFilePath);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                File.AppendAllText(LogFilePath, DateTime.Now.ToString("o") + " " + (message ?? string.Empty) + Environment.NewLine);
            }
            catch { }
        }
    }
}
