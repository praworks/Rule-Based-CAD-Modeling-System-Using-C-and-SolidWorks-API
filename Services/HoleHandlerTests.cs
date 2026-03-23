using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using AICAD.Services.Operations.PartFeatures;

namespace AICAD.Services
{
    [TestClass]
    public class HoleHandlerTests
    {
        [TestMethod]
        public void NullDepth_IsNotTreatedAsBlindHole()
        {
            var step = new JObject
            {
                ["op"] = "hole",
                ["diameter"] = 20,
                ["target"] = "center",
                ["face"] = "top",
                ["depth"] = JValue.CreateNull()
            };

            var hasBlindDepth = HoleHandler.TryGetBlindDepth(step, out var depthMeters);

            Assert.IsFalse(hasBlindDepth, "A null depth should be treated as omitted, not as a blind-hole request.");
            Assert.AreEqual(0.0, depthMeters, 1e-12, "No blind depth should be returned when depth is null.");
        }

        [TestMethod]
        public void PositiveDepth_IsTreatedAsBlindHole()
        {
            var step = new JObject
            {
                ["op"] = "hole",
                ["diameter"] = 20,
                ["target"] = "center",
                ["face"] = "top",
                ["depth"] = 15
            };

            var hasBlindDepth = HoleHandler.TryGetBlindDepth(step, out var depthMeters);

            Assert.IsTrue(hasBlindDepth, "A positive numeric depth should remain a blind-hole request.");
            Assert.AreEqual(0.015, depthMeters, 1e-12, "Blind-hole depth should be converted from mm to meters.");
        }
    }
}
