using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICAD.Services
{
    [TestClass]
    public class PromptCatalogIntegrationTests
    {
        [TestMethod]
        public void PromptCatalog_Loads_FromTempWorkingDirectory_And_Provides_DecomposePrompt()
        {
            var orig = Directory.GetCurrentDirectory();
            var tmp = Path.Combine(Path.GetTempPath(), "aicad_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                Directory.SetCurrentDirectory(tmp);

                // Ensure no Config folder exists in the temp directory
                var cfg = Path.Combine(tmp, "Config");
                if (Directory.Exists(cfg))
                    Directory.Delete(cfg, true);

                // PromptCatalog should load from embedded resource or other fallbacks
                var decompose = PromptCatalog.GetSystemPrompt("decompose_system");
                Assert.IsFalse(string.IsNullOrWhiteSpace(decompose), "decompose_system prompt must be present even when working directory lacks Config files");

                // Ensure LlmPlanService default resolution for DECOMPOSE is also non-empty
                var method = typeof(LlmPlanService).GetMethod("GetDefaultSystemPromptForStage", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy | BindingFlags.Public);
                Assert.IsNotNull(method, "GetDefaultSystemPromptForStage method expected");
                var result = method.Invoke(null, new object[] { "DECOMPOSE" }) as string;
                Assert.IsFalse(string.IsNullOrWhiteSpace(result), "LlmPlanService default DECOMPOSE system prompt must be non-empty");
            }
            finally
            {
                try { Directory.SetCurrentDirectory(orig); } catch { }
                try { Directory.Delete(tmp, true); } catch { }
            }
        }
    }
}
