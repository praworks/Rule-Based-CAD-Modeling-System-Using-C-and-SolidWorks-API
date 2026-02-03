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
        public void PromptCatalog_Loads_FromExplicitPath()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "aicad_integration_" + Guid.NewGuid().ToString("N"));
            var cfgDir = Path.Combine(tmp, "Config");
            Directory.CreateDirectory(cfgDir);
            var path = Path.Combine(cfgDir, "PromptCatalog.json");
            var json = @"{
  ""systemPrompts"": {
    ""decompose_system"": ""Return JSON with features, needs_description, question; no steps."",
    ""execute_system"": ""Return steps array with op; include clarification_needed, feature_index, feature_type, questions; never command.""
  },
  ""templates"": {
    ""decompose_template"": ""{systemPrompt} {userRequest}"",
    ""execute_template"": ""{systemPrompt} {featureTask}""
  }
}";
            File.WriteAllText(path, json);
            try
            {
                PromptCatalog.ResetForTests(path);
                PromptCatalog.EnsureCatalogLoaded();
                Assert.IsFalse(string.IsNullOrWhiteSpace(PromptCatalog.GetSystemPrompt("decompose_system")), "decompose_system should load from explicit path");
            }
            finally
            {
                PromptCatalog.ResetForTests();
                try { Directory.Delete(tmp, true); } catch { }
            }
        }
    }
}
