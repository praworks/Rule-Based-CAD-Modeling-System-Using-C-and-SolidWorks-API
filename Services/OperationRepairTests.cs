using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using AICAD.Services.Operations;

namespace AICAD.Services
{
    [TestClass]
    public class OperationRepairTests
    {
        [TestMethod]
        public void PlanWithCreateCube_IsRepairedToRegisteredOps()
        {
            var allowed = new System.Collections.Generic.HashSet<string>(OperationRegistry.CreateDefault().GetRegisteredOperations(), StringComparer.OrdinalIgnoreCase);
            var initialPlan = new LlmPlanService.FeaturePlanResult
            {
                Steps = JArray.Parse("[{\"op\":\"create_cube\",\"params\":{\"size\":10}}]")
            };

            // Test hook: return a repaired plan using only allowed ops
            LlmPlanService.OpRepairResponder = _ =>
                "{\"steps\":[{\"op\":\"new_part\"},{\"op\":\"select_plane\",\"params\":{\"name\":\"Front Plane\"}},{\"op\":\"sketch_begin\"},{\"op\":\"rectangle_center\",\"params\":{\"cx\":0,\"cy\":0,\"w\":10,\"h\":10}},{\"op\":\"sketch_end\"},{\"op\":\"extrude\",\"params\":{\"depth\":10}}]}";
            try
            {
                var repaired = LlmPlanService.ValidateAndRepairOpsForTest(initialPlan, "orig", "sys", "feature", 15, "run-test", "req-test");
                Assert.IsNotNull(repaired, "Repair should return a plan result");
                Assert.IsNotNull(repaired.Steps, "Repaired plan should contain steps");
                Assert.IsTrue(repaired.Steps.All(s => allowed.Contains(((JObject)s)["op"]?.ToString() ?? string.Empty)), "All repaired ops must be registered");
                Assert.IsFalse(repaired.Steps.Any(s => string.Equals(((JObject)s)["op"]?.ToString(), "create_cube", StringComparison.OrdinalIgnoreCase)), "create_cube should be removed after repair");
            }
            finally
            {
                LlmPlanService.OpRepairResponder = null;
            }
        }
    }
}
