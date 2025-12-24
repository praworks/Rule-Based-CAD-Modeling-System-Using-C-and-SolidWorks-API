using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    internal class FileDbLogger
    {
        private readonly string _dbFilePath;
        private string _lastError;

        public FileDbLogger(string baseDirectory, string dbFileName = "nl2cad.db.jsonl")
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
                throw new ArgumentException("Base directory is required", nameof(baseDirectory));

            Directory.CreateDirectory(baseDirectory);
            _dbFilePath = Path.Combine(baseDirectory, dbFileName);
            try { AddinStatusLogger.Log("FileDbLogger", $"Using file db at {_dbFilePath}"); } catch { }
        }

        public bool IsAvailable => true; // file-based logging always available if path exists
        public string LastError => _lastError;

        public async Task<bool> LogAsync(string prompt, string reply, string model, TimeSpan llmDuration, TimeSpan totalDuration, string error = null)
        {
            var doc = new JObject
            {
                ["collection"] = "SW",
                ["timestamp"] = DateTime.UtcNow,
                ["prompt"] = prompt ?? string.Empty,
                ["reply"] = reply ?? string.Empty,
                ["model"] = model ?? string.Empty,
                ["llmMs"] = (long)llmDuration.TotalMilliseconds,
                ["totalMs"] = (long)totalDuration.TotalMilliseconds,
                ["error"] = error ?? string.Empty
            };
            return await AppendAsync(doc);
        }

        public async Task<bool> InsertAsync(string collection, JObject doc)
        {
            if (doc == null) doc = new JObject();
            doc["collection"] = collection ?? "unknown";
            doc["timestamp"] = doc["timestamp"] ?? DateTime.UtcNow;
            return await AppendAsync(doc);
        }

        private async Task<bool> AppendAsync(JObject doc)
        {
            try
            {
                var line = AICAD.Services.JsonUtils.SerializeCompact(doc) + Environment.NewLine;
                // Retry a few times to avoid transient file locks from other processes
                const int maxAttempts = 5;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        using (var fs = new FileStream(_dbFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                        using (var sw = new StreamWriter(fs, Encoding.UTF8))
                        {
                            await sw.WriteAsync(line).ConfigureAwait(false);
                            await sw.FlushAsync().ConfigureAwait(false);
                        }
                        _lastError = null;
                        try { AddinStatusLogger.Log("FileDbLogger", $"Appended document to file (len={line.Length})"); } catch { }
                        return true;
                    }
                    catch (IOException ioEx)
                    {
                        _lastError = ioEx.Message;
                        if (attempt == maxAttempts)
                        {
                            try { AddinStatusLogger.Error("FileDbLogger", "AppendAsync failed", ioEx); } catch { }
                            return false;
                        }
                        await Task.Delay(100 * attempt).ConfigureAwait(false);
                        continue;
                    }
                }
                // Shouldn't get here
                return false;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                try { AddinStatusLogger.Error("FileDbLogger", "AppendAsync failed", ex); } catch { }
                return false;
            }
        }
    }
}
