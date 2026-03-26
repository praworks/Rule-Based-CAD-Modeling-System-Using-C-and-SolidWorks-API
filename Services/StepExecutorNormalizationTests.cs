using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace AICAD.Services
{
    [TestClass]
    public class StepExecutorNormalizationTests
    {
        private static JObject Normalize(JObject step)
        {
            var method = typeof(StepExecutor).GetMethod("NormalizeStep", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "NormalizeStep should be available for test coverage.");
            return (JObject)method.Invoke(null, new object[] { step });
        }

        [TestMethod]
        public void BareDimension_IsNormalizedToAutoDimension_WithoutInjectedPlaceholderDims()
        {
            var normalized = Normalize(new JObject { ["op"] = "dimension" });

            Assert.AreEqual("auto_dimension", normalized.Value<string>("op"));
            Assert.IsNull(normalized["cx"]);
            Assert.IsNull(normalized["cy"]);
            Assert.IsNull(normalized["w"]);
            Assert.IsNull(normalized["h"]);
        }

        [TestMethod]
        public void RectangleDimension_KeepsManualDimensionStep()
        {
            var normalized = Normalize(new JObject
            {
                ["op"] = "dimension",
                ["cx"] = 0,
                ["cy"] = 0,
                ["w"] = 100,
                ["h"] = 50
            });

            Assert.AreEqual("dimension", normalized.Value<string>("op"));
            Assert.AreEqual(100d, normalized.Value<double>("w"));
            Assert.AreEqual(50d, normalized.Value<double>("h"));
        }
    }
}
