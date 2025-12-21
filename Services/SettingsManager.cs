using System;
using System.IO;
using Microsoft.Win32;

namespace AICAD.Services
{
    /// <summary>
    /// Stores NameEasy settings (database path) in HKCU to keep user configurable.
    /// </summary>
    public static class SettingsManager
    {
        private const string RegistryPath = @"Software\AI-CAD\NameEasy";
        private const string DbPathKey = "DatabasePath";

        public static string GetDatabasePath()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key != null)
                    {
                        var saved = key.GetValue(DbPathKey) as string;
                        if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
                        {
                            AddinLogger.Log(nameof(SettingsManager), $"Loaded database path from registry: {saved}");
                            return saved;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(SettingsManager), "Failed to read database path from registry", ex);
            }

            var fallback = GetDefaultDatabasePath();
            AddinLogger.Log(nameof(SettingsManager), $"Using default database path: {fallback}");
            return fallback;
        }

        public static bool SetDatabasePath(string path)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    key?.SetValue(DbPathKey, path);
                }
                AddinLogger.Log(nameof(SettingsManager), $"Saved database path: {path}");
                return true;
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(SettingsManager), "Failed to save database path", ex);
                return false;
            }
        }

        public static string GetDefaultDatabasePath()
        {
            var asmDir = Path.GetDirectoryName(typeof(SettingsManager).Assembly.Location) ?? string.Empty;
            return Path.Combine(asmDir, "NameEasy.db");
        }
    }
}
