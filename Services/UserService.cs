using System;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Google.Apis.Auth;

namespace AICAD.Services
{
    public static class UserService
    {
        // Upsert user by email. If no email provided, do nothing.
        public static async Task<bool> UpsertUserAsync(string email, string displayName, string idToken)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;

            try
            {
                var conn = Environment.GetEnvironmentVariable("MONGODB_URI", EnvironmentVariableTarget.User)
                           ?? Environment.GetEnvironmentVariable("MONGODB_URI")
                           ?? Environment.GetEnvironmentVariable("MONGO_LOG_CONN")
                           ?? string.Empty;

                if (string.IsNullOrWhiteSpace(conn)) return false;

                var dbName = Environment.GetEnvironmentVariable("MONGODB_DB", EnvironmentVariableTarget.User) ?? "TaskPaneAddin";

                var client = new MongoClient(conn);
                var db = client.GetDatabase(dbName);
                var users = db.GetCollection<BsonDocument>("users");

                var filter = Builders<BsonDocument>.Filter.Eq("email", email);
                var update = Builders<BsonDocument>.Update
                    .Set("email", email)
                    .Set("displayName", displayName ?? string.Empty)
                    .Set("lastSignInUtc", DateTime.UtcNow)
                    .Set("idToken", idToken ?? string.Empty)
                    .SetOnInsert("createdUtc", DateTime.UtcNow);

                var options = new UpdateOptions { IsUpsert = true };
                await users.UpdateOneAsync(filter, update, options).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Validate an id_token with Google's library, then create or update the user
        public static async Task<BsonDocument> GetOrCreateFromIdTokenAsync(string idToken)
        {
            if (string.IsNullOrWhiteSpace(idToken)) return null;

            try
            {
                GoogleJsonWebSignature.Payload payload = null;
                try
                {
                    var settings = new GoogleJsonWebSignature.ValidationSettings();
                    // If client id is available, require it in audience
                    var cfg = GoogleOAuthConfig.Load();
                    if (cfg != null && !string.IsNullOrWhiteSpace(cfg.ClientId))
                    {
                        settings.Audience = new[] { cfg.ClientId };
                    }
                    payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings).ConfigureAwait(false);
                }
                catch
                {
                    return null; // invalid token
                }

                if (payload == null || string.IsNullOrWhiteSpace(payload.Email)) return null;

                var conn = Environment.GetEnvironmentVariable("MONGODB_URI", EnvironmentVariableTarget.User)
                           ?? Environment.GetEnvironmentVariable("MONGODB_URI")
                           ?? Environment.GetEnvironmentVariable("MONGO_LOG_CONN")
                           ?? string.Empty;

                if (string.IsNullOrWhiteSpace(conn)) return null;

                var dbName = Environment.GetEnvironmentVariable("MONGODB_DB", EnvironmentVariableTarget.User) ?? "TaskPaneAddin";

                var client = new MongoClient(conn);
                var db = client.GetDatabase(dbName);
                var users = db.GetCollection<BsonDocument>("users");

                var email = payload.Email;
                var name = payload.Name ?? payload.GivenName ?? payload.FamilyName ?? string.Empty;

                var filter = Builders<BsonDocument>.Filter.Eq("email", email);
                var update = Builders<BsonDocument>.Update
                    .Set("email", email)
                    .Set("displayName", name)
                    .Set("lastSignInUtc", DateTime.UtcNow)
                    .Set("idToken", idToken ?? string.Empty)
                    .Set("googleSub", payload.Subject ?? string.Empty)
                    .Set("picture", payload.Picture ?? string.Empty)
                    .SetOnInsert("createdUtc", DateTime.UtcNow);

                var options = new FindOneAndUpdateOptions<BsonDocument>
                {
                    IsUpsert = true,
                    ReturnDocument = ReturnDocument.After
                };

                var result = await users.FindOneAndUpdateAsync(filter, update, options).ConfigureAwait(false);
                return result;
            }
            catch
            {
                return null;
            }
        }
    }
}
