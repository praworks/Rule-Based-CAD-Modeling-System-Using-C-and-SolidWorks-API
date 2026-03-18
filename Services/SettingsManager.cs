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

        // Generic double settings stored under the same registry branch
        public static double GetDouble(string key, double defaultValue)
        {
            try
            {
                using (var reg = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (reg != null)
                    {
                        var v = reg.GetValue(key);
                        if (v != null)
                        {
                            if (double.TryParse(v.ToString(), out var d)) return d;
                        }
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        public static bool SetDouble(string key, double value)
        {
            try
            {
                using (var reg = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    reg?.SetValue(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                return true;
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(SettingsManager), "Failed to save setting " + key, ex);
                return false;
            }
        }

        // Bool helpers stored as 0/1 strings
        public static bool GetBool(string key, bool defaultValue)
        {
            try
            {
                using (var reg = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (reg != null)
                    {
                        var v = reg.GetValue(key);
                        if (v != null)
                        {
                            var s = v.ToString();
                            if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                            if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                        }
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        public static bool SetBool(string key, bool value)
        {
            try
            {
                using (var reg = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    reg?.SetValue(key, value ? "1" : "0");
                }
                return true;
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(SettingsManager), "Failed to save setting " + key + "", ex);
                return false;
            }
        }

        public static string GetString(string key, string defaultValue)
        {
            try
            {
                using (var reg = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    var v = reg?.GetValue(key)?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            catch { }
            return defaultValue;
        }

        public static bool SetString(string key, string value)
        {
            try
            {
                using (var reg = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    reg?.SetValue(key, value ?? string.Empty);
                }
                return true;
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(SettingsManager), "Failed to save setting " + key, ex);
                return false;
            }
        }
    }
}
