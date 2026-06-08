using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICAD.Services
{
    [TestClass]
    public class MaterialNameResolverTests
    {
        [TestMethod]
        public void ResolveForSolidWorks_NormalizesDropdownAndPromptVariants()
        {
            Assert.AreEqual("1060 Alloy", MaterialNameResolver.ResolveForSolidWorks("Aluminum"));
            Assert.AreEqual("1060 Alloy", MaterialNameResolver.ResolveForSolidWorks("Aluminum 1060 Alloy"));
            Assert.AreEqual("6061 Alloy", MaterialNameResolver.ResolveForSolidWorks("Aluminum 6061"));
            Assert.AreEqual("AISI 304", MaterialNameResolver.ResolveForSolidWorks("304 Stainless"));
            Assert.AreEqual("Plain Carbon Steel", MaterialNameResolver.ResolveForSolidWorks("Steel, Mild"));
            Assert.AreEqual("Nylon 6/10", MaterialNameResolver.ResolveForSolidWorks("nylon"));
        }

        [TestMethod]
        public void TryExtractFromText_PrefersSpecificAlloys()
        {
            Assert.IsTrue(MaterialNameResolver.TryExtractFromText("set the material to Aluminum 1060 alloy", out var aluminum));
            Assert.AreEqual("1060 Alloy", aluminum);

            Assert.IsTrue(MaterialNameResolver.TryExtractFromText("use 316 stainless for the body", out var stainless));
            Assert.AreEqual("AISI 316 Stainless Steel Sheet (SS)", stainless);
        }

        [TestMethod]
        public void AreEquivalent_ToleratesUiAndSolidWorksFormattingDifferences()
        {
            Assert.IsTrue(MaterialNameResolver.AreEquivalent("Aluminum 1060 Alloy", "1060 Alloy"));
            Assert.IsTrue(MaterialNameResolver.AreEquivalent("304 Stainless", "AISI 304"));
            Assert.IsFalse(MaterialNameResolver.AreEquivalent("Aluminum 6061", "Aluminum 7075"));
        }
    }
}
