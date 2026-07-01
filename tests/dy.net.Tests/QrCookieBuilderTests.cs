using System.Collections.Generic;
using dy.net.utils;
using Xunit;

namespace dy.net.Tests
{
    public class QrCookieBuilderTests
    {
        [Fact]
        public void Build_JoinsDouyinCookies_AsKeyValueSemicolon()
        {
            var cookies = new List<BrowserCookie>
            {
                new BrowserCookie("sessionid", "abc", ".douyin.com"),
                new BrowserCookie("ttwid", "t1", "www.douyin.com"),
            };

            var result = QrCookieBuilder.Build(cookies);

            Assert.Equal("sessionid=abc; ttwid=t1", result);
        }

        [Fact]
        public void Build_FiltersOutNonDouyinDomains()
        {
            var cookies = new List<BrowserCookie>
            {
                new BrowserCookie("sessionid", "abc", ".douyin.com"),
                new BrowserCookie("_ga", "x", ".google.com"),
                new BrowserCookie("evil", "y", "douyin.com.attacker.com"),
            };

            var result = QrCookieBuilder.Build(cookies);

            Assert.Equal("sessionid=abc", result);
        }

        [Fact]
        public void Build_SkipsEmptyNames_AndHandlesNull()
        {
            Assert.Equal("", QrCookieBuilder.Build(null));
            Assert.Equal("", QrCookieBuilder.Build(new List<BrowserCookie>
            {
                new BrowserCookie("", "v", ".douyin.com"),
            }));
        }
    }
}
