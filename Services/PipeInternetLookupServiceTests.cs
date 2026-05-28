using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AICAD.Services
{
    [TestClass]
    public class PipeInternetLookupServiceTests
    {
        [TestMethod]
        public void ContainsPipeKeyword_AcceptsPipeAndTube()
        {
            Assert.IsTrue(PipeInternetLookupService.ContainsPipeKeyword("make a 1 inch sch 40 pipe"));
            Assert.IsTrue(PipeInternetLookupService.ContainsPipeKeyword("make a hollow tube"));
            Assert.IsFalse(PipeInternetLookupService.ContainsPipeKeyword("make a cylinder"));
        }

        [TestMethod]
        public void TryExtractSchedule_AcceptsSchAndSchedule()
        {
            Assert.AreEqual(40, PipeInternetLookupService.TryExtractSchedule("make 1 inch sch 40 pipe"));
            Assert.AreEqual(80, PipeInternetLookupService.TryExtractSchedule("make 2 in schedule 80 tube"));
        }

        [TestMethod]
        public void TryExtractNominalPipeSizeLabel_NormalizesCommonSizes()
        {
            Assert.AreEqual("1", PipeInternetLookupService.TryExtractNominalPipeSizeLabel("make 1 inch sch 40 pipe"));
            Assert.AreEqual("1-1/2", PipeInternetLookupService.TryExtractNominalPipeSizeLabel("make 1 1/2 inch pipe sch 40"));
            Assert.AreEqual("3/4", PipeInternetLookupService.TryExtractNominalPipeSizeLabel("make 3/4 in pipe schedule 80"));
        }

        [TestMethod]
        public void TryParsePipeScheduleChartHtml_ComputesIdFromOnlineOdAndWall()
        {
            const string html = @"
<table>
<tr><th>NPS</th><th>OD (mm)</th><th>OD (inch)</th><th>Sch 40 Wall (mm)</th><th>Sch 40 Wt</th><th>Sch 80 Wall (mm)</th><th>Sch 80 Wt</th></tr>
<tr><td>1&quot;</td><td>33.4</td><td>1.315</td><td>3.38</td><td>2.50</td><td>4.55</td><td>3.24</td></tr>
</table>";

            var info = PipeInternetLookupService.TryParsePipeScheduleChartHtml(html, "1", 40);

            Assert.IsNotNull(info);
            Assert.AreEqual("1", info.NpsLabel);
            Assert.AreEqual(40, info.Schedule);
            Assert.AreEqual(33.4d, info.OuterDiameterMm);
            Assert.AreEqual(3.38d, info.WallThicknessMm);
            Assert.AreEqual(26.64d, info.InnerDiameterMm);
        }
    }
}
