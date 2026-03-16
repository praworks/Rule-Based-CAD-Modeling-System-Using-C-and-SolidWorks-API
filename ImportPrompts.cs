using System;
using System.IO;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;

class ImportPrompts
{
    static async System.Threading.Tasks.Task Main(string[] args)
    {
        try
        {
            var mongoUri = args.Length > 0
                ? args[0]
                : Environment.GetEnvironmentVariable("PROMPT_PRESET_MONGO_URI")
                    ?? Environment.GetEnvironmentVariable("MONGODB_URI")
                    ?? "mongodb://localhost:27017";
            var dbName = args.Length > 1
                ? args[1]
                : Environment.GetEnvironmentVariable("PROMPT_PRESET_MONGO_DB")
                    ?? Environment.GetEnvironmentVariable("MONGODB_DB")
                    ?? "TaskPaneAddin";
            var collectionName = args.Length > 2
                ? args[2]
                : Environment.GetEnvironmentVariable("PROMPT_PRESET_MONGO_COLL")
                    ?? "PromptPresetCollection";

            var mongoClient = new MongoClient(mongoUri);
            var database = mongoClient.GetDatabase(dbName);
            var collection = database.GetCollection<BsonDocument>(collectionName);

            // Read refactored prompts
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RefactoredPrompts.json");
            string jsonContent = File.ReadAllText(jsonPath);
            var prompts = JsonConvert.DeserializeObject<dynamic>(jsonContent);

            // Delete existing prompts
            var deleteResult = await collection.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty);
            Console.WriteLine($"✓ Deleted {deleteResult.DeletedCount} existing prompts");

            // Insert new prompts
            var documents = new System.Collections.Generic.List<BsonDocument>();
            foreach (var prompt in prompts)
            {
                documents.Add(BsonDocument.Parse(JsonConvert.SerializeObject(prompt)));
            }

            await collection.InsertManyAsync(documents);
            Console.WriteLine($"✓ Inserted {documents.Count} new refactored prompts");

            // Verify
            var count = await collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
            Console.WriteLine($"✓ Total prompts in collection: {count}");
            Console.WriteLine("\n✓ MongoDB PromptPresetCollection successfully updated!");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Error: {ex.Message}");
            System.Environment.Exit(1);
        }
    }
}
