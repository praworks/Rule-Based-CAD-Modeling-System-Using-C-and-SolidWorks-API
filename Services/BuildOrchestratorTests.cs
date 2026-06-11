using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    [TestClass]
    public class BuildOrchestratorTests
    {
        [TestMethod]
        public void NormalizeRepeatedCornerHoleTasks_CollapsesDuplicateCornerHolesWithoutEdgeKeyword()
        {
            var orchestrator = new BuildOrchestrator(null, null, null, null, null);
            var method = typeof(BuildOrchestrator).GetMethod("NormalizeRepeatedCornerHoleTasks", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "NormalizeRepeatedCornerHoleTasks should exist.");

            var tasks = new JArray
            {
                new JObject
                {
                    ["feature_type"] = "extrude",
                    ["role"] = "base",
                    ["intent"] = "create a plate 100x100x6mm",
                    ["depends_on"] = new JArray()
                },
                new JObject
                {
                    ["feature_type"] = "hole",
                    ["role"] = "dependent",
                    ["intent"] = "create a 10mm dia hole 10mm from corner",
                    ["depends_on"] = new JArray(0)
                },
                new JObject
                {
                    ["feature_type"] = "hole",
                    ["role"] = "dependent",
                    ["intent"] = "create a 10mm dia hole 10mm from corner",
                    ["depends_on"] = new JArray(0)
                },
                new JObject
                {
                    ["feature_type"] = "hole",
                    ["role"] = "dependent",
                    ["intent"] = "create a 10mm dia hole 10mm from corner",
                    ["depends_on"] = new JArray(0)
                },
                new JObject
                {
                    ["feature_type"] = "hole",
                    ["role"] = "dependent",
                    ["intent"] = "create a 10mm dia hole 10mm from corner",
                    ["depends_on"] = new JArray(0)
                }
            };

            method.Invoke(orchestrator, new object[] { tasks, null });

            Assert.AreEqual(2, tasks.Count, "Repeated single-corner hole tasks should collapse into one corner pattern task.");
            Assert.AreEqual(
                "create a 10mm dia hole 10mm from corner on all four corners",
                ((JObject)tasks[1])["intent"]?.ToString(),
                "Collapsed corner-hole intent should preserve the shorthand and mark the four-corner pattern.");
        }

        [TestMethod]
        public void ShouldUseFollowUpModelContext_AllowsNormalFollowUpPrompt()
        {
            var method = typeof(BuildOrchestrator).GetMethod("ShouldUseFollowUpModelContext", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "ShouldUseFollowUpModelContext should exist.");

            var result = (bool)method.Invoke(null, new object[] { "add a 10 mm hole at the center of the top face" });

            Assert.IsTrue(result, "Ordinary follow-up prompts should reuse the active model when the mode is enabled.");
        }

        [TestMethod]
        public void ShouldUseFollowUpModelContext_DisablesContextForFreshBuildPrompt()
        {
            var method = typeof(BuildOrchestrator).GetMethod("ShouldUseFollowUpModelContext", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "ShouldUseFollowUpModelContext should exist.");

            var result = (bool)method.Invoke(null, new object[] { "start over and create a new part from scratch" });

            Assert.IsFalse(result, "Explicit fresh-build prompts should bypass follow-up model context.");
        }
    }
}
