using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    internal class SqliteStepStore : IStepStore
    {
        private readonly string _dbPath;
        private readonly string _connString;
        public string LastError { get; private set; }

        public SqliteStepStore(string baseDirectory, string dbFileName = "run_feedback.db")
        {
            if (string.IsNullOrWhiteSpace(baseDirectory)) throw new ArgumentException("Base directory is required", nameof(baseDirectory));
            Directory.CreateDirectory(baseDirectory);
            _dbPath = Path.Combine(baseDirectory, dbFileName);
            _connString = $"Data Source={_dbPath};Version=3;Pooling=True;Journal Mode=WAL";
            AddinStatusLogger.Log("SqliteStepStore", "ctor: dbPath=" + _dbPath);
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
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_key TEXT UNIQUE,
    ts TEXT NOT NULL,
    prompt TEXT,
    model TEXT,
    plan TEXT,
    success INTEGER,
    llm_ms INTEGER,
    total_ms INTEGER,
    error TEXT
);
CREATE TABLE IF NOT EXISTS steps (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_key TEXT NOT NULL,
    step_index INTEGER NOT NULL,
    op TEXT NOT NULL,
    params_json TEXT,
    success INTEGER,
    error TEXT,
    FOREIGN KEY(run_key) REFERENCES runs(run_key) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_runs_ts ON runs(ts DESC);
CREATE INDEX IF NOT EXISTS idx_steps_run ON steps(run_key);
CREATE INDEX IF NOT EXISTS idx_steps_op ON steps(op);
CREATE TABLE IF NOT EXISTS run_feedback (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_key TEXT NOT NULL,
    ts TEXT NOT NULL,
    thumb TEXT,
    comment TEXT
);
";
                        cmd.ExecuteNonQuery();
                    }
                }
                LastError = null;
                AddinStatusLogger.Log("SqliteStepStore", "EnsureSchema OK dbPath=" + _dbPath);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("SqliteStepStore", "EnsureSchema failed", ex);
            }
        }

    public async Task<bool> SaveRunWithStepsAsync(
            string runKey,
            string prompt,
            string model,
            string planJson,
            StepExecutionResult exec,
            TimeSpan llm,
            TimeSpan total,
            string error)
        {
            try
            {
                using (var conn = new SQLiteConnection(_connString))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    using (var tx = conn.BeginTransaction())
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT OR REPLACE INTO runs (run_key, ts, prompt, model, plan, success, llm_ms, total_ms, error) VALUES (@run, @ts, @prompt, @model, @plan, @succ, @llm, @total, @err)";
                        cmd.Parameters.AddWithValue("@run", (object)runKey ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
                        cmd.Parameters.AddWithValue("@prompt", (object)prompt ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@model", (object)model ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@plan", (object)planJson ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@succ", exec != null && exec.Success ? 1 : 0);
                        cmd.Parameters.AddWithValue("@llm", (long)llm.TotalMilliseconds);
                        cmd.Parameters.AddWithValue("@total", (long)total.TotalMilliseconds);
                        cmd.Parameters.AddWithValue("@err", (object)error ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

                        // Remove previous steps for same run_key (if any)
                        cmd.Parameters.Clear();
                        cmd.CommandText = "DELETE FROM steps WHERE run_key=@run";
                        cmd.Parameters.AddWithValue("@run", (object)runKey ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

                        // Insert steps if plan is available
                        if (!string.IsNullOrWhiteSpace(planJson))
                        {
                            JArray stepsArr = null;
                            try
                            {
                                var planObj = JObject.Parse(AICAD.Services.JsonUtils.SerializeCompact(planJson));
                                stepsArr = planObj["steps"] as JArray;
                            }
                            catch { /* ignore parse errors; we'll skip steps */ }

                            if (stepsArr != null)
                            {
                                // Build a quick lookup for exec log by step index
                                var execMap = new Dictionary<int, JObject>();
                                if (exec?.Log != null)
                                {
                                    foreach (var entry in exec.Log)
                                    {
                                        int idx = entry.Value<int?>("step") ?? -1;
                                        if (idx >= 0) execMap[idx] = entry;
                                    }
                                }

                                for (int i = 0; i < stepsArr.Count; i++)
                                {
                                    var s = stepsArr[i] as JObject ?? new JObject();
                                    var op = s.Value<string>("op") ?? string.Empty;
                                    // clone without mutating source
                                    var sCopy = s.DeepClone();
                                    // remove op from params to avoid duplication
                                    if (sCopy is JObject so) so.Remove("op");
                                    var execEntry = execMap.ContainsKey(i) ? execMap[i] : null;
                                    int succ = execEntry?.Value<bool?>("success") == true ? 1 : 0;
                                    var err = execEntry?.Value<string>("error");

                                    cmd.Parameters.Clear();
                                    cmd.CommandText = "INSERT INTO steps (run_key, step_index, op, params_json, success, error) VALUES (@run, @idx, @op, @params, @succ, @err)";
                                    cmd.Parameters.AddWithValue("@run", (object)runKey ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@idx", i);
                                    cmd.Parameters.AddWithValue("@op", op);
                                    cmd.Parameters.AddWithValue("@params", AICAD.Services.JsonUtils.SerializeCompact(sCopy));
                                    cmd.Parameters.AddWithValue("@succ", succ);
                                    cmd.Parameters.AddWithValue("@err", (object)err ?? DBNull.Value);
                                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                                }
                            }
                        }

                        tx.Commit();
                    }
                }
                LastError = null;
                AddinStatusLogger.Log("SqliteStepStore", "SaveRunWithStepsAsync succeeded run=" + runKey);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("SqliteStepStore", "SaveRunWithStepsAsync failed", ex);
                return false;
            }
        }

    public async Task<bool> SaveFeedbackAsync(string runKey, bool up, string comment)
        {
            try
            {
                using (var conn = new SQLiteConnection(_connString))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO run_feedback (run_key, ts, thumb, comment) VALUES (@run, @ts, @thumb, @comment)";
                        cmd.Parameters.AddWithValue("@run", (object)runKey ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
                        cmd.Parameters.AddWithValue("@thumb", up ? "up" : "down");
                        cmd.Parameters.AddWithValue("@comment", (object)comment ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
                LastError = null;
                AddinStatusLogger.Log("SqliteStepStore", "SaveFeedbackAsync succeeded run=" + runKey + " thumb=" + (up?"up":"down"));
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("SqliteStepStore", "SaveFeedbackAsync failed", ex);
                return false;
            }
        }

        // Very simple retrieval: score runs by word overlap with prompt and success=1; return few-shot strings
    public List<string> GetRelevantFewShots(string prompt, int max = 3)
        {
            var shots = new List<string>();
            try
            {
                var words = Tokenize(prompt);
                var candidates = new List<(int score, string prompt, string plan, string ts)>();
                using (var conn = new SQLiteConnection(_connString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT prompt, plan, ts FROM runs WHERE success=1 AND plan IS NOT NULL ORDER BY ts DESC LIMIT 100";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var p = r[0] as string ?? string.Empty;
                                var plan = r[1] as string ?? string.Empty;
                                var ts = r[2] as string ?? string.Empty;
                                int score = Score(words, p);
                                candidates.Add((score, p, plan, ts));
                            }
                        }
                    }
                }

                foreach (var c in candidates.OrderByDescending(c => c.score).ThenByDescending(c => c.ts).Take(max))
                {
                    var sb = new StringBuilder();
                    sb.Append("\nInput: ").Append(c.prompt);
                    sb.Append("\nOutput:").Append(c.plan);
                    shots.Add(sb.ToString());
                }
                LastError = null;
                AddinStatusLogger.Log("SqliteStepStore", "GetRelevantFewShots succeeded count=" + shots.Count);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("SqliteStepStore", "GetRelevantFewShots failed", ex);
            }
            return shots;
        }

    public List<RunRow> GetRecentRuns(int max = 50)
        {
            var runs = new List<RunRow>();
            try
            {
                using (var conn = new SQLiteConnection(_connString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT run_key, ts, prompt, model, plan, success, llm_ms, total_ms, error FROM runs ORDER BY ts DESC LIMIT @n";
                        cmd.Parameters.AddWithValue("@n", max);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                runs.Add(new RunRow
                                {
                                    RunKey = r[0] as string,
                                    Timestamp = r[1] as string,
                                    Prompt = r[2] as string,
                                    Model = r[3] as string,
                                    Plan = r[4] as string,
                                    Success = Convert.ToInt32(r[5]) == 1,
                                    LlmMs = r.IsDBNull(6) ? 0 : Convert.ToInt64(r[6]),
                                    TotalMs = r.IsDBNull(7) ? 0 : Convert.ToInt64(r[7]),
                                    Error = r[8] as string
                                });
                            }
                        }
                    }
                }
                LastError = null;
                AddinStatusLogger.Log("SqliteStepStore", "GetRecentRuns succeeded count=" + runs.Count);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("SqliteStepStore", "GetRecentRuns failed", ex);
            }
            return runs;
        }

    public List<StepRow> GetStepsForRun(string runKey)
        {
            var steps = new List<StepRow>();
            try
            {
                using (var conn = new SQLiteConnection(_connString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT step_index, op, params_json, success, error FROM steps WHERE run_key=@run ORDER BY step_index ASC";
                        cmd.Parameters.AddWithValue("@run", (object)runKey ?? DBNull.Value);
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                steps.Add(new StepRow
                                {
                                    StepIndex = Convert.ToInt32(r[0]),
                                    Op = r[1] as string,
                                    ParamsJson = r[2] as string,
                                    Success = Convert.ToInt32(r[3]) == 1,
                                    Error = r[4] as string
                                });
                            }
                        }
                    }
                }
                LastError = null;
                AddinStatusLogger.Log("SqliteStepStore", "GetStepsForRun succeeded count=" + steps.Count);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("SqliteStepStore", "GetStepsForRun failed", ex);
            }
            return steps;
        }
        private static HashSet<string> Tokenize(string text)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text)) return set;
            var sb = new StringBuilder();
            foreach (var ch in text)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch)); else sb.Append(' ');
            }
            foreach (var w in sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (w.Length > 2) set.Add(w);
            }
            return set;
        }

        private static int Score(HashSet<string> words, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || words == null || words.Count == 0) return 0;
            int score = 0;
            int i = 0;
            var sb = new StringBuilder();
            while (i < text.Length)
            {
                char ch = text[i++];
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch)); else sb.Append(' ');
            }
            foreach (var w in sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (w.Length > 2 && words.Contains(w)) score++;
            }
            return score;
        }
    }

    
}
