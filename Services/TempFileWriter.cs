using System;
using System.IO;

namespace AICAD.Services
{
    public static class TempFileWriter
    {
        public static bool Disabled => string.Equals(Environment.GetEnvironmentVariable("AICAD_DISABLE_TEMP_WRITES"), "1");

        public static string GetTempDir()
        {
            var env = Environment.GetEnvironmentVariable("AICAD_TEMP_DIR");
            if (!string.IsNullOrWhiteSpace(env)) return env;
            return Path.GetTempPath();
        }

        public static string GetPath(string filename)
        {
            if (Disabled) return null;
            try
            {
                var dir = GetTempDir();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, filename);
            }
            catch
            {
                return null;
            }
        }

        public static void AppendAllText(string filename, string text)
        {
            if (Disabled) return;
            try
            {
                var path = GetPath(filename);
                if (path == null) return;
                File.AppendAllText(path, text);
            }
            catch { }
        }

        public static FileStream CreateFile(string filename)
        {
            if (Disabled) return null;
            try
            {
                var path = GetPath(filename);
                if (path == null) return null;
                return File.Create(path);
            }
            catch { return null; }
        }
    }
}
