using dy.net.utils;

namespace dy.net.Tests
{
    public class JobScheduleValidatorTests
    {
        [Theory]
        [InlineData("interval", "30", true, "30")]
        [InlineData("interval", " 45 ", true, "45")]
        [InlineData("interval", "0", false, null)]
        [InlineData("interval", "-5", false, null)]
        [InlineData("interval", "abc", false, null)]
        [InlineData("cron", "0 0/30 * * * ?", true, "0 0/30 * * * ?")]
        [InlineData("cron", "not a cron", false, null)]
        [InlineData("weekly", "x", false, null)]
        public void ValidateAndNormalize_locks_behavior(string type, string expr, bool ok, string normalized)
        {
            var (resultOk, resultNorm, error) = JobScheduleValidator.ValidateAndNormalize(type, expr);
            Assert.Equal(ok, resultOk);
            if (ok) { Assert.Equal(normalized, resultNorm); Assert.Null(error); }
            else { Assert.Null(resultNorm); Assert.False(string.IsNullOrEmpty(error)); }
        }
    }
}
