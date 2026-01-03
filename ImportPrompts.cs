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
            // MongoDB connection
            var mongoClient = new MongoClient("mongodb://localhost:27017");
            var database = mongoClient.GetDatabase("TaskPaneAddin");
            var collection = database.GetCollection<BsonDocument>("PromptPresetCollection");

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
