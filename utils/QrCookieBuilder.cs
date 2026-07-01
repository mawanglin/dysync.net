using System;
using System.Collections.Generic;
using System.Linq;

namespace dy.net.utils
{
    /// <summary>
    /// 把浏览器 cookie 列表拼成请求头用的 "k=v; k=v" 字符串。
    /// 仅保留 douyin.com 及其子域，防止把无关三方 cookie 也带上。
    /// </summary>
    public static class QrCookieBuilder
    {
        public static string Build(IReadOnlyList<BrowserCookie> cookies)
        {
            if (cookies == null) return string.Empty;

            return string.Join("; ", cookies
                .Where(c => c != null
                    && !string.IsNullOrWhiteSpace(c.Name)
                    && IsDouyinDomain(c.Domain))
                .Select(c => $"{c.Name}={c.Value}"));
        }

        private static bool IsDouyinDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return false;
            var d = domain.TrimStart('.');
            return d.Equals("douyin.com", StringComparison.OrdinalIgnoreCase)
                || d.EndsWith(".douyin.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
