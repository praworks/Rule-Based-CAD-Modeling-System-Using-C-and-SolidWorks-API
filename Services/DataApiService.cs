using System;
using System.Net;
using System.Net.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;

namespace AICAD.Services
{
    /// <summary>
    /// MongoDB Data API service for posting feedback without database credentials.
    /// Users don't need MongoDB password - data posts via secure API endpoint.
    /// </summary>
    internal class DataApiService
    {
        static DataApiService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        private readonly string _apiEndpoint;
        private readonly string _apiKey;
        private readonly string _dataSource = "Cluster0";
        private readonly string _database = "TaskPaneAddin";
        private readonly HttpClient _httpClient;
        private readonly bool _useDirectMongo;
        private readonly MongoClient _mongoClient;
        public string LastError { get; private set; }

        /// <summary>
        /// Initialize with MongoDB Data API credentials
        /// </summary>
        public DataApiService(string apiEndpoint, string apiKey)
        {
            _apiEndpoint = apiEndpoint?.Trim();
            _apiKey = apiKey?.Trim();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            // If the user provided a MongoDB connection string (mongodb:// or mongodb+srv://)
            // treat this as a direct MongoDB connection fallback instead of the Data API.
            if (!string.IsNullOrWhiteSpace(_apiEndpoint) && (_apiEndpoint.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) || _apiEndpoint.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    _mongoClient = new MongoClient(_apiEndpoint);
                    // quick ping
                    var db = _mongoClient.GetDatabase(_database);
                    db.RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }").GetAwaiter().GetResult();
                    _useDirectMongo = true;
                    LastError = null;
                    AddinStatusLogger.Log("DataApiService", "Initialized with direct MongoDB connection");
                }
                catch (Exception ex)
                {
                    LastError = "Direct MongoDB init failed: " + ex.Message;
                    AddinStatusLogger.Error("DataApiService", LastError);
                }
            }
            else if (string.IsNullOrWhiteSpace(_apiEndpoint) || string.IsNullOrWhiteSpace(_apiKey))
            {
                LastError = "Missing API endpoint or key";
                AddinStatusLogger.Error("DataApiService", LastError);
            }
            else
            {
                AddinStatusLogger.Log("DataApiService", "Initialized with Data API");
            }
        }

        /// <summary>
        /// Insert user feedback (thumbs up/down) to MongoDB via Data API
        /// </summary>
        public async Task<bool> InsertFeedbackAsync(string runId, string prompt, string model, string plan, bool thumbUp)
        {
            // If initialized with a direct MongoDB connection string, insert directly
            if (_useDirectMongo)
            {
                try
                {
                    var db = _mongoClient.GetDatabase(_database);
                    var col = db.GetCollection<BsonDocument>("good_feedback");
                    var doc = new BsonDocument
                    {
                        { "ts", DateTime.UtcNow },
                        { "runId", runId ?? string.Empty },
                        { "prompt", prompt ?? string.Empty },
                        { "model", model ?? string.Empty },
                        { "plan", plan ?? string.Empty },
                        { "thumb", thumbUp ? "up" : "down" }
                    };
                    await col.InsertOneAsync(doc).ConfigureAwait(false);
                    LastError = null;
                    AddinStatusLogger.Log("DataApiService", "Feedback inserted via direct MongoDB");
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    AddinStatusLogger.Error("DataApiService", "InsertFeedbackAsync direct Mongo exception", ex);
                    return false;
                }
            }

            // Otherwise fall back to Data API
            if (string.IsNullOrWhiteSpace(_apiEndpoint) || string.IsNullOrWhiteSpace(_apiKey))
            {
                LastError = "Data API not configured";
                return false;
            }

            try
            {
                var document = new
                {
                    ts = DateTime.UtcNow,
                    runId = runId ?? string.Empty,
                    prompt = prompt ?? string.Empty,
                    model = model ?? string.Empty,
                    plan = plan ?? string.Empty,
                    thumb = thumbUp ? "up" : "down"
                };

                var payload = new
                {
                    dataSource = _dataSource,
                    database = _database,
                    collection = "good_feedback",
                    document = document
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiEndpoint}/action/insertOne");
                request.Headers.Add("api-key", _apiKey);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json"
                );

                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var result = JObject.Parse(content);
                        var insertedId = result["insertedId"]?.ToString();
                        LastError = null;
                        AddinStatusLogger.Log("DataApiService", $"Feedback inserted: {insertedId}");
                        return true;
                    }
                    else
                    {
                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        LastError = $"HTTP {response.StatusCode}: {content}";
                        AddinStatusLogger.Error("DataApiService", $"Insert failed: {LastError}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("DataApiService", "InsertFeedbackAsync exception", ex);
                return false;
            }
        }

        /// <summary>
        /// Get recent good feedback examples (for few-shot learning)
        /// </summary>
        public async Task<JArray> GetRecentFeedbackAsync(int limit = 10)
        {
            // Direct MongoDB retrieval when initialized with connection string
            if (_useDirectMongo)
            {
                try
                {
                    var db = _mongoClient.GetDatabase(_database);
                    var col = db.GetCollection<BsonDocument>("good_feedback");
                    var docs = await col.Find(FilterDefinition<BsonDocument>.Empty).Sort(Builders<BsonDocument>.Sort.Descending("ts")).Limit(limit).ToListAsync().ConfigureAwait(false);
                    var arr = new JArray();
                    foreach (var d in docs)
                    {
                        var jo = JObject.Parse(d.ToJson());
                        arr.Add(jo);
                    }
                    LastError = null;
                    return arr;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    AddinStatusLogger.Error("DataApiService", "GetRecentFeedbackAsync direct Mongo exception", ex);
                    return new JArray();
                }
            }

            if (string.IsNullOrWhiteSpace(_apiEndpoint) || string.IsNullOrWhiteSpace(_apiKey))
            {
                return new JArray();
            }

            try
            {
                var payload = new
                {
                    dataSource = _dataSource,
                    database = _database,
                    collection = "good_feedback",
                    limit = limit,
                    sort = new { ts = -1 }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiEndpoint}/action/find");
                request.Headers.Add("api-key", _apiKey);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json"
                );

                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var result = JObject.Parse(content);
                        LastError = null;
                        return result["documents"] as JArray ?? new JArray();
                    }
                    else
                    {
                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        LastError = content;
                        return new JArray();
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("DataApiService", "GetRecentFeedbackAsync exception", ex);
                return new JArray();
            }
        }

        /// <summary>
        /// Test the Data API connection
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                if (_useDirectMongo)
                {
                    try
                    {
                        var db = _mongoClient.GetDatabase(_database);
                        var col = db.GetCollection<BsonDocument>("good_feedback");
                        var doc = await col.Find(FilterDefinition<BsonDocument>.Empty).Limit(1).FirstOrDefaultAsync().ConfigureAwait(false);
                        LastError = null;
                        AddinStatusLogger.Log("DataApiService", "Direct MongoDB connection test successful");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        LastError = "Direct MongoDB connection test failed: " + ex.Message;
                        AddinStatusLogger.Error("DataApiService", LastError);
                        return false;
                    }
                }

                var payload = new
                {
                    dataSource = _dataSource,
                    database = _database,
                    collection = "good_feedback",
                    limit = 1
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiEndpoint}/action/findOne");
                request.Headers.Add("api-key", _apiKey);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json"
                );

                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        LastError = null;
                        AddinStatusLogger.Log("DataApiService", "Connection test successful");
                        return true;
                    }
                    else
                    {
                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        LastError = $"Connection test failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{content}";
                        AddinStatusLogger.Error("DataApiService", LastError);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AddinStatusLogger.Error("DataApiService", "Connection test exception", ex);
                return false;
            }
        }
    }
}
