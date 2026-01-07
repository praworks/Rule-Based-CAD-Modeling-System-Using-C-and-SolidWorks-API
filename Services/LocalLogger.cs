using System;
using System.IO;

namespace AICAD.Services
{
    /// <summary>
    /// Simple local logger that writes to a temp log file using TempFileWriter
    /// to avoid locking files inside the workspace and Git conflicts.
    /// </summary>
    public static class LocalLogger
    {
        public static string LogPath => AICAD.Services.TempFileWriter.GetPath("aicad_log.txt");

        public static void Log(string message)
        {
            try
            {
                var line = DateTime.Now.ToString("o") + " " + (message ?? string.Empty) + Environment.NewLine;
                AICAD.Services.TempFileWriter.AppendAllText("aicad_log.txt", line);
            }
            catch { }
        }
    }
}
