using System;

namespace AICAD.Services
{
    /// <summary>
    /// Provides secure retrieval for secrets used by the add-in.
    /// Lookup order:
    /// 1. Environment variable `GOOGLE_API_KEY`
    /// 2. Windows Credential Manager generic credential target `AI-CAD-GoogleApiKey`
    /// </summary>
    public static class SecretProvider
    {
        private const string EnvVarName = "GOOGLE_API_KEY";
        private const string CredTarget = "AI-CAD-GoogleApiKey";

        public static string GetGoogleApiKey()
        {
            // 1) Check environment variable first (good for CI and local dev environment configs)
            var env = Environment.GetEnvironmentVariable(EnvVarName, EnvironmentVariableTarget.Process)
                      ?? Environment.GetEnvironmentVariable(EnvVarName, EnvironmentVariableTarget.User)
                      ?? Environment.GetEnvironmentVariable(EnvVarName, EnvironmentVariableTarget.Machine);

            if (!string.IsNullOrWhiteSpace(env)) return env;

            // 2) Fallback to Windows Credential Manager (stored as generic secret)
            try
            {
                var cred = CredentialManager.ReadGenericSecret(CredTarget);
                if (!string.IsNullOrWhiteSpace(cred)) return cred;
            }
            catch
            {
                // swallow — nothing we can do here except return null
            }

            return null;
        }
    }
}
