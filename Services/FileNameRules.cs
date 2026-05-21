using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AICAD.Services
{
    /// <summary>
    /// Validates and sanitizes Windows filename stems used by the naming workflow.
    /// </summary>
    public static class FileNameRules
    {
        private static readonly HashSet<string> ReservedDeviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static bool TryValidateSeriesId(string value, out string error)
        {
            error = null;
            var seriesId = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(seriesId))
            {
                error = "Series ID cannot be empty.";
                return false;
            }

            foreach (var ch in seriesId)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'))
                {
                    error = "Series ID can contain only letters, numbers, '-' and '_'.";
                    return false;
                }
            }

            if (IsReservedDeviceName(seriesId))
            {
                error = $"Series ID '{seriesId}' is reserved by Windows.";
                return false;
            }

            return true;
        }

        public static bool TryValidateFileStem(string value, out string error)
        {
            error = null;
            var stem = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(stem))
            {
                error = "File name cannot be empty.";
                return false;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var ch in stem)
            {
                if (Array.IndexOf(invalidChars, ch) >= 0)
                {
                    error = $"File name cannot contain '{ch}'.";
                    return false;
                }
            }

            if (stem.EndsWith(".", StringComparison.Ordinal) || stem.EndsWith(" ", StringComparison.Ordinal))
            {
                error = "File name cannot end with a period or space.";
                return false;
            }

            if (IsReservedDeviceName(stem))
            {
                error = $"File name '{stem}' is reserved by Windows.";
                return false;
            }

            return true;
        }

        public static string SanitizeFileStem(string value, string fallback = "Part")
        {
            var stem = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(stem))
            {
                return fallback;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(stem.Length);
            foreach (var ch in stem)
            {
                builder.Append(Array.IndexOf(invalidChars, ch) >= 0 ? '_' : ch);
            }

            stem = builder.ToString().Trim().TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(stem))
            {
                stem = fallback;
            }

            if (IsReservedDeviceName(stem))
            {
                stem += "_";
            }

            return stem;
        }

        private static bool IsReservedDeviceName(string value)
        {
            var stem = Path.GetFileNameWithoutExtension((value ?? string.Empty).Trim());
            return !string.IsNullOrWhiteSpace(stem) && ReservedDeviceNames.Contains(stem);
        }
    }
}
