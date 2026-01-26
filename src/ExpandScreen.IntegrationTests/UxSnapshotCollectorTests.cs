using ExpandScreen.Services.Configuration;
using ExpandScreen.Services.Diagnostics;

namespace ExpandScreen.IntegrationTests
{
    public class UxSnapshotCollectorTests
    {
        [Fact]
        public void Collect_BuildsSummary_AndFeedbackTemplate()
        {
            var config = AppConfig.CreateDefault();
            var snap = UxSnapshotCollector.Collect(config);

            string summary = UxSnapshotCollector.BuildSummaryText(snap);
            Assert.Contains("ExpandScreen UX Snapshot", summary);

            string template = UxSnapshotCollector.BuildFeedbackTemplate(snap);
            Assert.Contains("用户体验测试反馈", template);
        }
    }
}

