using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    [TestClass]
    public class LlmPlanServiceHeuristicsTests
    {
        [TestMethod]
        public void WindowCutout_IsPlannedAsThroughAllCut()
        {
            var featureTask = new JObject
            {
                ["feature_type"] = "extrude_cut",
                ["role"] = "dependent",
                ["intent"] = "create a rectangular window cutout 40 mm x 20 mm at the center"
            };

            var plan = LlmPlanService.PlanFeatureSubtask(featureTask, new JObject { ["feature_count"] = 1 });

            Assert.IsNotNull(plan, "Plan should be generated for a window cutout.");
            Assert.IsNotNull(plan.Steps, "Plan should contain executable steps.");
            Assert.AreEqual("auto_dimension", ((JObject)plan.Steps[3])["op"]?.ToString(), "Window cutout sketch should be auto-dimensioned before closing.");
            Assert.AreEqual("extrude_cut", ((JObject)plan.Steps[5])["op"]?.ToString(), "Last step should be an extrude_cut.");
            Assert.AreEqual(true, ((JObject)plan.Steps[5])["through_all"]?.Value<bool>(), "Window cutouts should default to through-all.");
            Assert.AreEqual(0d, ((JObject)plan.Steps[5])["depth"]?.Value<double>(), "Through-all cuts should not infer a blind depth from sketch dimensions.");
        }

        [TestMethod]
        public void TopCenterHole_WithoutExplicitDepth_RemainsThroughHole()
        {
            var featureTask = new JObject
            {
                ["feature_type"] = "hole",
                ["role"] = "dependent",
                ["intent"] = "create a 20 mm diameter hole at the center of the top face"
            };

            var plan = LlmPlanService.PlanFeatureSubtask(featureTask, new JObject { ["feature_count"] = 1 });

            Assert.IsNotNull(plan, "Plan should be generated for a centered top hole.");
            Assert.IsNotNull(plan.Steps, "Plan should contain executable steps.");
            Assert.AreEqual(1, plan.Steps.Count, "Top-center hole shortcut should emit a single hole step.");
            Assert.AreEqual("hole", ((JObject)plan.Steps[0])["op"]?.ToString(), "Plan should use the hole operation.");
            Assert.IsNull(((JObject)plan.Steps[0])["depth"], "A through hole should not infer a blind depth from the diameter.");
        }

        [TestMethod]
        public void CenterHole_WithoutFaceHint_DefaultsToTopFace()
        {
            var featureTask = new JObject
            {
                ["feature_type"] = "hole",
                ["role"] = "dependent",
                ["intent"] = "create a 20 mm diameter hole at the center"
            };

            var plan = LlmPlanService.PlanFeatureSubtask(featureTask, new JObject { ["feature_count"] = 1 });

            Assert.IsNotNull(plan, "Plan should be generated for a generic center hole.");
            Assert.IsNotNull(plan.Steps, "Plan should contain executable steps.");
            Assert.AreEqual(1, plan.Steps.Count, "Center hole shortcut should emit a single hole step.");
            Assert.AreEqual("top", ((JObject)plan.Steps[0])["face"]?.ToString(), "Center holes without a face hint should default to the top face.");
            Assert.IsNull(((JObject)plan.Steps[0])["depth"], "Generic center holes should remain through holes unless depth is explicit.");
        }

        [TestMethod]
        public void Pocket_WithThreeDimensions_KeepsExplicitDepthFallback()
        {
            var featureTask = new JObject
            {
                ["feature_type"] = "pocket",
                ["role"] = "dependent",
                ["intent"] = "create a rectangular pocket 40 mm x 20 mm x 5 mm on top"
            };

            var plan = LlmPlanService.PlanFeatureSubtask(featureTask, new JObject { ["feature_count"] = 1 });

            Assert.IsNotNull(plan, "Plan should be generated for a rectangular pocket.");
            Assert.IsNotNull(plan.Steps, "Plan should contain executable steps.");
            Assert.AreEqual("auto_dimension", ((JObject)plan.Steps[3])["op"]?.ToString(), "Pocket shortcut should fully define the sketch before closing it.");
            Assert.AreEqual("extrude_cut", ((JObject)plan.Steps[5])["op"]?.ToString(), "Pocket shortcut should terminate with an extrude_cut.");
            Assert.AreEqual(false, ((JObject)plan.Steps[5])["through_all"]?.Value<bool>(), "Explicit pocket depth should remain a blind cut.");
            Assert.AreEqual(5d, ((JObject)plan.Steps[5])["depth"]?.Value<double>(), "Pocket depth should come from the third dimension when no depth keyword is present.");
        }

        [TestMethod]
        public void TopMountedBossPlan_InsertsAutoDimensionBeforeSketchEnd()
        {
            var featureTask = new JObject
            {
                ["feature_type"] = "extrude",
                ["role"] = "dependent",
                ["intent"] = "create a 60 mm x 30 mm x 15 mm boss on top"
            };

            var plan = LlmPlanService.PlanFeatureSubtask(featureTask, new JObject { ["feature_count"] = 1 });

            Assert.IsNotNull(plan, "Plan should be generated for a top-mounted boss.");
            Assert.IsNotNull(plan.Steps, "Plan should contain executable steps.");
            Assert.AreEqual("auto_dimension", ((JObject)plan.Steps[3])["op"]?.ToString(), "Boss shortcut should fully define the sketch before closing it.");
            Assert.AreEqual("sketch_end", ((JObject)plan.Steps[4])["op"]?.ToString(), "Boss shortcut should close the sketch after auto-dimensioning.");
        }

        [TestMethod]
        public void BaseCube_UsesLocalExtrudeShortcut()
        {
            var featureTask = new JObject
            {
                ["feature_type"] = "extrude",
                ["role"] = "base",
                ["intent"] = "create cube 100 mm"
            };

            var plan = LlmPlanService.PlanFeatureSubtask(featureTask, null);

            Assert.IsNotNull(plan, "Plan should be generated for a base cube.");
            Assert.IsNotNull(plan.Steps, "Cube shortcut should return executable steps.");
            Assert.AreEqual("new_part", ((JObject)plan.Steps[0])["op"]?.ToString(), "Base cube shortcut should start a new part when no model exists.");
            Assert.AreEqual("rectangle_center", ((JObject)plan.Steps[3])["op"]?.ToString(), "Cube shortcut should sketch a centered rectangle.");
            Assert.AreEqual(100d, ((JObject)plan.Steps[6])["depth"]?.Value<double>(), "Cube depth should match the side length.");
        }

        [TestMethod]
        public void BaseCylinder_UsesLocalExtrudeShortcut()
        {
            var featureTask = new JObject
            {
                ["feature_type"] = "extrude",
                ["role"] = "base",
                ["intent"] = "create a cylinder 40 mm diameter and 80 mm height"
            };

            var plan = LlmPlanService.PlanFeatureSubtask(featureTask, null);

            Assert.IsNotNull(plan, "Plan should be generated for a base cylinder.");
            Assert.IsNotNull(plan.Steps, "Cylinder shortcut should return executable steps.");
            Assert.AreEqual("circle_center", ((JObject)plan.Steps[3])["op"]?.ToString(), "Cylinder shortcut should sketch a centered circle.");
            Assert.AreEqual(40d, ((JObject)plan.Steps[3])["diameter"]?.Value<double>(), "Cylinder diameter should come from the intent.");
            Assert.AreEqual(80d, ((JObject)plan.Steps[6])["depth"]?.Value<double>(), "Cylinder height should become extrude depth.");
        }

        [TestMethod]
        public void BaseCylinder_CommonMisspelling_UsesLocalExtrudeShortcut()
        {
            var featureTask = new JObject
            {
                ["feature_type"] = "extrude",
                ["role"] = "base",
                ["intent"] = "make a clyinder 40 mm diameter and 80 mm height"
            };

            var plan = LlmPlanService.PlanFeatureSubtask(featureTask, null);

            Assert.IsNotNull(plan, "Plan should be generated for a misspelled cylinder intent.");
            Assert.IsNotNull(plan.Steps, "Misspelled cylinder shortcut should return executable steps.");
            Assert.AreEqual("circle_center", ((JObject)plan.Steps[3])["op"]?.ToString(), "Misspelled cylinder shortcut should still sketch a centered circle.");
            Assert.AreEqual(40d, ((JObject)plan.Steps[3])["diameter"]?.Value<double>(), "Misspelled cylinder diameter should come from the intent.");
            Assert.AreEqual(80d, ((JObject)plan.Steps[6])["depth"]?.Value<double>(), "Misspelled cylinder height should become extrude depth.");
        }
    }
}
