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
        // 伪装成正常桌面 Chrome 的 UA，降低被判定为爬虫而弹滑块
        private const string RealUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
        // 二维码优先按元素截图；canvas 放最后，避免误截到滑块验证等其它 canvas，取不到则整页兜底
        private static readonly string[] QrSelectors =
        {
            "img[src*=\"qrcode\"]",
            "div[class*=\"qrcode\"] img",
            "div[class*=\"qrcode\"]",
            "canvas"
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

            // 反自动化检测：正常 UA + 抹掉 navigator.webdriver（在每个新文档注入，先于页面脚本执行）
            await _page.SetUserAgentAsync(RealUserAgent);
            await _page.EvaluateExpressionOnNewDocumentAsync(
                "Object.defineProperty(navigator,'webdriver',{get:()=>undefined});");

            // 视口调大 + 2x 高清：二维码渲染得更大更清晰，扫码更容易
            await _page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 900, DeviceScaleFactor = 2 });

            await _page.GoToAsync(LoginUrl, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                Timeout = 30000,
                // 显式给合法 referrerPolicy，规避 PuppeteerSharp 默认空值导致
                // Chromium 报 "Protocol error (Page.navigate): Invalid referrerPolicy"
                ReferrerPolicy = "unsafeUrl"
            });
            // 给登录弹层/二维码一点渲染时间
            await Task.Delay(2000);
        }

        public async Task<byte[]> ScreenshotQrAsync()
        {
            // 诊断：二维码常在 passport iframe 内，先把各 frame 结构打到日志，便于精确定位选择器
            foreach (var frame in _page.Frames)
            {
                try
                {
                    var info = await frame.EvaluateExpressionAsync<string>(
                        "JSON.stringify({u:location.href," +
                        "img:document.querySelectorAll('img').length," +
                        "canvas:document.querySelectorAll('canvas').length," +
                        "qr:document.querySelectorAll('[class*=qrcode],[class*=qr-code],img[src*=qrcode]').length})");
                    Serilog.Log.Information("扫码诊断 frame: {Info}", info);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning("扫码诊断 frame 失败: {Msg}", ex.Message);
                }
            }

            // 在所有 frame（含 iframe）里找可见、够大的二维码元素并截图；单个元素失败不中断，继续找
            foreach (var frame in _page.Frames)
            {
                foreach (var sel in QrSelectors)
                {
                    try
                    {
                        var el = await frame.QuerySelectorAsync(sel);
                        if (el == null) continue;
                        var box = await el.BoundingBoxAsync();
                        if (box == null || box.Width < 60 || box.Height < 60) continue; // 隐藏/太小则跳过
                        return await el.ScreenshotDataAsync();
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning("二维码元素截图失败({Sel}): {Msg}", sel, ex.Message);
                    }
                }
            }

            // 兜底：整页截图，保证有图返回、绝不抛异常
            Serilog.Log.Warning("未匹配到可见二维码元素，回退整页截图");
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
                Timeout = 30000,
                ReferrerPolicy = "unsafeUrl"
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
