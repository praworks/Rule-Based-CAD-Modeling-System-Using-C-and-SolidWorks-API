using System;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: dotnet run -- <mongo-uri> [dbName] [--dry-run]");
            return 2;
        }

        var mongoUri = args[0];
        var dbName = args.Length >= 2 ? args[1] : "TaskPaneAddin";
        var dryRun = Array.Exists(args, a => a == "--dry-run");

        Console.WriteLine($"Connecting to: {mongoUri}, DB: {dbName}, dryRun={dryRun}");

        var client = new MongoClient(mongoUri);
        var db = client.GetDatabase(dbName);

        // Detect available source collections
        var existing = await db.ListCollectionNames().ToListAsync();
        string[] candidates = new[] { "Feedback", "feedback2", "feedback", "Feedback2" };
        var sources = new System.Collections.Generic.List<string>();
        foreach (var c in candidates)
            if (existing.Contains(c)) sources.Add(c);

        if (sources.Count == 0)
        {
            Console.WriteLine("No source feedback collections found (Feedback / feedback2). Nothing to do.");
            return 0;
        }

        var targetName = "run_feedback";
        var target = db.GetCollection<BsonDocument>(targetName);

        foreach (var s in sources)
        {
            var src = db.GetCollection<BsonDocument>(s);
            var cnt = await src.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
            Console.WriteLine($"Found {cnt} documents in {s} -> will merge into {targetName}");

            var cursor = await src.Find(FilterDefinition<BsonDocument>.Empty).ToCursorAsync();
            long processed = 0;
            while (await cursor.MoveNextAsync())
            {
                foreach (var doc in cursor.Current)
                {
                    // Build a dedupe filter using common fields
                    var filters = new System.Collections.Generic.List<FilterDefinition<BsonDocument>>();
                    BsonValue runKey = doc.Contains("run_key") ? doc["run_key"] : BsonNull.Value;
                    BsonValue ts = doc.Contains("ts") ? doc["ts"] : BsonNull.Value;
                    BsonValue thumb = doc.Contains("thumb") ? doc["thumb"] : BsonNull.Value;
                    BsonValue comment = doc.Contains("comment") ? doc["comment"] : BsonNull.Value;

                    filters.Add(Builders<BsonDocument>.Filter.Eq("run_key", runKey));
                    filters.Add(Builders<BsonDocument>.Filter.Eq("ts", ts));
                    filters.Add(Builders<BsonDocument>.Filter.Eq("thumb", thumb));
                    filters.Add(Builders<BsonDocument>.Filter.Eq("comment", comment));

                    var filter = Builders<BsonDocument>.Filter.And(filters);

                    // annotate source
                    doc["_source_collection"] = s;

                    if (dryRun)
                    {
                        var exists = await target.Find(filter).Limit(1).AnyAsync();
                        if (!exists) Console.WriteLine($"[DRY] would insert doc from {s} run_key={runKey}");
                    }
                    else
                    {
                        await target.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true });
                    }
                    processed++;
                }
            }
            Console.WriteLine($"Processed {processed} docs from {s}");
        }

        if (!dryRun)
        {
            Console.WriteLine("Creating recommended indexes on run_feedback...");
            try
            {
                await target.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("run_key")));
                await target.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Descending("ts")));
                await target.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("installation_id")));
                await target.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("event_id"), new CreateIndexOptions { Unique = true, Sparse = true }));
                Console.WriteLine("Indexes created.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Index creation error: " + ex.Message);
            }
        }

        Console.WriteLine("Migration completed.");
        return 0;
    }
}
