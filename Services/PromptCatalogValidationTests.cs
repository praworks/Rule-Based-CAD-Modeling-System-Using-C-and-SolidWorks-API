using System;
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
    }
}
