using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICAD.Services
{
    [TestClass]
    public class FastenerInternetLookupServiceTests
    {
        [TestMethod]
        public void ContainsNutKeyword_AcceptsSingularAndPlural()
        {
            Assert.IsTrue(FastenerInternetLookupService.ContainsNutKeyword("make an M10 nut"));
            Assert.IsTrue(FastenerInternetLookupService.ContainsNutKeyword("make two M10 nuts"));
            Assert.IsFalse(FastenerInternetLookupService.ContainsNutKeyword("make an M10 washer"));
        }

        [TestMethod]
        public void ContainsBoltKeyword_AcceptsSingularAndPlural()
        {
            Assert.IsTrue(FastenerInternetLookupService.ContainsBoltKeyword("make an M24 bolt"));
            Assert.IsTrue(FastenerInternetLookupService.ContainsBoltKeyword("make three M24 bolts"));
            Assert.IsFalse(FastenerInternetLookupService.ContainsBoltKeyword("make an M24 stud"));
        }

        [TestMethod]
        public void ResolveBoltStandardName_DefaultsToIso4014()
        {
            Assert.AreEqual("ISO 4014", FastenerInternetLookupService.ResolveBoltStandardName("make M24x100 bolt"));
        }

        [TestMethod]
        public void ResolveBoltStandardName_UsesExplicitRequestedStandard()
        {
            Assert.AreEqual("DIN 933", FastenerInternetLookupService.ResolveBoltStandardName("make M24x100 bolt to DIN 933"));
            Assert.AreEqual("ISO 4017", FastenerInternetLookupService.ResolveBoltStandardName("make M12x60 bolt as per ISO 4017"));
            Assert.AreEqual("DIN 931", FastenerInternetLookupService.ResolveBoltStandardName("make M24x100 bolt to DIN931"));
        }

        [TestMethod]
        public void TryResolveMetricBoltInfo_UsesBuiltInDefaultStandardDimensions()
        {
            var info = FastenerInternetLookupService.TryResolveMetricBoltInfo("Create an M24x100 hex-head bolt");

            Assert.IsNotNull(info, "Bolt dimensions should resolve from the built-in default table when online enrichment is absent.");
            Assert.AreEqual("M24", info.Designation);
            Assert.AreEqual("ISO 4014", info.StandardName);
            Assert.AreEqual(36d, info.WidthAcrossFlatsMaxMm);
            Assert.AreEqual(15.2d, info.HeadHeightMaxMm);
        }
    }
}
