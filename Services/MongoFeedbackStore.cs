using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using System.Security.Authentication;

namespace AICAD.Services
{
    internal class MongoFeedbackStore : IGoodFeedbackStore
    {
        private readonly IMongoCollection<BsonDocument> _collection;
        public string LastError { get; private set; }

    public MongoFeedbackStore(string connectionString, string databaseName = "TaskPaneAddin", string collectionName = "good_feedback")
        {
            try
            {
                AddinStatusLogger.Log("MongoFeedbackStore", "ctor: starting with database=" + databaseName);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    LastError = "Missing Mongo connection string";
                    AddinStatusLogger.Error("MongoFeedbackStore", "ctor: missing connection string");
                    return;
                }

                var settings = MongoClientSettings.FromConnectionString(connectionString);
                // Force TLS 1.2 to avoid SSPI/SChannel handshake failures on some Windows hosts
                try { settings.SslSettings = new SslSettings { EnabledSslProtocols = SslProtocols.Tls12 }; } catch { }
                settings.ServerApi = new ServerApi(ServerApiVersion.V1, strict: true, deprecationErrors: true);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
                settings.ConnectTimeout = TimeSpan.FromSeconds(5);
                var client = new MongoClient(settings);

                // quick ping
                try
                {
                    AddinStatusLogger.Log("MongoFeedbackStore", "ping admin database");
                    client.GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument("ping", 1));
                    AddinStatusLogger.Log("MongoFeedbackStore", "ping ok");
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    AddinStatusLogger.Error("MongoFeedbackStore", "ping failed", ex);
                    return;
                }

                var db = client.GetDatabase(databaseName);
                _collection = db.GetCollection<BsonDocument>(collectionName);
                LastError = null;
                AddinStatusLogger.Log("MongoFeedbackStore", "connected to collection " + collectionName);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _collection = null;
                AddinStatusLogger.Error("MongoFeedbackStore", "ctor exception", ex);
            }
        }

        public async Task<bool> SaveGoodAsync(string runId, string prompt, string model, string planJson, string comment)
        {
            if (_collection == null)
            {
                if (string.IsNullOrWhiteSpace(LastError)) LastError = "Mongo collection not available";
                return false;
            }
            try
            {
                var doc = new BsonDocument
                {
                    { "ts", DateTime.UtcNow },
                    { "runId", runId ?? string.Empty },
                    { "prompt", prompt ?? string.Empty },
                    { "model", model ?? string.Empty },
                    { "plan", planJson ?? string.Empty },
                    { "comment", comment ?? string.Empty }
                };
                await _collection.InsertOneAsync(doc).ConfigureAwait(false);
                LastError = null;
                AddinStatusLogger.Log("MongoFeedbackStore", $"Inserted good feedback run={runId}");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("MongoFeedbackStore", "Insert failed", ex);
                return false;
            }
        }

        public List<string> GetRecentFewShots(int max = 2)
        {
            var shots = new List<string>();
            if (_collection == null) return shots;
            try
            {
                // Allow forcing only "key" shots when environment requests it
                var forceKey = (System.Environment.GetEnvironmentVariable("AICAD_FORCE_KEY_SHOTS") ?? "").Equals("1", StringComparison.OrdinalIgnoreCase)
                               || (System.Environment.GetEnvironmentVariable("AICAD_FORCE_KEY_SHOTS") ?? "").Equals("true", StringComparison.OrdinalIgnoreCase);

                // First try to fetch key-marked examples (comment contains 'key')
                var keyFilter = Builders<BsonDocument>.Filter.Regex("comment", new BsonRegularExpression("key", "i"));
                var baseSort = Builders<BsonDocument>.Sort.Descending("ts");
                if (forceKey)
                {
                    var optsKey = new FindOptions<BsonDocument> { Sort = baseSort, Limit = max, Projection = Builders<BsonDocument>.Projection.Include("prompt").Include("plan").Include("comment") };
                    using (var cursor = _collection.FindSync(keyFilter, optsKey))
                    {
                        foreach (var doc in cursor.ToEnumerable())
                        {
                            var prompt = doc.GetValue("prompt", string.Empty).AsString;
                            var plan = AICAD.Services.JsonUtils.SerializeCompact(doc.GetValue("plan", "{}"));
                            shots.Add("\nInput: " + prompt + "\nOutput:" + plan);
                        }
                    }
                    LastError = null;
                    AddinStatusLogger.Log("MongoFeedbackStore", "GetRecentFewShots (key-only) succeeded count=" + shots.Count);
                    return shots;
                }

                // Otherwise prefer key shots but fill with recent if not enough
                var optsAll = new FindOptions<BsonDocument> { Sort = baseSort, Limit = Math.Max(max * 3, max), Projection = Builders<BsonDocument>.Projection.Include("prompt").Include("plan").Include("comment") };
                var candidates = new List<BsonDocument>();
                using (var cursor = _collection.FindSync(FilterDefinition<BsonDocument>.Empty, optsAll))
                {
                    foreach (var doc in cursor.ToEnumerable()) candidates.Add(doc);
                }

                // Select key-marked first
                foreach (var doc in candidates)
                {
                    if (shots.Count >= max) break;
                    var comment = doc.GetValue("comment", string.Empty).AsString ?? string.Empty;
                    if (!string.IsNullOrEmpty(comment) && comment.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var prompt = doc.GetValue("prompt", string.Empty).AsString;
                        var plan = AICAD.Services.JsonUtils.SerializeCompact(doc.GetValue("plan", "{}"));
                        shots.Add("\nInput: " + prompt + "\nOutput:" + plan);
                    }
                }

                // Fill remaining with non-key recent
                if (shots.Count < max)
                {
                    foreach (var doc in candidates)
                    {
                        if (shots.Count >= max) break;
                        var prompt = doc.GetValue("prompt", string.Empty).AsString;
                        var plan = AICAD.Services.JsonUtils.SerializeCompact(doc.GetValue("plan", "{}"));
                        var entry = "\nInput: " + prompt + "\nOutput:" + plan;
                        if (!shots.Contains(entry)) shots.Add(entry);
                    }
                }

                LastError = null;
                AddinStatusLogger.Log("MongoFeedbackStore", "GetRecentFewShots succeeded count=" + shots.Count);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("MongoFeedbackStore", "GetRecentFewShots failed", ex);
            }
            return shots;
        }
    }
}
