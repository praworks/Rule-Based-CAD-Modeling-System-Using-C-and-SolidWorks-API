using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICAD.Services
{
    [TestClass]
    public class PostBuildViewServiceTests
    {
        [TestMethod]
        public void NormalizeMode_ReturnsExpectedModes()
        {
            Assert.AreEqual("isometric", PostBuildViewService.NormalizeMode(null));
            Assert.AreEqual("isometric", PostBuildViewService.NormalizeMode("unexpected"));
            Assert.AreEqual("top", PostBuildViewService.NormalizeMode("Top"));
            Assert.AreEqual("front", PostBuildViewService.NormalizeMode(" FRONT "));
            Assert.AreEqual("right", PostBuildViewService.NormalizeMode("right"));
            Assert.AreEqual("left", PostBuildViewService.NormalizeMode("left"));
            Assert.AreEqual("none", PostBuildViewService.NormalizeMode("none"));
        }
    }
}
