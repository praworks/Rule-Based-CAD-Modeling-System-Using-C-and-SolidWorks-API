using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

class Program
{
	static async Task<int> Main(string[] args)
	{
		try
		{
			if (args.Length == 0)
			{
				Console.WriteLine("Usage: dotnet run -- <mongo-uri>");
				return 2;
			}
			var uri = args[0];
			Console.WriteLine("Connecting to: " + uri);
			var client = new MongoClient(uri);
			var db = client.GetDatabase("TaskPaneAddin");

			var desired = new List<string> { "SW", "good_feedback", "runs", "steps", "run_feedback" };

			var existingNames = new HashSet<string>(await db.ListCollectionNames().ToListAsync(), StringComparer.OrdinalIgnoreCase);
			Console.WriteLine("Existing collections: " + string.Join(", ", existingNames));

			foreach (var name in desired)
			{
				if (existingNames.Contains(name))
				{
					Console.WriteLine($"Collection '{name}' already exists.");
				}
				else
				{
					Console.WriteLine($"Creating collection '{name}'...");
					await db.CreateCollectionAsync(name);
					var col = db.GetCollection<BsonDocument>(name);
					var doc = new BsonDocument
					{
						{ "test", "ok" },
						{ "createdAt", BsonDateTime.Create(DateTime.UtcNow) }
					};
					await col.InsertOneAsync(doc);
					Console.WriteLine($"Created and inserted test doc into '{name}'.");
				}
			}

			Console.WriteLine("Done.");
			return 0;
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error: " + ex.ToString());
			return 1;
		}
	}
}
// Program complete
