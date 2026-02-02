using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICAD.Services
{
    [TestClass]
    public class PromptCatalogValidationTests
    {
        [TestMethod]
        public void ExecutePrompt_EncodesClarificationAndOps()
        {
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
        public void DecomposePrompt_ReturnsDescriptionAndFeatures()
        {
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
    }
}
