using System.Net;
using dy.net.utils;
using Xunit;

namespace dy.net.Tests
{
    /// <summary>
    /// Pins NetworkGuard.IsPrivateOrLoopback — the source gate for the anonymous
    /// cookie-tool endpoints (FastResetCookie / GetAllCookies). Private/loopback → allow,
    /// public → deny, incl. RFC1918 boundaries, link-local, IPv6 ULA, IPv4-mapped, null.
    /// </summary>
    public class NetworkGuardTests
    {
        [Theory]
        // 环回
        [InlineData("127.0.0.1", true)]
        [InlineData("127.255.255.254", true)]
        [InlineData("::1", true)]
        // 10.0.0.0/8
        [InlineData("10.0.0.1", true)]
        [InlineData("10.255.255.255", true)]
        // 172.16.0.0/12 及边界
        [InlineData("172.15.255.255", false)]
        [InlineData("172.16.0.0", true)]
        [InlineData("172.20.10.5", true)]
        [InlineData("172.31.255.255", true)]
        [InlineData("172.32.0.0", false)]
        // 192.168.0.0/16
        [InlineData("192.168.0.1", true)]
        [InlineData("192.168.1.100", true)]   // 工具默认示例地址
        [InlineData("192.169.0.1", false)]
        // 169.254.0.0/16 链路本地
        [InlineData("169.254.1.1", true)]
        [InlineData("169.253.0.1", false)]
        // 公网
        [InlineData("8.8.8.8", false)]
        [InlineData("1.1.1.1", false)]
        [InlineData("11.0.0.1", false)]
        [InlineData("192.167.255.255", false)]
        // IPv4-mapped IPv6
        [InlineData("::ffff:192.168.1.100", true)]
        [InlineData("::ffff:8.8.8.8", false)]
        // IPv6 ULA fc00::/7
        [InlineData("fd00::1", true)]
        [InlineData("fc00::1", true)]
        // IPv6 链路本地 fe80::/10
        [InlineData("fe80::1", true)]
        // IPv6 公网
        [InlineData("2001:4860:4860::8888", false)]
        public void IsPrivateOrLoopback_ClassifiesAddress(string ip, bool expected)
        {
            Assert.Equal(expected, NetworkGuard.IsPrivateOrLoopback(IPAddress.Parse(ip)));
        }

        [Fact]
        public void IsPrivateOrLoopback_Null_IsFalse()
        {
            Assert.False(NetworkGuard.IsPrivateOrLoopback(null));
        }

        /// <summary>
        /// IsLocalToolRequest：内网/本机 + 无转发头 → 放行；任何情况下出现转发头 → 拒绝
        /// （堵住同机反向代理后 RemoteIpAddress 恒为 loopback/LAN 的门控绕过）。
        /// </summary>
        [Theory]
        // 内网直连（无转发头）→ 放行
        [InlineData("192.168.1.100", false, true)]
        [InlineData("127.0.0.1", false, true)]
        [InlineData("10.0.0.5", false, true)]
        // 内网但带转发头（典型公网经代理）→ 拒绝
        [InlineData("127.0.0.1", true, false)]
        [InlineData("192.168.1.100", true, false)]
        [InlineData("10.0.0.5", true, false)]
        // 公网无论是否带转发头 → 拒绝
        [InlineData("8.8.8.8", false, false)]
        [InlineData("8.8.8.8", true, false)]
        public void IsLocalToolRequest_RequiresPrivateIpAndNoForwardedHeaders(string ip, bool hasForwarded, bool expected)
        {
            Assert.Equal(expected, NetworkGuard.IsLocalToolRequest(IPAddress.Parse(ip), hasForwarded));
        }

        [Fact]
        public void IsLocalToolRequest_NullIp_IsFalse()
        {
            Assert.False(NetworkGuard.IsLocalToolRequest(null, false));
        }
    }
}
