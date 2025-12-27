using System;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;

if (args.Length < 5)
{
    Console.WriteLine("Usage: dotnet run --project .tools/mongo_copy -- <MONGO_URI> <SRC_DB> <SRC_COLL> <TGT_DB> <TGT_COLL>");
    return 1;
}

var uri = args[0];
var srcDb = args[1];
var srcColl = args[2];
var tgtDb = args[3];
var tgtColl = args[4];

try
{
    var settings = MongoClientSettings.FromConnectionString(uri);
    settings.ServerApi = new ServerApi(ServerApiVersion.V1);
    var client = new MongoClient(settings);
    var sdb = client.GetDatabase(srcDb);
    var tdb = client.GetDatabase(tgtDb);
    var sColl = sdb.GetCollection<BsonDocument>(srcColl);
    var tColl = tdb.GetCollection<BsonDocument>(tgtColl);

    var docs = await sColl.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
    Console.WriteLine($"Fetched {docs.Count} documents from {srcDb}.{srcColl}");

    // Drop target collection
    try { await tdb.DropCollectionAsync(tgtColl); Console.WriteLine($"Dropped existing {tgtDb}.{tgtColl}"); } catch { }

    if (docs.Count > 0)
    {
        await tColl.InsertManyAsync(docs);
        var cnt = await tColl.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        Console.WriteLine($"Inserted {cnt} documents into {tgtDb}.{tgtColl}");
    }
    else
    {
        Console.WriteLine("No documents to insert.");
    }
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("Copy failed: " + ex);
    return 2;
}