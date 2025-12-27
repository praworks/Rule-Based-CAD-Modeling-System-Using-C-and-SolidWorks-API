using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AICAD.Services
{
    internal class SqliteFeedbackStore : IGoodFeedbackStore
    {
        private readonly string _dbPath;
        private readonly string _connString;
        public string LastError { get; private set; }

        public SqliteFeedbackStore(string baseDirectory, string dbFileName = "feedback.db")
        {
            if (string.IsNullOrWhiteSpace(baseDirectory)) throw new ArgumentException("Base directory is required", nameof(baseDirectory));
            Directory.CreateDirectory(baseDirectory);
            _dbPath = Path.Combine(baseDirectory, dbFileName);
            _connString = $"Data Source={_dbPath};Version=3;Pooling=True;Journal Mode=WAL";
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            try
            {
                using (var conn = new SQLiteConnection(_connString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS good_feedback (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    ts TEXT NOT NULL,
    runId TEXT,
    prompt TEXT,
    model TEXT,
    plan TEXT,
    comment TEXT
);
CREATE INDEX IF NOT EXISTS idx_feedback_ts ON good_feedback(ts DESC);
";
                        cmd.ExecuteNonQuery();
                    }
                }
                LastError = null;
                try { AddinStatusLogger.Log("SqliteFeedbackStore", $"Schema ensured at {_dbPath}"); } catch { }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                try { AddinStatusLogger.Error("SqliteFeedbackStore", "EnsureSchema failed", ex); } catch { }
            }
        }

        public async Task<bool> SaveGoodAsync(string runId, string prompt, string model, string planJson, string comment)
        {
            try
            {
                using (var conn = new SQLiteConnection(_connString))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO good_feedback (ts, runId, prompt, model, plan, comment) VALUES (@ts, @runId, @prompt, @model, @plan, @comment)";
                        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
                        cmd.Parameters.AddWithValue("@runId", (object)runId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@prompt", (object)prompt ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@model", (object)model ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@plan", (object)planJson ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@comment", (object)comment ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
                LastError = null;
                try { AddinStatusLogger.Log("SqliteFeedbackStore", $"Saved good feedback runId={runId} promptLen={prompt?.Length ?? 0}"); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                try { AddinStatusLogger.Error("SqliteFeedbackStore", "SaveGoodAsync failed", ex); } catch { }
                return false;
            }
        }

        public List<string> GetRecentFewShots(int max = 2)
        {
            var shots = new List<string>();
            try
            {
                using (var conn = new SQLiteConnection(_connString))
                {
                    conn.Open();
                    // Check for forced key-only mode
                    var forceKey = (System.Environment.GetEnvironmentVariable("AICAD_FORCE_KEY_SHOTS") ?? "").Equals("1", StringComparison.OrdinalIgnoreCase)
                                   || (System.Environment.GetEnvironmentVariable("AICAD_FORCE_KEY_SHOTS") ?? "").Equals("true", StringComparison.OrdinalIgnoreCase);

                    if (forceKey)
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT prompt, plan FROM good_feedback WHERE lower(comment) LIKE '%key%' ORDER BY ts DESC LIMIT @n";
                            cmd.Parameters.AddWithValue("@n", max);
                            using (var r = cmd.ExecuteReader())
                            {
                                while (r.Read())
                                {
                                    var prompt = r[0] as string ?? string.Empty;
                                    var plan = r[1] as string ?? "{}";
                                    var sb = new StringBuilder();
                                    sb.Append("\nInput: ").Append(prompt);
                                    sb.Append("\nOutput:").Append(plan);
                                    shots.Add(sb.ToString());
                                }
                            }
                        }
                    }
                    else
                    {
                        // Prefer key-marked examples but include recent ones to fill
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT prompt, plan, comment FROM good_feedback ORDER BY ts DESC LIMIT @n";
                            cmd.Parameters.AddWithValue("@n", Math.Max(max * 3, max));
                            using (var r = cmd.ExecuteReader())
                            {
                                var candidates = new System.Collections.Generic.List<(string prompt, string plan, string comment)>();
                                while (r.Read())
                                {
                                    candidates.Add((r[0] as string ?? string.Empty, r[1] as string ?? "{}", r[2] as string ?? string.Empty));
                                }
                                // key-marked first
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
                                // fill remaining
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
                        }
                    }
                }
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return shots;
        }
    }
}
