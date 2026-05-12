using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

class Program
{
    static async Task<int> Main()
    {
        var conn = Environment.GetEnvironmentVariable("MONGO_CONN");
        if (string.IsNullOrWhiteSpace(conn))
        {
            Console.WriteLine("Environment variable MONGO_CONN is not set.");
            Console.WriteLine("Set MONGO_CONN to the connection string and re-run.");
            return 2;
        }

        var action = (Environment.GetEnvironmentVariable("MONGO_ACTION") ?? "list-dbs").Trim().ToLowerInvariant();
        var dbName = Environment.GetEnvironmentVariable("MONGO_DB");
        if (string.IsNullOrWhiteSpace(dbName))
        {
            dbName = "TaskPaneAddin";
        }

        try
        {
            var settings = MongoClientSettings.FromConnectionString(conn);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
            var client = new MongoClient(settings);
            var db = client.GetDatabase(dbName);
            var users = db.GetCollection<BsonDocument>("users");

            if (action == "grant-admin")
            {
                var email = (Environment.GetEnvironmentVariable("MONGO_EMAIL") ?? string.Empty).Trim();
                var displayName = (Environment.GetEnvironmentVariable("MONGO_DISPLAY_NAME") ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(email))
                {
                    Console.WriteLine("MONGO_EMAIL is required for MONGO_ACTION=grant-admin.");
                    return 2;
                }

                var filter = Builders<BsonDocument>.Filter.Eq("email", email);
                var update = Builders<BsonDocument>.Update
                    .Set("email", email)
                    .Set("isAdmin", true)
                    .Set("role", "admin")
                    .AddToSet("roles", "admin")
                    .Set("adminGrantedUtc", DateTime.UtcNow)
                    .Set("lastUpdatedUtc", DateTime.UtcNow)
                    .SetOnInsert("createdUtc", DateTime.UtcNow);

                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    update = update.Set("displayName", displayName);
                }

                var options = new FindOneAndUpdateOptions<BsonDocument>
                {
                    IsUpsert = true,
                    ReturnDocument = ReturnDocument.After
                };

                var result = await users.FindOneAndUpdateAsync(filter, update, options);
                Console.WriteLine("Admin grant successful.");
                Console.WriteLine(result.ToJson());
                return 0;
            }

            if (action == "enforce-single-admin")
            {
                var email = (Environment.GetEnvironmentVariable("MONGO_EMAIL") ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(email))
                {
                    Console.WriteLine("MONGO_EMAIL is required for MONGO_ACTION=enforce-single-admin.");
                    return 2;
                }

                var targetFilter = Builders<BsonDocument>.Filter.Eq("email", email);
                var targetUpdate = Builders<BsonDocument>.Update
                    .Set("email", email)
                    .Set("isAdmin", true)
                    .Set("role", "admin")
                    .Set("roles", new BsonArray { "admin" })
                    .Set("adminGrantedUtc", DateTime.UtcNow)
                    .Set("lastUpdatedUtc", DateTime.UtcNow)
                    .SetOnInsert("createdUtc", DateTime.UtcNow);

                await users.UpdateOneAsync(targetFilter, targetUpdate, new UpdateOptions { IsUpsert = true });

                var otherFilter = Builders<BsonDocument>.Filter.Ne("email", email);
                var otherUpdate = Builders<BsonDocument>.Update
                    .Set("isAdmin", false)
                    .Set("role", "user")
                    .Set("roles", new BsonArray { "user" })
                    .Set("lastUpdatedUtc", DateTime.UtcNow)
                    .Unset("adminGrantedUtc");

                var result = await users.UpdateManyAsync(otherFilter, otherUpdate);
                Console.WriteLine($"Single-admin policy enforced. Target={email}; other_users_updated={result.ModifiedCount}");
                return 0;
            }

            if (action == "count-users")
            {
                var count = await users.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
                Console.WriteLine($"users_count={count}");
                return 0;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var dbNamesCursor = await client.ListDatabaseNamesAsync(cts.Token);

            Console.WriteLine("Connection successful. Databases:");
            await dbNamesCursor.ForEachAsync(name => Console.WriteLine(" - " + name), cts.Token);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Connection failed:");
            Console.WriteLine(ex.ToString());
            return 1;
        }
    }
}
