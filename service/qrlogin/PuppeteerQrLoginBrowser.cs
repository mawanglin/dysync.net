using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using dy.net.utils;
using PuppeteerSharp;

namespace dy.net.service.qrlogin
{
    /// <summary>
    /// PuppeteerSharp 版 IQrLoginBrowser。
    /// 登录检测用「是否出现 sessionid cookie」，不依赖登录页 DOM 结构（最稳）。
    /// 二维码截图 / 账号抓取用页面选择器，是唯一需要按抖音页面调优的地方（下方常量）。
    /// </summary>
    public sealed class PuppeteerQrLoginBrowser : IQrLoginBrowser
    {
        // ---- 调优面：如抖音改版，改这里 ----
        private const string LoginUrl = "https://www.douyin.com/";
        private const string ProfileUrl = "https://www.douyin.com/user/self";
        // 二维码优先按元素截图，取不到则整页兜底
        private static readonly string[] QrSelectors =
        {
            "img[src*=\"qrcode\"]",
            "canvas",
            "div[class*=\"qrcode\"]"
        };
        // 登录态判定 cookie 名
        private static readonly string[] SessionCookieNames = { "sessionid", "sessionid_ss", "sid_tt" };
        // sec_user_id 从 /user/self 跳转后的 URL 里解析
        private static readonly Regex SecUserIdRegex = new(@"/user/([^/?#]+)", RegexOptions.Compiled);
        private const string NicknameSelector = "span[class*=\"nickname\"], h1";

        private readonly IBrowser _browser;
        private IPage _page;
        private bool _disposed;

        public PuppeteerQrLoginBrowser(IBrowser browser)
        {
            _browser = browser;
        }

        public async Task OpenLoginPageAsync()
        {
            _page = await _browser.NewPageAsync();
            await _page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 900 });
            await _page.GoToAsync(LoginUrl, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                Timeout = 30000
            });
            // 给登录弹层/二维码一点渲染时间
            await Task.Delay(2000);
        }

        public async Task<byte[]> ScreenshotQrAsync()
        {
            foreach (var sel in QrSelectors)
            {
                var el = await _page.QuerySelectorAsync(sel);
                if (el != null)
                    return await el.ScreenshotDataAsync();
            }
            // 兜底：整页截图
            return await _page.ScreenshotDataAsync();
        }

        public async Task<QrLoginStatus> GetLoginStatusAsync()
        {
            var cookies = await _page.GetCookiesAsync();
            var loggedIn = cookies.Any(c =>
                SessionCookieNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(c.Value));
            return loggedIn ? QrLoginStatus.Success : QrLoginStatus.Waiting;
        }

        public async Task<IReadOnlyList<BrowserCookie>> GetCookiesAsync()
        {
            var cookies = await _page.GetCookiesAsync();
            return cookies
                .Select(c => new BrowserCookie(c.Name, c.Value, c.Domain ?? string.Empty))
                .ToList();
        }

        public async Task<QrProfile> GetProfileAsync()
        {
            await _page.GoToAsync(ProfileUrl, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                Timeout = 30000
            });

            var profile = new QrProfile();

            var m = SecUserIdRegex.Match(_page.Url);
            if (m.Success && !m.Groups[1].Value.Equals("self", StringComparison.OrdinalIgnoreCase))
                profile.SecUserId = m.Groups[1].Value;

            try
            {
                var nick = await _page.QuerySelectorAsync(NicknameSelector);
                if (nick != null)
                {
                    var text = await nick.EvaluateFunctionAsync<string>("e => e.textContent");
                    profile.UserName = text?.Trim();
                }
            }
            catch { /* 昵称抓不到不致命 */ }

            return profile;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try { if (_page != null) await _page.CloseAsync(); } catch { }
            try { await _browser.CloseAsync(); } catch { }
            try { _browser.Dispose(); } catch { }
        }
    }
}
