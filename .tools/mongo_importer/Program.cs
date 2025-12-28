using System;
using System.IO;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;

Console.WriteLine("MongoImporter starting...");
if (args.Length < 4)
{
    Console.WriteLine("Usage: dotnet run -- <MONGO_URI> <DB_NAME> <COLLECTION_NAME> <JSON_FILE_PATH>");
    return 1;
}

var uri = args[0];
var dbName = args[1];
var collName = args[2];
var filePath = args[3];

if (!File.Exists(filePath))
{
    Console.WriteLine($"ERROR: file not found: {filePath}");
    return 2;
}

try
{
    var txt = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
    var arr = JArray.Parse(txt);
    var clientSettings = MongoClientSettings.FromConnectionString(uri);
    clientSettings.ServerApi = new ServerApi(ServerApiVersion.V1);
    var client = new MongoClient(clientSettings);
    var db = client.GetDatabase(dbName);
    var coll = db.GetCollection<BsonDocument>(collName);

    Console.WriteLine($"Connected; replacing collection '{collName}' in DB '{dbName}' with {arr.Count} documents...");

    // Drop collection if exists
    try { await db.DropCollectionAsync(collName); } catch { }

    var docs = new BsonDocument[arr.Count];
    for (int i = 0; i < arr.Count; i++)
    {
        var j = arr[i];
        var json = j.ToString();
        docs[i] = BsonDocument.Parse(json);
    }
    if (docs.Length > 0)
    {
        await coll.InsertManyAsync(docs);
    }

    var count = await coll.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
    Console.WriteLine($"Import complete. Document count in collection: {count}");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("Import failed: " + ex);
    return 3;
}