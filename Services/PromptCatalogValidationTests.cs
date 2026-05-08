using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICAD.Services
{
    [TestClass]
    public class PromptCatalogValidationTests
    {
        [TestCleanup]
        public void ResetCatalog() => PromptCatalog.ResetForTests();

        [TestMethod]
        public void ExecutePrompt_EncodesClarificationAndOps()
        {
            PromptCatalog.EnsureCatalogLoaded();
            var execute = PromptCatalog.GetSystemPrompt("execute_system");
            var decompose = PromptCatalog.GetSystemPrompt("decompose_system");

            Assert.IsFalse(string.IsNullOrWhiteSpace(execute), "execute_system prompt must be present");
            Assert.IsFalse(string.IsNullOrWhiteSpace(decompose), "decompose_system prompt must be present");
            Assert.AreNotEqual(decompose, execute, "execute_system must not be identical to decompose_system");

            Assert.IsTrue(execute.Contains("clarification_needed"), "execute_system should mention clarification structure");
            Assert.IsTrue(execute.Contains("\"feature_index\""), "execute_system should reference the current feature index");
            Assert.IsTrue(execute.Contains("\"steps\""), "execute_system must require \"steps\"");
            Assert.IsTrue(execute.ToLowerInvariant().Contains("\"op\""), "execute_system must require step objects to use the 'op' field");
            Assert.IsTrue(execute.Contains("\"questions\""), "execute_system should mention \"questions\" for clarifications");
        }

        [TestMethod]
        public void FeaturePromptPath_IsResolvedToPromptBody()
        {
            PromptCatalog.EnsureCatalogLoaded();
            var featurePrompt = PromptCatalog.GetSystemPromptForFeature("execute_extrude");

            Assert.IsFalse(string.IsNullOrWhiteSpace(featurePrompt), "execute_extrude prompt must be present");
            Assert.IsFalse(
                featurePrompt.Trim().Equals("prompts/execute/extrude.txt", StringComparison.OrdinalIgnoreCase),
                "Feature prompt lookup must resolve file paths to file content.");
            Assert.IsTrue(
                featurePrompt.IndexOf("clarification_needed", StringComparison.OrdinalIgnoreCase) >= 0,
                "Resolved execute_extrude prompt should include the clarification contract.");
        }

        [TestMethod]
        public void FilletFeaturePromptPath_IsResolvedToPromptBody()
        {
            PromptCatalog.EnsureCatalogLoaded();
            var filletPrompt = PromptCatalog.GetSystemPromptForFeature("execute_fillet");

            Assert.IsFalse(string.IsNullOrWhiteSpace(filletPrompt), "execute_fillet prompt must be present");
            Assert.IsFalse(
                filletPrompt.Trim().Equals("prompts/execute/fillet.txt", StringComparison.OrdinalIgnoreCase),
                "Feature prompt lookup must resolve file paths to file content.");
            Assert.IsTrue(
                filletPrompt.IndexOf("\"feature_type\": \"fillet\"", StringComparison.OrdinalIgnoreCase) >= 0,
                "Resolved execute_fillet prompt should include the fillet clarification contract.");
        }

        [TestMethod]
        public void ChamferFeaturePromptPath_IsResolvedToPromptBody()
        {
            PromptCatalog.EnsureCatalogLoaded();
            var chamferPrompt = PromptCatalog.GetSystemPromptForFeature("execute_chamfer");

            Assert.IsFalse(string.IsNullOrWhiteSpace(chamferPrompt), "execute_chamfer prompt must be present");
            Assert.IsFalse(
                chamferPrompt.Trim().Equals("prompts/execute/chamfer.txt", StringComparison.OrdinalIgnoreCase),
                "Feature prompt lookup must resolve file paths to file content.");
            Assert.IsTrue(
                chamferPrompt.IndexOf("\"feature_type\": \"chamfer\"", StringComparison.OrdinalIgnoreCase) >= 0,
                "Resolved execute_chamfer prompt should include the chamfer clarification contract.");
        }

        [TestMethod]
        public void RevolveFeaturePromptPath_IsResolvedToPromptBody()
        {
            PromptCatalog.EnsureCatalogLoaded();
            var revolvePrompt = PromptCatalog.GetSystemPromptForFeature("execute_revolve");

            Assert.IsFalse(string.IsNullOrWhiteSpace(revolvePrompt), "execute_revolve prompt must be present");
            Assert.IsFalse(
                revolvePrompt.Trim().Equals("prompts/execute/revolve.txt", StringComparison.OrdinalIgnoreCase),
                "Feature prompt lookup must resolve file paths to file content.");
            Assert.IsTrue(
                revolvePrompt.IndexOf("\"feature_type\": \"revolve\"", StringComparison.OrdinalIgnoreCase) >= 0,
                "Resolved execute_revolve prompt should include the revolve clarification contract.");
        }

        [TestMethod]
        public void DecomposePrompt_ReturnsDescriptionAndFeatures()
        {
            PromptCatalog.EnsureCatalogLoaded();
            var decompose = PromptCatalog.GetSystemPrompt("decompose_system");
            Assert.IsFalse(string.IsNullOrWhiteSpace(decompose), "decompose_system prompt must be present");
            Assert.IsTrue(decompose.Contains("needs_description"), "decompose_system must mention needs_description");
            Assert.IsTrue(decompose.Contains("\"features\""), "decompose_system must include a features array");
            Assert.IsFalse(decompose.Contains("\"steps\""), "decompose_system must not mention steps");
            Assert.IsTrue(decompose.Contains("\"question\""), "decompose_system must include a question field for clarification");
        }

        [TestMethod]
        public void ExecuteStage_UsesExecutePrompts_NotDecompose()
        {
            PromptCatalog.EnsureCatalogLoaded();
            var execute = PromptCatalog.GetSystemPrompt("execute_system");
            var decompose = PromptCatalog.GetSystemPrompt("decompose_system");
            var template = PromptCatalog.GetTemplate("execute_template");
            Assert.IsFalse(string.IsNullOrWhiteSpace(execute), "execute_system prompt must be present");
            Assert.IsFalse(string.IsNullOrWhiteSpace(template), "execute_template must be present");
            Assert.AreNotEqual(decompose, execute, "execute_system must not be identical to decompose_system");
            Assert.IsFalse(execute.ToLowerInvariant().Contains("needs_description"), "execute_system must not request decomposition fields");

            var method = typeof(LlmPlanService).GetMethod("GetDefaultSystemPromptForStage", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy | BindingFlags.Public);
            Assert.IsNotNull(method, "GetDefaultSystemPromptForStage must exist for fallback resolution checks");
            var defaultExecute = method.Invoke(null, new object[] { "EXECUTE" }) as string;
            Assert.IsFalse(string.IsNullOrWhiteSpace(defaultExecute), "Default EXECUTE system prompt must resolve to a non-empty value");
            Assert.AreEqual(execute, defaultExecute, "Default EXECUTE prompt should resolve to execute_system prompt, not decompose_system");
        }

        [TestMethod]
        public void MissingFile_ThrowsFatal()
        {
            var dir = Path.Combine(Path.GetTempPath(), "aicad_test_missing_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "Config", "PromptCatalog.json");
            try
            {
                PromptCatalog.ResetForTests(path);
                Assert.ThrowsException<InvalidOperationException>(() => PromptCatalog.EnsureCatalogLoaded(), "Missing PromptCatalog.json should throw");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [TestMethod]
        public void InvalidJson_ThrowsFatal()
        {
            var dir = Path.Combine(Path.GetTempPath(), "aicad_test_invalid_" + Guid.NewGuid().ToString("N"));
            var cfgDir = Path.Combine(dir, "Config");
            Directory.CreateDirectory(cfgDir);
            var path = Path.Combine(cfgDir, "PromptCatalog.json");
            File.WriteAllText(path, "{ \"systemPrompts\": ");
            try
            {
                PromptCatalog.ResetForTests(path);
                Assert.ThrowsException<InvalidOperationException>(() => PromptCatalog.EnsureCatalogLoaded(), "Invalid JSON must throw");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [TestMethod]
        public void MissingRequiredKey_ThrowsFatal()
        {
            var dir = Path.Combine(Path.GetTempPath(), "aicad_test_missingkey_" + Guid.NewGuid().ToString("N"));
            var cfgDir = Path.Combine(dir, "Config");
            Directory.CreateDirectory(cfgDir);
            var path = Path.Combine(cfgDir, "PromptCatalog.json");
            var json = "{ \"systemPrompts\": { \"decompose_system\": \"needs_description features question\" }, \"templates\": { \"decompose_template\": \"{systemPrompt}\", \"execute_template\": \"{systemPrompt}\" } }";
            File.WriteAllText(path, json);
            try
            {
                PromptCatalog.ResetForTests(path);
                Assert.ThrowsException<InvalidOperationException>(() => PromptCatalog.EnsureCatalogLoaded(), "Missing execute_system must throw");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [TestMethod]
        public void EmptyPromptString_ThrowsFatal()
        {
            var dir = Path.Combine(Path.GetTempPath(), "aicad_test_empty_" + Guid.NewGuid().ToString("N"));
            var cfgDir = Path.Combine(dir, "Config");
            Directory.CreateDirectory(cfgDir);
            var path = Path.Combine(cfgDir, "PromptCatalog.json");
            var json = "{ \"systemPrompts\": { \"decompose_system\": \"needs_description features question\", \"execute_system\": \"\" }, \"templates\": { \"decompose_template\": \"{systemPrompt}\", \"execute_template\": \"{systemPrompt}\" } }";
            File.WriteAllText(path, json);
            try
            {
                PromptCatalog.ResetForTests(path);
                Assert.ThrowsException<InvalidOperationException>(() => PromptCatalog.EnsureCatalogLoaded(), "Empty execute_system must throw");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [TestMethod]
        public void ValidFile_LoadsSuccessfully()
        {
            var dir = Path.Combine(Path.GetTempPath(), "aicad_test_valid_" + Guid.NewGuid().ToString("N"));
            var cfgDir = Path.Combine(dir, "Config");
            Directory.CreateDirectory(cfgDir);
            var path = Path.Combine(cfgDir, "PromptCatalog.json");
            var json = @"{
  ""systemPrompts"": {
    ""decompose_system"": ""Return JSON with features, needs_description, question; no steps or op."",
    ""execute_system"": ""Return steps array with op; include clarification_needed, feature_index, feature_type, questions; never command.""
  },
  ""systemPromptsByFeature"": {
    ""execute_extrude"": ""Extrude prompt with steps and op only.""
  },
  ""templates"": {
    ""decompose_template"": ""{systemPrompt} {userRequest}"",
    ""execute_template"": ""{systemPrompt} {allowedOps} {featureTask}""
  }
}";
            File.WriteAllText(path, json);
            try
            {
                PromptCatalog.ResetForTests(path);
                PromptCatalog.EnsureCatalogLoaded();
                Assert.IsFalse(string.IsNullOrWhiteSpace(PromptCatalog.GetSystemPrompt("decompose_system")));
                Assert.IsFalse(string.IsNullOrWhiteSpace(PromptCatalog.GetSystemPrompt("execute_system")));
                Assert.IsFalse(string.IsNullOrWhiteSpace(PromptCatalog.GetTemplate("execute_template")));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
