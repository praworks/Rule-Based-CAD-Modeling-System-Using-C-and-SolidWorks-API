using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICAD.Services
{
    [TestClass]
    public class FileNameRulesTests
    {
        [TestMethod]
        public void TryValidateSeriesId_RejectsInvalidCharacters()
        {
            Assert.IsFalse(FileNameRules.TryValidateSeriesId("ASM/01", out var error));
            Assert.AreEqual("Series ID can contain only letters, numbers, '-' and '_'.", error);
        }

        [TestMethod]
        public void TryValidateFileStem_RejectsReservedNames()
        {
            Assert.IsFalse(FileNameRules.TryValidateFileStem("CON", out var error));
            Assert.AreEqual("File name 'CON' is reserved by Windows.", error);
        }

        [TestMethod]
        public void SanitizeFileStem_RewritesInvalidAndReservedNames()
        {
            Assert.AreEqual("ASM_0001", FileNameRules.SanitizeFileStem("ASM:0001"));
            Assert.AreEqual("CON_", FileNameRules.SanitizeFileStem("CON"));
            Assert.AreEqual("Part", FileNameRules.SanitizeFileStem("..."));
        }
    }
}
