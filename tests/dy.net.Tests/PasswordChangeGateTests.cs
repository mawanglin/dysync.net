using dy.net.utils;
using Xunit;

namespace dy.net.Tests
{
    /// <summary>
    /// Pins PasswordChangeGate.ShouldBlock — the server-side enforcement of forced
    /// first-login password change. A session whose JWT carries must_change_pwd=true is
    /// blocked from every endpoint except those marked [AllowWhenPasswordChangeRequired].
    /// </summary>
    public class PasswordChangeGateTests
    {
        [Theory]
        // 带标记的端点（改密 / 取 UserId）：无论是否待改密都放行
        [InlineData(true, "true", true, false)]
        [InlineData(true, null, true, false)]
        // 待改密会话访问普通端点 → 拦截
        [InlineData(true, "true", false, true)]
        [InlineData(true, "TRUE", false, true)]   // 大小写不敏感
        // 已改密（无 claim）→ 放行
        [InlineData(true, null, false, false)]
        [InlineData(true, "false", false, false)]
        // 未认证（匿名端点）→ 放行（由各自的鉴权/门控负责）
        [InlineData(false, null, false, false)]
        [InlineData(false, "true", false, false)]
        public void ShouldBlock_Truth(bool isAuthenticated, string claim, bool endpointAllows, bool expected)
        {
            Assert.Equal(expected, PasswordChangeGate.ShouldBlock(isAuthenticated, claim, endpointAllows));
        }
    }
}
