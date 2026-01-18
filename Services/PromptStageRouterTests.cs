using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICAD.Services
{
    [TestClass]
    public class PromptStageRouterTests
    {
        [TestMethod]
        public void StageKeys_MatchExpectedNames()
        {
            var classify = PromptStageRouter.GetKeys("CLASSIFY");
            Assert.AreEqual("CLASSIFY", classify.Stage);
            Assert.AreEqual("classify_system", classify.SystemPromptKey);
            Assert.AreEqual("classify_template", classify.TemplateKey);

            var decompose = PromptStageRouter.GetKeys("DECOMPOSE");
            Assert.AreEqual("DECOMPOSE", decompose.Stage);
            Assert.AreEqual("decompose_system", decompose.SystemPromptKey);
            Assert.AreEqual("decompose_template", decompose.TemplateKey);

            var execute = PromptStageRouter.GetKeys("EXECUTE");
            Assert.AreEqual("EXECUTE", execute.Stage);
            Assert.AreEqual("execute_system", execute.SystemPromptKey);
            Assert.AreEqual("execute_template", execute.TemplateKey);
        }

        [TestMethod]
        public void SystemPrompts_AdhereToStageContracts()
        {
            var executePrompt = PromptCatalog.GetSystemPrompt("execute_system");
            Assert.IsFalse(executePrompt.IndexOf("feature task objects", StringComparison.OrdinalIgnoreCase) >= 0, "EXECUTE prompt should not reference feature task objects.");
            Assert.IsFalse(executePrompt.IndexOf("JSON array", StringComparison.OrdinalIgnoreCase) >= 0, "EXECUTE prompt should not ask for a JSON array of feature tasks.");

            var decomposePrompt = PromptCatalog.GetSystemPrompt("decompose_system");
            Assert.IsFalse(decomposePrompt.IndexOf("thinking", StringComparison.OrdinalIgnoreCase) >= 0, "DECOMPOSE prompt should not include thinking instructions.");
            Assert.IsFalse(decomposePrompt.Contains("\"steps\""), "DECOMPOSE prompt should not mention steps.");
        }
    }
}
