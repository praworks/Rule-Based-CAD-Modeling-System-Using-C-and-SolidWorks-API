using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AICAD.Services
{
    internal class FileGoodFeedbackStore : IGoodFeedbackStore
    {
        private readonly string _path;
        public string LastError { get; private set; }

        public FileGoodFeedbackStore(string baseDirectory, string fileName = "good_feedback.jsonl")
        {
            Directory.CreateDirectory(baseDirectory);
            _path = Path.Combine(baseDirectory, fileName);
        }

        public async Task<bool> SaveGoodAsync(string runId, string prompt, string model, string planJson, string comment)
        {
            try
            {
                var line = string.Concat(
                    "{\"ts\":\"", DateTime.UtcNow.ToString("o"), "\",",
                    "\"runId\":\"", (runId ?? string.Empty).Replace("\"", "'"), "\",",
                    "\"prompt\":\"", (prompt ?? string.Empty).Replace("\"", "'"), "\",",
                    "\"model\":\"", (model ?? string.Empty).Replace("\"", "'"), "\",",
                    "\"plan\":", string.IsNullOrWhiteSpace(planJson) ? "{}" : planJson, ",",
                    "\"comment\":\"", (comment ?? string.Empty).Replace("\"", "'"), "\"}"
                );
                using (var sw = new StreamWriter(_path, true, Encoding.UTF8))
                {
                    await sw.WriteLineAsync(line).ConfigureAwait(false);
                }
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public List<string> GetRecentFewShots(int max = 2)
        {
            var shots = new List<string>();
            try
            {
                if (!File.Exists(_path)) return shots;
                var lines = File.ReadAllLines(_path);
                // Check for forced key-only mode
                var forceKey = (System.Environment.GetEnvironmentVariable("AICAD_FORCE_KEY_SHOTS") ?? "").Equals("1", StringComparison.OrdinalIgnoreCase)
                               || (System.Environment.GetEnvironmentVariable("AICAD_FORCE_KEY_SHOTS") ?? "").Equals("true", StringComparison.OrdinalIgnoreCase);

                if (forceKey)
                {
                    for (int i = lines.Length - 1; i >= 0 && shots.Count < max; i--)
                    {
                        var line = lines[i];
                        var comment = Extract(line, "\"comment\":\"", "\"") ?? string.Empty;
                        if (string.IsNullOrEmpty(comment) || comment.IndexOf("key", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var prompt = Extract(line, "\"prompt\":\"", "\"") ?? string.Empty;
                        var plan = ExtractAfter(line, "\"plan\":") ?? "{}";
                        var sb = new StringBuilder();
                        sb.Append("\nInput: ").Append(prompt);
                        sb.Append("\nOutput:").Append(plan);
                        shots.Add(sb.ToString());
                    }
                }
                else
                {
                    // Gather candidates and prefer key-marked
                    var candidates = new System.Collections.Generic.List<(string prompt, string plan, string comment)>();
                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        var line = lines[i];
                        var prompt = Extract(line, "\"prompt\":\"", "\"") ?? string.Empty;
                        var plan = ExtractAfter(line, "\"plan\":") ?? "{}";
                        var comment = Extract(line, "\"comment\":\"", "\"") ?? string.Empty;
                        candidates.Add((prompt, plan, comment));
                        if (candidates.Count >= Math.Max(max * 3, max)) break;
                    }
                    foreach (var c in candidates)
                    {
                        if (shots.Count >= max) break;
                        if (!string.IsNullOrEmpty(c.comment) && c.comment.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var sb = new StringBuilder();
                            sb.Append("\nInput: ").Append(c.prompt);
                            sb.Append("\nOutput:").Append(c.plan);
                            shots.Add(sb.ToString());
                        }
                    }
                    if (shots.Count < max)
                    {
                        foreach (var c in candidates)
                        {
                            if (shots.Count >= max) break;
                            var sb = new StringBuilder();
                            sb.Append("\nInput: ").Append(c.prompt);
                            sb.Append("\nOutput:").Append(c.plan);
                            var entry = sb.ToString();
                            if (!shots.Contains(entry)) shots.Add(entry);
                        }
                    }
                }
                LastError = null;
                AddinStatusLogger.Log("FileGoodFeedbackStore", "GetRecentFewShots succeeded lines=" + (File.Exists(_path) ? File.ReadAllLines(_path).Length.ToString() : "0"));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("FileGoodFeedbackStore", "GetRecentFewShots failed", ex);
            }
            return shots;
        }

        private static string Extract(string text, string start, string end)
        {
            var i = text.IndexOf(start, StringComparison.Ordinal);
            if (i < 0) return null;
            i += start.Length;
            var j = text.IndexOf(end, i, StringComparison.Ordinal);
            if (j < 0) return null;
            return text.Substring(i, j - i);
        }

        private static string ExtractAfter(string text, string start)
        {
            var i = text.IndexOf(start, StringComparison.Ordinal);
            if (i < 0) return null;
            i += start.Length;
            return text.Substring(i).TrimEnd();
        }
    }
}
