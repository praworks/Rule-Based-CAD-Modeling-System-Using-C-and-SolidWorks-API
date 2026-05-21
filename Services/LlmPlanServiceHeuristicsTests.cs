using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    [TestClass]
    public class LlmPlanServiceHeuristicsTests
    {
        [TestMethod]
        public void MetricHexBolt_UsesLocalDecomposeShortcut()
        {
            var request = string.Join("\n", new[]
            {
                "make M24x100 bolt",
                "",
                "ONLINE FASTENER CONTEXT (auto-fetched; use only if it matches the user request):",
                "- Standard interpretation: ISO 4014 metric hex-head bolt size M24 (default standard).",
                "- Bolt head dimensions from online chart: width across flats 36 mm max (35 mm min) and head height 15 mm max (14 mm min).",
                "- Modeling hint: interpret this as a hex head per ISO 4014 near 36 mm across flats with head height about 15 mm, plus a cylindrical shank using the nominal M24 diameter. If shank length is missing, ask one clarification instead of guessing."
            });

            var result = LlmPlanService.DecomposeByFeature(request);

            Assert.IsNotNull(result, "Decompose result should be generated for a metric bolt request.");
            Assert.IsNotNull(result.Features, "Metric bolt shortcut should return ordered features.");
            Assert.AreEqual(2, result.Features.Count, "Metric bolt shortcut should emit shank and head features.");
            Assert.AreEqual("Hex-head bolt M24 x 100 mm (ISO 4014)", result.Description, "Description should preserve the compact engineering bolt notation and report the standard used.");
            Assert.AreEqual("extrude", ((JObject)result.Features[0])["feature_type"]?.ToString(), "Shank should be modeled as an extrude.");
            Assert.AreEqual("create a cylinder 24 mm diameter and 100 mm height", ((JObject)result.Features[0])["intent"]?.ToString(), "Shank intent should use the metric diameter and compact length.");
            Assert.AreEqual("create a hex head 36 mm across flats and 15 mm height on top", ((JObject)result.Features[1])["intent"]?.ToString(), "Head intent should use the looked-up hex dimensions.");
        }

        [TestMethod]
        public void MetricHexBolt_RawPrompt_UsesBuiltInStandardFallback()
        {
            var result = LlmPlanService.DecomposeByFeature("Create an M24x100 hex-head bolt");

            Assert.IsNotNull(result, "Decompose result should be generated for a raw metric bolt request.");
            Assert.IsNotNull(result.Features, "Raw metric bolt shortcut should return ordered features.");
            Assert.AreEqual(2, result.Features.Count, "Raw metric bolt shortcut should emit shank and head features.");
            Assert.AreEqual("Hex-head bolt M24 x 100 mm (ISO 4014)", result.Description, "Description should report the default standard used.");
            Assert.AreEqual("create a hex head 36 mm across flats and 15.2 mm height on top", ((JObject)result.Features[1])["intent"]?.ToString(), "Raw metric bolt shortcut should use built-in default standard dimensions.");
        }

        [TestMethod]
        public void MetricHexBolt_RawPrompt_HeadFeaturePlansWithoutHexClarification()
        {
            var decompose = LlmPlanService.DecomposeByFeature("Create an M24x100 hex-head bolt");
            var headFeature = (JObject)decompose.Features[1];

            var plan = LlmPlanService.PlanFeatureSubtask(headFeature, new JObject { ["feature_count"] = 1 });

            Assert.IsNotNull(plan, "Plan should be generated for the raw prompt bolt head.");
            Assert.IsFalse(plan.ClarificationNeeded, "Bolt head should not ask for hex size when standard dimensions were resolved.");
            Assert.IsNotNull(plan.Steps, "Bolt head plan should contain executable steps.");
            Assert.AreEqual("line", ((JObject)plan.Steps[2])["op"]?.ToString(), "Hex head plan should sketch explicit hex edges.");
            Assert.AreEqual(15.2d, ((JObject)plan.Steps[10])["depth"]?.Value<double>(), "Hex head plan should extrude with the resolved standard head height.");
        }

        [TestMethod]
        public void MetricHexBolt_UsesExplicitIso4017AcrossFlatsOverride()
        {
            var request = string.Join("\n", new[]
            {
                "make M12x60 bolt to ISO 4017",
                "",
                "ONLINE FASTENER CONTEXT (auto-fetched; use only if it matches the user request):",
                "- Standard interpretation: ISO 4017 metric hex-head bolt size M12.",
                "- Bolt head dimensions from online chart: width across flats 18 mm max (17.57 mm min) and head height 7.5 mm max (7.32 mm min).",
                "- Modeling hint: interpret this as a hex head per ISO 4017 near 18 mm across flats with head height about 7.5 mm, plus a cylindrical shank using the nominal M12 diameter. If shank length is missing, ask one clarification instead of guessing."
            });

            var result = LlmPlanService.DecomposeByFeature(request);

            Assert.IsNotNull(result, "Decompose result should be generated for an ISO 4017 metric bolt request.");
            Assert.IsNotNull(result.Features, "ISO 4017 metric bolt shortcut should return ordered features.");
            Assert.AreEqual("Hex-head bolt M12 x 60 mm (ISO 4017)", result.Description, "Description should preserve the requested standard.");
            Assert.AreEqual("create a hex head 18 mm across flats and 7.5 mm height on top", ((JObject)result.Features[1])["intent"]?.ToString(), "ISO 4017 sizes with different wrench flats should use the ISO override values.");
        }

        [TestMethod]
        public void MetricHexNut_UsesLocalDecomposeShortcut()
        {
            var request = string.Join("\n", new[]
            {
                "make M24 nuts",
                "",
                "ONLINE FASTENER CONTEXT (auto-fetched; use only if it matches the user request):",
                "- Standard interpretation: ISO 4032 metric hex nut size M24.",
                "- Nut dimensions from online chart: width across flats 36 mm max (35 mm min), width across corners 41.6 mm min, nut height 19 mm max (18.4 mm min).",
                "- Modeling hint: interpret this as a hex nut body with across-flats size about 36 mm and height about 19 mm. If a center hole is required, ask one short clarification instead of assuming more geometry."
            });

            var result = LlmPlanService.DecomposeByFeature(request);

            Assert.IsNotNull(result, "Decompose result should be generated for a metric nut request.");
            Assert.IsNotNull(result.Features, "Metric nut shortcut should return ordered features.");
            Assert.AreEqual(2, result.Features.Count, "Metric nut shortcut should emit body and hole features.");
            Assert.AreEqual("Hex nut M24", result.Description, "Description should preserve the compact engineering nut notation.");
            Assert.AreEqual("extrude", ((JObject)result.Features[0])["feature_type"]?.ToString(), "Nut body should be modeled as an extrude.");
            Assert.AreEqual("create a hex nut body 36 mm across flats and 19 mm height", ((JObject)result.Features[0])["intent"]?.ToString(), "Nut body intent should use the looked-up hex dimensions.");
            Assert.AreEqual("hole", ((JObject)result.Features[1])["feature_type"]?.ToString(), "Nut opening should be modeled as a hole.");
            Assert.AreEqual("create a 24 mm diameter hole at the center of the top face", ((JObject)result.Features[1])["intent"]?.ToString(), "Nut hole intent should use the nominal metric diameter.");
        }

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

        [TestMethod]
        public void BaseHexPrism_UsesLocalHexSketchShortcut()
        {
            var featureTask = new JObject
            {
                ["feature_type"] = "extrude",
                ["role"] = "base",
                ["intent"] = "create a hex nut body 16 mm across flats and 8.4 mm height"
            };

            var plan = LlmPlanService.PlanFeatureSubtask(featureTask, null);

            Assert.IsNotNull(plan, "Plan should be generated for a base hex prism.");
            Assert.IsNotNull(plan.Steps, "Hex prism shortcut should return executable steps.");
            Assert.AreEqual("new_part", ((JObject)plan.Steps[0])["op"]?.ToString(), "Base hex prism should start a new part when no model exists.");
            Assert.AreEqual("line", ((JObject)plan.Steps[3])["op"]?.ToString(), "Hex prism shortcut should sketch with explicit line segments.");
            Assert.AreEqual("auto_dimension", ((JObject)plan.Steps[9])["op"]?.ToString(), "Hex prism shortcut should auto-dimension before sketch end.");
            Assert.AreEqual(8.4d, ((JObject)plan.Steps[11])["depth"]?.Value<double>(), "Hex prism height should become extrude depth.");
        }

    }
}
