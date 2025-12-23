using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Threading.Tasks;

namespace AICAD.Services
{
    /// <summary>
    /// Lightweight Mongo logger backed by MongoDB.Driver.
    /// Establishes a client with Stable API v1, performs an initial ping to verify
    /// connectivity, and exposes LastError for the UI to surface diagnostics.
    /// </summary>
    internal class MongoLogger
    {
        private readonly string _connectionString;
        private readonly string _dbName;
        private readonly string _collectionName;
    private string _lastError;

    private IMongoCollection<BsonDocument> _collection;
    private IMongoDatabase _db;

    private long _lastPingMs;
    private string _serverVersion;

    public string LastError => _lastError;
    public long LastPingMs => _lastPingMs;

        public MongoLogger(string connectionString,
                           string databaseName = "TaskPaneAddin",
                           string collectionName = "SW")
        {
            // Enforce application name for observability
            _connectionString = EnsureAppName(connectionString, "Cluster2");
            _dbName = databaseName;
            _collectionName = collectionName;

            try
            {
                if (!string.IsNullOrWhiteSpace(_connectionString))
                {
                    var settings = MongoClientSettings.FromConnectionString(_connectionString);
                    // Align with Node sample: Stable API v1 + strict + deprecationErrors
                    settings.ServerApi = new ServerApi(ServerApiVersion.V1, strict: true, deprecationErrors: true);
                    // Make initial server selection/ping snappy
                    settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
                    settings.ConnectTimeout = TimeSpan.FromSeconds(5);

                    var client = new MongoClient(settings);

                    // Optional: ping admin to confirm connectivity (like Node sample)
                    try
                    {
                        var admin = client.GetDatabase("admin");
                        var pingCmd = new BsonDocument("ping", 1);
                        var sw = Stopwatch.StartNew();
                        admin.RunCommand<BsonDocument>(pingCmd);
                        sw.Stop();
                        _lastPingMs = sw.ElapsedMilliseconds;
                        try
                        {
                            var buildInfo = admin.RunCommand<BsonDocument>(new BsonDocument("buildInfo", 1));
                            _serverVersion = buildInfo.GetValue("version", "").AsString;
                        }
                        catch { /* buildInfo may be restricted; ignore */ }
                        try { AddinStatusLogger.Log("MongoLogger", $"Ping success ping_ms={_lastPingMs} server_version={_serverVersion}"); } catch { }
                    }
                    catch (Exception ex)
                    {
                        // Treat ping failure as not available for logging
                        _collection = null;
                        _lastError = ex.ToString();
                        try { AddinStatusLogger.Error("MongoLogger", "Ping failed", ex); } catch { }
                        return;
                    }

                    _db = client.GetDatabase(_dbName);
                    _collection = _db.GetCollection<BsonDocument>(_collectionName);
                    // Ensure collection exists (creates on first insert if allowed). Attempt explicit creation
                    try
                    {
                        var exists = _db.ListCollectionNames().ToList().Contains(_collectionName);
                        if (!exists)
                        {
                            var options = new CreateCollectionOptions();
                            _db.CreateCollection(_collectionName, options);
                            AddinStatusLogger.Log("MongoLogger", $"Created missing collection={_collectionName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Creation may fail due to permissions; ignore but log
                        try { AddinStatusLogger.Error("MongoLogger", "CreateCollection failed", ex); } catch { }
                    }
                    _lastError = null;
                    try { AddinStatusLogger.Log("MongoLogger", $"Connected to MongoDB database={_dbName} collection={_collectionName}"); } catch { }
                }
            }
            catch (Exception ex)
            {
                _collection = null;
                _lastError = ex.ToString();
                try { AddinStatusLogger.Error("MongoLogger", "Constructor failed", ex); } catch { }
            }
        }

    public bool IsAvailable => _collection != null;

    public async Task<bool> LogAsync(string prompt,
                     string reply,
                     string model,
                     TimeSpan llmDuration,
                     TimeSpan totalDuration,
                     string error = null)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                _lastError = "Missing Mongo connection string (MONGO_LOG_CONN)";
                return false;
            }
            if (!IsAvailable)
            {
                if (string.IsNullOrWhiteSpace(_lastError)) _lastError = "Mongo unavailable (connection/ping failed)";
                return false;
            }

            try
            {
                var doc = new BsonDocument
                {
                    { "timestamp", DateTime.UtcNow },
                    { "prompt", prompt ?? string.Empty },
                    { "reply", reply ?? string.Empty },
                    { "model", model ?? string.Empty },
                    { "llmMs", (long)llmDuration.TotalMilliseconds },
                    { "totalMs", (long)totalDuration.TotalMilliseconds }
                };
                if (!string.IsNullOrWhiteSpace(error)) doc.Add("error", error);

                await _collection.InsertOneAsync(doc).ConfigureAwait(false);
                try { AddinStatusLogger.Log("MongoLogger", $"Inserted log doc to { _collectionName } (prompt_len={prompt?.Length ?? 0})"); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.ToString();
                try { AddinStatusLogger.Error("MongoLogger", "InsertAsync failed", ex); } catch { }
                return false;
            }
        }

        private static string EnsureAppName(string uri, string appName)
        {
            if (string.IsNullOrWhiteSpace(uri)) return uri;
            try
            {
                if (uri.IndexOf("appName=", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Regex.Replace(uri, "appName=[^&#]+", $"appName={appName}");
                return uri.Contains("?") ? ($"{uri}&appName={appName}") : ($"{uri}?appName={appName}");
            }
            catch { return uri; }
        }
    }

    internal static class MongoLoggerExtensions
    {
        public static IEnumerable<string> GetDebugInfo(this MongoLogger logger)
        {
            var lines = new List<string>();
            try
            {
                var csField = typeof(MongoLogger).GetField("_connectionString", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var dbField = typeof(MongoLogger).GetField("_dbName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var colField = typeof(MongoLogger).GetField("_collectionName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var pingField = typeof(MongoLogger).GetField("_lastPingMs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var verField = typeof(MongoLogger).GetField("_serverVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                var conn = csField?.GetValue(logger) as string ?? string.Empty;
                var db = dbField?.GetValue(logger) as string ?? string.Empty;
                var col = colField?.GetValue(logger) as string ?? string.Empty;
                var pingMs = (long)(pingField?.GetValue(logger) ?? (long)0);
                var ver = verField?.GetValue(logger) as string ?? string.Empty;

                var srv = conn.IndexOf("+srv", StringComparison.OrdinalIgnoreCase) >= 0 ? "yes" : "no";
                string hosts = "";
                try
                {
                    var url = MongoUrl.Create(conn);
                    if (url.Servers != null)
                    {
                        hosts = string.Join(",", url.Servers.Select(s => string.IsNullOrEmpty(s.Host) ? s.ToString() : ($"{s.Host}:{s.Port}")));
                    }
                    if (!string.IsNullOrWhiteSpace(url.ApplicationName))
                    {
                        lines.Add($"appName: {url.ApplicationName}");
                    }
                }
                catch { }

                // Redact credentials if present
                string redacted = conn;
                try
                {
                    redacted = System.Text.RegularExpressions.Regex.Replace(conn ?? string.Empty, @"://([^:]+):[^@]+@", @"://$1:***@");
                }
                catch { }

                lines.Add($"conn: {(string.IsNullOrWhiteSpace(redacted) ? "<none>" : redacted)}");
                if (!string.IsNullOrWhiteSpace(hosts)) lines.Add($"hosts: {hosts}");
                lines.Add($"srv: {srv}");
                lines.Add($"db: {db}; col: {col}");
                lines.Add($"ping_ms: {pingMs}");
                if (!string.IsNullOrWhiteSpace(ver)) lines.Add($"server_version: {ver}");
            }
            catch { }
            return lines;
        }

        public static async Task<bool> InsertAsync(this MongoLogger logger, string collection, BsonDocument doc)
        {
            var field = typeof(MongoLogger).GetField("_db", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var db = (IMongoDatabase)field?.GetValue(logger);
            var lastErrProp = typeof(MongoLogger).GetField("_lastError", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (db == null)
            {
                lastErrProp?.SetValue(logger, "Mongo unavailable (no database)");
                return false;
            }
            try
            {
                var col = db.GetCollection<BsonDocument>(collection);
                await col.InsertOneAsync(doc).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                lastErrProp?.SetValue(logger, ex.ToString());
                return false;
            }
        }
    }
}
