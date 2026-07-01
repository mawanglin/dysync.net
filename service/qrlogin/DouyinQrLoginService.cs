using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClockSnowFlake;
using dy.net.utils;

namespace dy.net.service.qrlogin
{
    /// <summary>
    /// 扫码登录会话状态机。Singleton：跨 start/poll 请求持有会话。
    /// 单会话约束：新 start 会取消并释放旧会话（并用 SemaphoreSlim 串行化，防并发下同时开两个浏览器）。
    /// 浏览器用完即关。
    /// </summary>
    public sealed class DouyinQrLoginService
    {
        private sealed class Session
        {
            public string Id { get; init; }
            public IQrLoginBrowser Browser { get; init; }
            public DateTime CreatedAt { get; init; }
        }

        private static readonly TimeSpan QrTtl = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan SessionMaxAge = TimeSpan.FromMinutes(3);

        private readonly IQrLoginBrowserFactory _factory;
        private readonly Func<DateTime> _clock;
        private readonly ConcurrentDictionary<string, Session> _sessions = new();
        private readonly SemaphoreSlim _startLock = new(1, 1);

        // 单构造函数 + 可选时钟参数：DI 解析 factory 并对未注册的 clock 用默认值 null；测试直接传时钟。
        public DouyinQrLoginService(IQrLoginBrowserFactory factory, Func<DateTime> clock = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        public async Task<QrStartResult> StartAsync()
        {
            // 串行化启动，保证「单会话约束」在并发下也成立
            await _startLock.WaitAsync();
            try
            {
                await CancelAllAsync();

                var browser = await _factory.CreateAsync();
                try
                {
                    await browser.OpenLoginPageAsync();
                    var png = await browser.ScreenshotQrAsync();
                    var id = IdGener.GetLong().ToString();
                    var now = _clock();
                    _sessions[id] = new Session { Id = id, Browser = browser, CreatedAt = now };
                    return new QrStartResult
                    {
                        SessionId = id,
                        QrImageBase64 = "data:image/png;base64," + Convert.ToBase64String(png),
                        ExpiresAt = now + QrTtl
                    };
                }
                catch
                {
                    await SafeDisposeAsync(browser);
                    throw;
                }
            }
            finally
            {
                _startLock.Release();
            }
        }

        public async Task<QrPollResult> PollAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var s))
                return new QrPollResult { Status = StatusText(QrLoginStatus.NotFound) };

            if (_clock() - s.CreatedAt > QrTtl)
            {
                await RemoveAndDisposeAsync(sessionId);
                return new QrPollResult { Status = StatusText(QrLoginStatus.Expired) };
            }

            QrLoginStatus status;
            try
            {
                status = await s.Browser.GetLoginStatusAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "扫码登录查询状态异常");
                status = QrLoginStatus.Error;
            }

            if (status != QrLoginStatus.Success)
                return new QrPollResult { Status = StatusText(status) };

            // 成功分支：无论取 cookie / 账号是否抛异常，都保证释放会话
            try
            {
                var cookieList = await s.Browser.GetCookiesAsync();
                var cookies = QrCookieBuilder.Build(cookieList);

                QrProfile profile = null;
                try
                {
                    profile = await s.Browser.GetProfileAsync();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "扫码登录抓取账号信息失败，降级为仅返回 cookie");
                }

                return new QrPollResult
                {
                    Status = StatusText(QrLoginStatus.Success),
                    Cookies = cookies,
                    SecUserId = profile?.SecUserId,
                    UserName = profile?.UserName,
                    MyUserId = profile?.MyUserId
                };
            }
            finally
            {
                await RemoveAndDisposeAsync(sessionId);
            }
        }

        public Task CancelAsync(string sessionId) => RemoveAndDisposeAsync(sessionId);

        public async Task SweepExpiredAsync()
        {
            var now = _clock();
            foreach (var id in _sessions.Keys.ToList())
            {
                if (_sessions.TryGetValue(id, out var s) && now - s.CreatedAt > SessionMaxAge)
                    await RemoveAndDisposeAsync(id);
            }
        }

        private static string StatusText(QrLoginStatus status) => status.ToString().ToLowerInvariant();

        private async Task CancelAllAsync()
        {
            foreach (var id in _sessions.Keys.ToList())
                await RemoveAndDisposeAsync(id);
        }

        private async Task RemoveAndDisposeAsync(string sessionId)
        {
            if (!string.IsNullOrWhiteSpace(sessionId) && _sessions.TryRemove(sessionId, out var s))
                await SafeDisposeAsync(s.Browser);
        }

        private static async Task SafeDisposeAsync(IQrLoginBrowser browser)
        {
            try { await browser.DisposeAsync(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "扫码登录浏览器释放异常"); }
        }
    }
}
