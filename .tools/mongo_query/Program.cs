using System;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;
using Newtonsoft.Json;

if (args.Length < 3)
{
    Console.WriteLine("Usage: dotnet run --project .tools/mongo_query -- <MONGO_URI> <DB> <COLLECTION> [limit]");
    return 1;
}

var uri = args[0];
var dbName = args[1];
var collName = args[2];
var limit = args.Length >=4 && int.TryParse(args[3], out var l) ? l : 5;

try
{
    var settings = MongoClientSettings.FromConnectionString(uri);
    settings.ServerApi = new ServerApi(ServerApiVersion.V1);
    var client = new MongoClient(settings);
    var db = client.GetDatabase(dbName);
    var coll = db.GetCollection<BsonDocument>(collName);

    var docs = await coll.Find(Builders<BsonDocument>.Filter.Empty).Limit(limit).ToListAsync();
    Console.WriteLine($"Fetched {docs.Count} document(s) from {dbName}.{collName} (limit={limit})\n");
    int i = 0;
    foreach (var d in docs)
    {
        i++;
        // Use strict JSON writer settings so ObjectId and dates are represented as JSON-friendly $oid/$date fields
        var js = d.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.Strict });
        // Pretty-print the strict JSON
        try
        {
            var parsed = JsonConvert.DeserializeObject(js);
            var pretty = JsonConvert.SerializeObject(parsed, Formatting.Indented);
            Console.WriteLine($"--- Document {i} ---\n{pretty}\n");
        }
        catch
        {
            // Fallback: print raw JSON
            Console.WriteLine($"--- Document {i} (raw) ---\n{js}\n");
        }
    }
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("Query failed: " + ex);
    return 2;
}