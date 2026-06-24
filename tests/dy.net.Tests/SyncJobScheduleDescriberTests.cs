using dy.net.utils;

namespace dy.net.Tests
{
    public class SyncJobScheduleDescriberTests
    {
        [Theory]
        [InlineData(false, null, 30, "每 30 分钟")]
        [InlineData(false, null, 60, "每 60 分钟")]
        [InlineData(true, "0 0/30 * * * ?", null, "Cron: 0 0/30 * * * ?")]
        [InlineData(false, null, null, "自定义")]
        [InlineData(true, null, null, "自定义")]
        [InlineData(false, null, 0, "自定义")]
        public void Describe_locks_current_behavior(bool isCron, string cronExpr, int? minutes, string expected)
        {
            Assert.Equal(expected, SyncJobScheduleDescriber.Describe(isCron, cronExpr, minutes));
        }
    }
}
