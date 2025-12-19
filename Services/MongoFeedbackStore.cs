using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

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
                var opts = new FindOptions<BsonDocument>
                {
                    Sort = Builders<BsonDocument>.Sort.Descending("ts"),
                    Limit = max,
                    Projection = Builders<BsonDocument>.Projection.Include("prompt").Include("plan")
                };
                using (var cursor = _collection.FindSync(FilterDefinition<BsonDocument>.Empty, opts))
                {
                        foreach (var doc in cursor.ToEnumerable())
                        {
                            var prompt = doc.GetValue("prompt", string.Empty).AsString;
                            var plan = doc.GetValue("plan", "{}").ToString();
                            shots.Add("\\nInput: " + prompt + "\\nOutput:" + plan);
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
