using System;
using System.Threading;
using System.Threading.Tasks;
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

        try
        {
            var settings = MongoClientSettings.FromConnectionString(conn);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
            var client = new MongoClient(settings);

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
