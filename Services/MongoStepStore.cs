using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using System.Security.Authentication;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    internal class MongoStepStore : IStepStore
    {
        private readonly IMongoDatabase _db;
        private readonly IMongoCollection<BsonDocument> _runs;
        private readonly IMongoCollection<BsonDocument> _steps;
        private readonly IMongoCollection<BsonDocument> _runFeedback;
        public string LastError { get; private set; }

        public MongoStepStore(string connectionString, string databaseName)
        {
            try
            {
                AddinStatusLogger.Log("MongoStepStore", "ctor: connecting to " + databaseName);
                var settings = MongoClientSettings.FromConnectionString(connectionString);
                // Force TLS 1.2 to avoid SSPI/SChannel handshake failures on some Windows hosts
                try { settings.SslSettings = new SslSettings { EnabledSslProtocols = SslProtocols.Tls12 }; } catch { }
                settings.ServerApi = new ServerApi(ServerApiVersion.V1);
                settings.ConnectTimeout = TimeSpan.FromSeconds(6);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(6);
                var client = new MongoClient(settings);
                _db = client.GetDatabase(databaseName);
                _runs = _db.GetCollection<BsonDocument>("runs");
                _steps = _db.GetCollection<BsonDocument>("steps");
                _runFeedback = _db.GetCollection<BsonDocument>("run_feedback");
                LastError = null;
                AddinStatusLogger.Log("MongoStepStore", "ctor: connected to mongo");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("MongoStepStore", "ctor failed", ex);
            }
        }

        public async Task<bool> SaveRunWithStepsAsync(string runKey, string prompt, string model, string planJson, StepExecutionResult exec, TimeSpan llm, TimeSpan total, string error)
        {
            try
            {
                var runDoc = new BsonDocument
                {
                    { "run_key", runKey ?? string.Empty },
                    { "ts", DateTime.UtcNow },
                    { "prompt", prompt ?? string.Empty },
                    { "model", model ?? string.Empty },
                    { "plan", planJson ?? string.Empty },
                    { "success", exec?.Success ?? false },
                    { "llm_ms", (long)llm.TotalMilliseconds },
                    { "total_ms", (long)total.TotalMilliseconds },
                    { "error", error ?? string.Empty }
                };
                var filter = Builders<BsonDocument>.Filter.Eq("run_key", runKey);
                await _runs.ReplaceOneAsync(filter, runDoc, new ReplaceOptions { IsUpsert = true }).ConfigureAwait(false);
                await _steps.DeleteManyAsync(Builders<BsonDocument>.Filter.Eq("run_key", runKey)).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(planJson))
                {
                    try
                    {
                        var planObj = JObject.Parse(planJson);
                        var arr = planObj["steps"] as JArray;
                        if (arr != null)
                        {
                            var docs = new List<BsonDocument>();
                            var execMap = new Dictionary<int, JObject>();
                            if (exec?.Log != null)
                            {
                                foreach (var entry in exec.Log)
                                {
                                    int idx = entry.Value<int?>("step") ?? -1;
                                    if (idx >= 0) execMap[idx] = entry;
                                }
                            }
                            for (int i = 0; i < arr.Count; i++)
                            {
                                var s = arr[i] as JObject ?? new JObject();
                                var op = s.Value<string>("op") ?? string.Empty;
                                var sCopy = s.DeepClone() as JObject ?? new JObject();
                                sCopy.Remove("op");
                                var execEntry = execMap.ContainsKey(i) ? execMap[i] : null;
                                bool succ = execEntry?.Value<bool?>("success") == true;
                                var err = execEntry?.Value<string>("error") ?? string.Empty;
                                var doc = new BsonDocument
                                {
                                    { "run_key", runKey ?? string.Empty },
                                    { "step_index", i },
                                    { "op", op },
                                    { "params_json", AICAD.Services.JsonUtils.SerializeCompact(sCopy) },
                                    { "success", succ },
                                    { "error", err }
                                };
                                docs.Add(doc);
                            }
                            if (docs.Count > 0) await _steps.InsertManyAsync(docs).ConfigureAwait(false);
                            AddinStatusLogger.Log("MongoStepStore", $"Inserted {docs.Count} steps for run={runKey}");
                        }
                    }
                    catch { /* ignore malformed plan */ }
                }
                LastError = null;
                AddinStatusLogger.Log("MongoStepStore", $"SaveRunWithStepsAsync succeeded run={runKey}");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("MongoStepStore", "SaveRunWithStepsAsync failed", ex);
                return false;
            }
        }

        public async Task<bool> SaveFeedbackAsync(string runKey, bool up, string comment)
        {
            try
            {
                var doc = new BsonDocument
                {
                    { "run_key", runKey ?? string.Empty },
                    { "ts", DateTime.UtcNow },
                    { "thumb", up ? "up" : "down" },
                    { "comment", comment ?? string.Empty }
                };
                await _runFeedback.InsertOneAsync(doc).ConfigureAwait(false);
                LastError = null;
                AddinStatusLogger.Log("MongoStepStore", $"SaveFeedbackAsync succeeded run={runKey} thumb={up}");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("MongoStepStore", "SaveFeedbackAsync failed", ex);
                return false;
            }
        }

        public List<string> GetRelevantFewShots(string prompt, int max = 3)
        {
            try
            {
                var words = Tokenize(prompt);
                var runs = _runs.Find(Builders<BsonDocument>.Filter.Eq("success", true))
                                 .Sort(Builders<BsonDocument>.Sort.Descending("ts"))
                                 .Limit(200)
                                 .ToList();
                var candidates = new List<(int score, string prompt, string plan, DateTime ts)>();
                foreach (var r in runs)
                {
                    var p = r.GetValue("prompt", "").AsString;
                    var plan = r.GetValue("plan", "").AsString;
                    var ts = r.GetValue("ts", DateTime.MinValue).ToUniversalTime();
                    int score = Score(words, p);
                    candidates.Add((score, p, plan, ts));
                }
                var result = candidates.OrderByDescending(c => c.score).ThenByDescending(c => c.ts).Take(max)
                    .Select(c => "\\nInput: " + c.prompt + "\\nOutput:" + c.plan).ToList();
                AddinStatusLogger.Log("MongoStepStore", "GetRelevantFewShots returning " + result.Count);
                return result;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("MongoStepStore", "GetRelevantFewShots failed", ex);
                return new List<string>();
            }
        }

        public List<RunRow> GetRecentRuns(int max = 50)
        {
            var list = new List<RunRow>();
            try
            {
                var runs = _runs.Find(FilterDefinition<BsonDocument>.Empty)
                                 .Sort(Builders<BsonDocument>.Sort.Descending("ts"))
                                 .Limit(max)
                                 .ToList();
                foreach (var r in runs)
                {
                    list.Add(new RunRow
                    {
                        RunKey = r.GetValue("run_key", "").AsString,
                        Timestamp = r.GetValue("ts", DateTime.MinValue).ToUniversalTime().ToString("o"),
                        Prompt = r.GetValue("prompt", "").AsString,
                        Model = r.GetValue("model", "").AsString,
                        Plan = r.GetValue("plan", "").AsString,
                        Success = r.GetValue("success", false).ToBoolean(),
                        LlmMs = r.GetValue("llm_ms", 0).ToInt64(),
                        TotalMs = r.GetValue("total_ms", 0).ToInt64(),
                        Error = r.GetValue("error", "").AsString
                    });
                }
                LastError = null;
                AddinStatusLogger.Log("MongoStepStore", "GetRecentRuns succeeded count=" + list.Count);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("MongoStepStore", "GetRecentRuns failed", ex);
            }
            return list;
        }

        public List<StepRow> GetStepsForRun(string runKey)
        {
            var list = new List<StepRow>();
            try
            {
                var steps = _steps.Find(Builders<BsonDocument>.Filter.Eq("run_key", runKey))
                                   .Sort(Builders<BsonDocument>.Sort.Ascending("step_index"))
                                   .ToList();
                foreach (var s in steps)
                {
                    list.Add(new StepRow
                    {
                        StepIndex = s.GetValue("step_index", 0).ToInt32(),
                        Op = s.GetValue("op", "").AsString,
                        ParamsJson = s.GetValue("params_json", "").AsString,
                        Success = s.GetValue("success", false).ToBoolean(),
                        Error = s.GetValue("error", "").AsString
                    });
                }
                LastError = null;
                AddinStatusLogger.Log("MongoStepStore", "GetStepsForRun succeeded count=" + list.Count);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("MongoStepStore", "GetStepsForRun failed", ex);
            }
            return list;
        }

        private static HashSet<string> Tokenize(string text)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text)) return set;
            var sb = new System.Text.StringBuilder();
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
            var sb = new System.Text.StringBuilder();
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
