# 抖音扫码登录自动获取 Cookie 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 用无头 Chromium 打开抖音登录页、前端展示二维码、用户抖音 App 扫码后由服务端从浏览器上下文导出 cookie（并抓取账号信息），汇入现有 `/config/update` 落库，替代手动 F12 提取 cookie。

**架构：** 新增一条"扫码登录会话"链路：`前端 QrLoginModal` → `POST /api/qrlogin/start`（启动一次性 Chromium、截二维码 base64）→ 前端轮询 `GET /api/qrlogin/poll`（检测登录成功即导出 cookie + 抓 sec_user_id/昵称/uid，随后关浏览器）→ 前端回填新增表单（新增账号）或直接 `POST /api/config/update` 更新（过期重扫）。会话状态机 `DouyinQrLoginService` 为 Singleton，持有 `ConcurrentDictionary` 会话；PuppeteerSharp 交互藏在 `IQrLoginBrowser` 抽象后，便于对状态机做纯单测。浏览器用完即关。

**技术栈：** C# .NET 8、PuppeteerSharp（指向 apt 装的系统 chromium）、xUnit、Vue3 + Pinia + ant-design-vue、Docker（Debian bookworm）。

**规格：** `docs/superpowers/specs/2026-07-01-douyin-qrlogin-cookie-design.md`

**关键既有资产（复用，勿重造）：**
- `service/DouyinHttpClientService.cs:515` `CheckCookie(DouyinCookie)`：拿 cookie 去抖音验活（有 SecUserId 走 SyncMyFollows，否则 SyncCollectVideos）。
- `Controllers/ConfigController.cs:299` `AddOrUpdateAsync`（`POST /api/config/update`）：`id=="0"` 新增、否则更新，内部已含 CheckCookie 复验 + 置 `StatusMsg="正常"` + `ReStartJob()`。
- 前端 `app/src/store/coreapi.ts:191` `UpdateConfig(param)` → `POST /api/config/update`；`CookiePageList`、`SwitchCookieStatus`、`deleteCookie` 等。
- `app/src/pages/cok/CookieTable.vue`：新增/编辑表单（`DataItem`、`form`、`newCookie()`、`copyObject()`、`submit()`）、行操作、状态列。
- DI：`Program.cs:144` `AddHttpClients()`；`Program.cs:153-154` `AddServicesFromNamespace("dy.net.repository").AddServicesFromNamespace("dy.net.service")`（仅精确命名空间，不含子命名空间）。
- `extension/ServiceLifetimeAttribute.cs`、`ClockSnowFlake.IdGener.GetLong()`。
- 控制器基类风格：`[Route("api/[controller]")] [ApiController] [Authorize]`，返回 `ApiResult.Success(data)` / `ApiResult.Fail(msg)`。
- 测试项目：`tests/dy.net.Tests/dy.net.Tests.csproj`（xUnit），运行 `dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`。

**命名空间约定（重要）：**
- `dy.net.utils`：`BrowserCookie`（record）、`QrCookieBuilder`（纯函数）。
- `dy.net.service.qrlogin`：状态枚举、DTO、`IQrLoginBrowser`/工厂、`DouyinQrLoginService`、PuppeteerSharp 实现、清理后台服务。**放子命名空间是为了不被 `AddServicesFromNamespace("dy.net.service")` 自动注册**，改用显式 DI（状态机须 Singleton、浏览器实现不可被直接解析）。
- `dy.net.Controllers`：`QrLoginController`。

---

## 任务 1：Cookie 字符串拼装纯函数 + BrowserCookie

把"浏览器 cookie 列表 → `k=v; k=v` 字符串（仅 `.douyin.com` 域）"做成纯函数，先 TDD。

**文件：**
- 创建：`model/BrowserCookie.cs`
- 创建：`utils/QrCookieBuilder.cs`
- 测试：`tests/dy.net.Tests/QrCookieBuilderTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `tests/dy.net.Tests/QrCookieBuilderTests.cs`：

```csharp
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
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter QrCookieBuilderTests`
预期：编译失败（`BrowserCookie`/`QrCookieBuilder` 未定义）。

- [ ] **步骤 3：编写最少实现代码**

创建 `model/BrowserCookie.cs`：

```csharp
namespace dy.net.utils
{
    /// <summary>
    /// 浏览器 cookie 的纯数据表示（脱离 PuppeteerSharp 类型，便于纯函数单测）。
    /// </summary>
    public sealed record BrowserCookie(string Name, string Value, string Domain);
}
```

创建 `utils/QrCookieBuilder.cs`：

```csharp
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
```

> 注：域判断同时接受 `douyin.com` 与 `*.douyin.com`（`.douyin.com`、`www.douyin.com`），并拒绝 `douyin.com.attacker.com` 这类后缀伪造。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter QrCookieBuilderTests`
预期：PASS（3 个测试）。

- [ ] **步骤 5：Commit**

```bash
git add model/BrowserCookie.cs utils/QrCookieBuilder.cs tests/dy.net.Tests/QrCookieBuilderTests.cs
git commit -m "feat(qrlogin): cookie 字符串拼装纯函数 + BrowserCookie（域过滤）"
```

---

## 任务 2：会话契约（枚举 + DTO + 浏览器抽象）

定义状态机与前端交互所需的数据契约与浏览器抽象接口。无逻辑、无测试。

**文件：**
- 创建：`service/qrlogin/QrLoginContracts.cs`
- 创建：`service/qrlogin/IQrLoginBrowser.cs`

- [ ] **步骤 1：编写契约**

创建 `service/qrlogin/QrLoginContracts.cs`：

```csharp
namespace dy.net.service.qrlogin
{
    /// <summary>扫码会话状态机的状态。</summary>
    public enum QrLoginStatus
    {
        Waiting,   // 已出码，等待扫描
        Scanned,   // 已扫描，等待手机确认（可选，当前实现可不细分）
        Confirmed, // 已确认（可选）
        Success,   // 登录成功，cookie 可导出
        Expired,   // 二维码/会话过期
        Error,     // 浏览器异常
        NotFound   // 会话不存在
    }

    /// <summary>start 返回：二维码 + 会话标识。</summary>
    public sealed class QrStartResult
    {
        public string SessionId { get; set; }
        public string QrImageBase64 { get; set; } // "data:image/png;base64,...."
        public System.DateTime ExpiresAt { get; set; }
    }

    /// <summary>poll 返回：状态 + 成功时的 cookie 与账号信息。</summary>
    public sealed class QrPollResult
    {
        public string Status { get; set; } // QrLoginStatus 的小写字符串
        public string Cookies { get; set; }
        public string SecUserId { get; set; }
        public string UserName { get; set; }
        public string MyUserId { get; set; }
    }

    /// <summary>登录成功后抓取到的账号信息。</summary>
    public sealed class QrProfile
    {
        public string SecUserId { get; set; }
        public string UserName { get; set; }
        public string MyUserId { get; set; }
    }
}
```

创建 `service/qrlogin/IQrLoginBrowser.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using dy.net.utils;

namespace dy.net.service.qrlogin
{
    /// <summary>
    /// 单次扫码登录所用浏览器的生命周期抽象。一个实例对应一个会话，用完即弃。
    /// PuppeteerSharp 交互全部藏在实现里，状态机可注入假实现做单测。
    /// </summary>
    public interface IQrLoginBrowser : IAsyncDisposable
    {
        /// <summary>启动浏览器并打开抖音登录页，等二维码出现。</summary>
        Task OpenLoginPageAsync();

        /// <summary>截取二维码区域，返回 PNG 字节。</summary>
        Task<byte[]> ScreenshotQrAsync();

        /// <summary>查询当前登录状态（Waiting / Success 等）。</summary>
        Task<QrLoginStatus> GetLoginStatusAsync();

        /// <summary>导出当前浏览器 cookie（纯数据）。</summary>
        Task<IReadOnlyList<BrowserCookie>> GetCookiesAsync();

        /// <summary>抓取账号信息；失败时抛异常，由调用方降级。</summary>
        Task<QrProfile> GetProfileAsync();
    }

    /// <summary>创建 IQrLoginBrowser 的工厂（便于替换真实/假实现）。</summary>
    public interface IQrLoginBrowserFactory
    {
        Task<IQrLoginBrowser> CreateAsync();
    }
}
```

- [ ] **步骤 2：编译验证**

运行：`dotnet build dy.net.csproj`
预期：编译通过（仅新增类型，无引用方）。

- [ ] **步骤 3：Commit**

```bash
git add service/qrlogin/QrLoginContracts.cs service/qrlogin/IQrLoginBrowser.cs
git commit -m "feat(qrlogin): 会话状态枚举/DTO + IQrLoginBrowser 抽象"
```

---

## 任务 3：会话状态机 DouyinQrLoginService（TDD）

核心：单会话约束、二维码过期、成功导出 cookie+账号、抓取失败降级、并发取消旧会话、过期清理。用假浏览器驱动。

**文件：**
- 创建：`service/qrlogin/DouyinQrLoginService.cs`
- 测试：`tests/dy.net.Tests/QrLoginServiceTests.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `tests/dy.net.Tests/QrLoginServiceTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using dy.net.service.qrlogin;
using dy.net.utils;
using Xunit;

namespace dy.net.Tests
{
    public class QrLoginServiceTests
    {
        // ---- 假浏览器 ----
        private sealed class FakeBrowser : IQrLoginBrowser
        {
            public QrLoginStatus Status = QrLoginStatus.Waiting;
            public bool ThrowOnProfile = false;
            public bool Disposed = false;
            public List<BrowserCookie> Cookies = new() { new BrowserCookie("sessionid", "s1", ".douyin.com") };
            public QrProfile Profile = new() { SecUserId = "sec1", UserName = "张三", MyUserId = "uid1" };

            public Task OpenLoginPageAsync() => Task.CompletedTask;
            public Task<byte[]> ScreenshotQrAsync() => Task.FromResult(new byte[] { 1, 2, 3 });
            public Task<QrLoginStatus> GetLoginStatusAsync() => Task.FromResult(Status);
            public Task<IReadOnlyList<BrowserCookie>> GetCookiesAsync() => Task.FromResult((IReadOnlyList<BrowserCookie>)Cookies);
            public Task<QrProfile> GetProfileAsync()
                => ThrowOnProfile ? throw new InvalidOperationException("profile fail") : Task.FromResult(Profile);
            public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        }

        private sealed class FakeFactory : IQrLoginBrowserFactory
        {
            public readonly Queue<FakeBrowser> Queue = new();
            public readonly List<FakeBrowser> Created = new();
            public FakeFactory(params FakeBrowser[] browsers)
            {
                foreach (var b in browsers) Queue.Enqueue(b);
            }
            public Task<IQrLoginBrowser> CreateAsync()
            {
                var b = Queue.Count > 0 ? Queue.Dequeue() : new FakeBrowser();
                Created.Add(b);
                return Task.FromResult((IQrLoginBrowser)b);
            }
        }

        private static (DouyinQrLoginService svc, Func<DateTime> _, Action<TimeSpan> advance) NewService(
            IQrLoginBrowserFactory factory)
        {
            var now = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
            Func<DateTime> clock = () => now;
            var svc = new DouyinQrLoginService(factory, clock);
            void Advance(TimeSpan by) => now = now.Add(by);
            return (svc, clock, Advance);
        }

        [Fact]
        public async Task Start_ReturnsSessionAndQr()
        {
            var (svc, _, _) = NewService(new FakeFactory(new FakeBrowser()));

            var r = await svc.StartAsync();

            Assert.False(string.IsNullOrWhiteSpace(r.SessionId));
            Assert.StartsWith("data:image/png;base64,", r.QrImageBase64);
        }

        [Fact]
        public async Task Start_Twice_DisposesPreviousBrowser()
        {
            var first = new FakeBrowser();
            var second = new FakeBrowser();
            var (svc, _, _) = NewService(new FakeFactory(first, second));

            await svc.StartAsync();
            await svc.StartAsync();

            Assert.True(first.Disposed);
            Assert.False(second.Disposed);
        }

        [Fact]
        public async Task Poll_UnknownId_ReturnsNotFound()
        {
            var (svc, _, _) = NewService(new FakeFactory());
            var r = await svc.PollAsync("nope");
            Assert.Equal("notfound", r.Status);
        }

        [Fact]
        public async Task Poll_Waiting_ReturnsWaiting()
        {
            var b = new FakeBrowser { Status = QrLoginStatus.Waiting };
            var (svc, _, _) = NewService(new FakeFactory(b));
            var start = await svc.StartAsync();

            var r = await svc.PollAsync(start.SessionId);

            Assert.Equal("waiting", r.Status);
            Assert.False(b.Disposed);
        }

        [Fact]
        public async Task Poll_Success_ReturnsCookiesAndProfile_AndDisposes()
        {
            var b = new FakeBrowser { Status = QrLoginStatus.Success };
            var (svc, _, _) = NewService(new FakeFactory(b));
            var start = await svc.StartAsync();

            var r = await svc.PollAsync(start.SessionId);

            Assert.Equal("success", r.Status);
            Assert.Equal("sessionid=s1", r.Cookies);
            Assert.Equal("sec1", r.SecUserId);
            Assert.Equal("张三", r.UserName);
            Assert.Equal("uid1", r.MyUserId);
            Assert.True(b.Disposed);
            // 成功后会话应被移除
            Assert.Equal("notfound", (await svc.PollAsync(start.SessionId)).Status);
        }

        [Fact]
        public async Task Poll_ProfileThrows_DegradesToCookiesOnly()
        {
            var b = new FakeBrowser { Status = QrLoginStatus.Success, ThrowOnProfile = true };
            var (svc, _, _) = NewService(new FakeFactory(b));
            var start = await svc.StartAsync();

            var r = await svc.PollAsync(start.SessionId);

            Assert.Equal("success", r.Status);
            Assert.Equal("sessionid=s1", r.Cookies);
            Assert.Null(r.SecUserId);
            Assert.True(b.Disposed);
        }

        [Fact]
        public async Task Poll_AfterQrTtl_ReturnsExpired_AndDisposes()
        {
            var b = new FakeBrowser { Status = QrLoginStatus.Waiting };
            var (svc, _, advance) = NewService(new FakeFactory(b));
            var start = await svc.StartAsync();

            advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(1)));
            var r = await svc.PollAsync(start.SessionId);

            Assert.Equal("expired", r.Status);
            Assert.True(b.Disposed);
        }

        [Fact]
        public async Task Cancel_DisposesAndSubsequentPollNotFound()
        {
            var b = new FakeBrowser();
            var (svc, _, _) = NewService(new FakeFactory(b));
            var start = await svc.StartAsync();

            await svc.CancelAsync(start.SessionId);

            Assert.True(b.Disposed);
            Assert.Equal("notfound", (await svc.PollAsync(start.SessionId)).Status);
        }

        [Fact]
        public async Task SweepExpired_DisposesOldSessions()
        {
            var b = new FakeBrowser();
            var (svc, _, advance) = NewService(new FakeFactory(b));
            await svc.StartAsync();

            advance(TimeSpan.FromMinutes(3).Add(TimeSpan.FromSeconds(1)));
            await svc.SweepExpiredAsync();

            Assert.True(b.Disposed);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter QrLoginServiceTests`
预期：编译失败（`DouyinQrLoginService` 未定义）。

- [ ] **步骤 3：编写实现**

创建 `service/qrlogin/DouyinQrLoginService.cs`：

```csharp
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using ClockSnowFlake;
using dy.net.utils;

namespace dy.net.service.qrlogin
{
    /// <summary>
    /// 扫码登录会话状态机。Singleton：跨 start/poll 请求持有会话。
    /// 单会话约束：新 start 会取消并释放旧会话。浏览器用完即关。
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

        // DI 用这个构造函数（Func<DateTime> 不在容器里，DI 会选参数最少且可解析的这个）
        public DouyinQrLoginService(IQrLoginBrowserFactory factory)
            : this(factory, () => DateTime.UtcNow)
        {
        }

        // 测试用：注入时钟
        public DouyinQrLoginService(IQrLoginBrowserFactory factory, Func<DateTime> clock)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        public async Task<QrStartResult> StartAsync()
        {
            await CancelAllAsync(); // 单会话约束

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

        public async Task<QrPollResult> PollAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var s))
                return new QrPollResult { Status = "notfound" };

            if (_clock() - s.CreatedAt > QrTtl)
            {
                await RemoveAndDisposeAsync(sessionId);
                return new QrPollResult { Status = "expired" };
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
                return new QrPollResult { Status = status.ToString().ToLowerInvariant() };

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

            await RemoveAndDisposeAsync(sessionId);

            return new QrPollResult
            {
                Status = "success",
                Cookies = cookies,
                SecUserId = profile?.SecUserId,
                UserName = profile?.UserName,
                MyUserId = profile?.MyUserId
            };
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
```

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter QrLoginServiceTests`
预期：PASS（9 个测试）。

- [ ] **步骤 5：Commit**

```bash
git add service/qrlogin/DouyinQrLoginService.cs tests/dy.net.Tests/QrLoginServiceTests.cs
git commit -m "feat(qrlogin): 会话状态机（单会话/过期/降级/清理）+ 单测"
```

---

## 任务 4：过期会话后台清理服务 QrLoginSessionReaper

定时调用 `SweepExpiredAsync()`，回收用户开了扫码却从不 poll（关标签页）导致泄漏的浏览器。逻辑已在任务 3 测过，这里仅包一层定时器，不再单测。

**文件：**
- 创建：`service/qrlogin/QrLoginSessionReaper.cs`

- [ ] **步骤 1：编写实现**

创建 `service/qrlogin/QrLoginSessionReaper.cs`：

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace dy.net.service.qrlogin
{
    /// <summary>每 30s 清理超龄扫码会话，兜底释放被遗弃的浏览器进程。</summary>
    public sealed class QrLoginSessionReaper : BackgroundService
    {
        private readonly DouyinQrLoginService _svc;

        public QrLoginSessionReaper(DouyinQrLoginService svc)
        {
            _svc = svc;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _svc.SweepExpiredAsync();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "QrLogin 会话清理异常");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
```

- [ ] **步骤 2：编译验证**

运行：`dotnet build dy.net.csproj`
预期：编译通过。

- [ ] **步骤 3：Commit**

```bash
git add service/qrlogin/QrLoginSessionReaper.cs
git commit -m "feat(qrlogin): 过期会话后台清理服务"
```

---

## 任务 5：PuppeteerSharp 真实浏览器实现 + 工厂

接 apt 装的系统 chromium 实现 `IQrLoginBrowser`。真实抖音登录页交互无法自动化断言，本任务靠编译 + 后续手动验证；选择器/URL 是唯一调优面，已抽成常量。

**文件：**
- 修改：`dy.net.csproj`（加 PuppeteerSharp 包引用）
- 创建：`service/qrlogin/PuppeteerQrLoginBrowser.cs`
- 创建：`service/qrlogin/PuppeteerQrLoginBrowserFactory.cs`

- [ ] **步骤 1：加 NuGet 引用**

在 `dy.net.csproj` 的 `PackageReference` 组内（约第 62-80 行那段）加一行：

```xml
    <PackageReference Include="PuppeteerSharp" Version="20.0.5" />
```

运行：`dotnet restore dy.net.csproj`
预期：还原成功。

- [ ] **步骤 2：实现浏览器工厂**

创建 `service/qrlogin/PuppeteerQrLoginBrowserFactory.cs`：

```csharp
using System;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace dy.net.service.qrlogin
{
    /// <summary>
    /// 用 apt 装的系统 chromium 启动无头浏览器。用完即关（每会话一个实例）。
    /// chromium 路径可用环境变量 CHROMIUM_PATH 覆盖，默认 /usr/bin/chromium。
    /// </summary>
    public sealed class PuppeteerQrLoginBrowserFactory : IQrLoginBrowserFactory
    {
        public async Task<IQrLoginBrowser> CreateAsync()
        {
            var executablePath = Environment.GetEnvironmentVariable("CHROMIUM_PATH");
            if (string.IsNullOrWhiteSpace(executablePath))
                executablePath = "/usr/bin/chromium";

            var launchOptions = new LaunchOptions
            {
                Headless = true,
                ExecutablePath = executablePath,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                    "--lang=zh-CN"
                }
            };

            var browser = await Puppeteer.LaunchAsync(launchOptions);
            return new PuppeteerQrLoginBrowser(browser);
        }
    }
}
```

- [ ] **步骤 3：实现浏览器**

创建 `service/qrlogin/PuppeteerQrLoginBrowser.cs`：

```csharp
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
```

> 说明：`GetLoginStatusAsync` 只区分 Waiting/Success（YAGNI，前端只需"等待扫码 / 成功"）。`GetProfileAsync` 尽力抓 sec_user_id + 昵称，MyUserId 抓不到留空——任务 3 已保证抓取失败/字段缺失时降级为仅回填 cookie。

- [ ] **步骤 4：编译验证**

运行：`dotnet build dy.net.csproj`
预期：编译通过。

- [ ] **步骤 5：Commit**

```bash
git add dy.net.csproj service/qrlogin/PuppeteerQrLoginBrowser.cs service/qrlogin/PuppeteerQrLoginBrowserFactory.cs
git commit -m "feat(qrlogin): PuppeteerSharp 系统 chromium 浏览器实现 + 工厂"
```

---

## 任务 6：DI 注册 + QrLoginController

显式注册三件套，暴露 start/poll/cancel 三个鉴权端点。

**文件：**
- 修改：`extension/ServiceExtension.cs`（新增 `AddQrLogin()` 扩展方法）
- 修改：`Program.cs:144` 附近（调用 `AddQrLogin()`）
- 创建：`Controllers/QrLoginController.cs`

- [ ] **步骤 1：加 DI 扩展方法**

在 `extension/ServiceExtension.cs` 的 `AddHttpClients` 方法之后，新增：

```csharp
        /// <summary>
        /// 扫码登录相关服务显式注册。
        /// 状态机须 Singleton（跨 start/poll 持有会话）；浏览器实现不入容器，由工厂 new。
        /// </summary>
        public static void AddQrLogin(this IServiceCollection services)
        {
            services.AddSingleton<dy.net.service.qrlogin.DouyinQrLoginService>();
            services.AddTransient<dy.net.service.qrlogin.IQrLoginBrowserFactory,
                                  dy.net.service.qrlogin.PuppeteerQrLoginBrowserFactory>();
            services.AddHostedService<dy.net.service.qrlogin.QrLoginSessionReaper>();
        }
```

- [ ] **步骤 2：在 Program.cs 调用**

在 `Program.cs` 第 144 行 `services.AddHttpClients();` 之后加一行：

```csharp
            services.AddQrLogin();
```

（确认 `Program.cs` 顶部已 `using dy.net.extension;`；若无则补上。）

- [ ] **步骤 3：编写控制器**

创建 `Controllers/QrLoginController.cs`：

```csharp
using System.Threading.Tasks;
using dy.net.extension;
using dy.net.service.qrlogin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace dy.net.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class QrLoginController : ControllerBase
    {
        private readonly DouyinQrLoginService _svc;

        public QrLoginController(DouyinQrLoginService svc)
        {
            _svc = svc;
        }

        public sealed class CancelDto
        {
            public string sessionId { get; set; }
        }

        /// <summary>启动扫码会话，返回二维码。</summary>
        [HttpPost("start")]
        public async Task<IActionResult> Start()
        {
            try
            {
                var r = await _svc.StartAsync();
                return ApiResult.Success(r);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "启动扫码登录失败");
                return ApiResult.Fail("启动扫码登录失败：浏览器组件异常，请稍后重试");
            }
        }

        /// <summary>轮询扫码状态；成功时返回 cookie 与账号信息。</summary>
        [HttpGet("poll")]
        public async Task<IActionResult> Poll([FromQuery] string sessionId)
        {
            var r = await _svc.PollAsync(sessionId);
            return ApiResult.Success(r);
        }

        /// <summary>取消/关闭扫码会话。</summary>
        [HttpPost("cancel")]
        public async Task<IActionResult> Cancel([FromBody] CancelDto dto)
        {
            await _svc.CancelAsync(dto?.sessionId);
            return ApiResult.Success();
        }
    }
}
```

> 注：`ApiResult` 与现有控制器同命名空间用法一致（`dy.net.extension`）。若 `ApiResult` 实际命名空间不同，按 `Controllers/ConfigController.cs` 顶部 using 对齐。

- [ ] **步骤 4：编译验证**

运行：`dotnet build dy.net.csproj`
预期：编译通过。

- [ ] **步骤 5：Commit**

```bash
git add extension/ServiceExtension.cs Program.cs Controllers/QrLoginController.cs
git commit -m "feat(qrlogin): DI 注册 + QrLoginController(start/poll/cancel)"
```

---

## 任务 7：前端 API 封装 + QrLoginModal 组件

**文件：**
- 修改：`app/src/store/coreapi.ts`（加 3 个方法并 export）
- 创建：`app/src/pages/cok/QrLoginModal.vue`

- [ ] **步骤 1：加 API 方法**

在 `app/src/store/coreapi.ts` 中，`deleteCookie` 函数之后新增三个方法（与现有 `http.request` 风格一致）：

```typescript
  // ===== 扫码登录 =====
  async function QrLoginStart() {
    return http.request<any, Response<any>>('/api/qrlogin/start', 'post_json', {}).then(r => r);
  }
  async function QrLoginPoll(sessionId: string) {
    return http
      .request<any, Response<any>>('/api/qrlogin/poll?sessionId=' + encodeURIComponent(sessionId), 'get')
      .then(r => r);
  }
  async function QrLoginCancel(sessionId: string) {
    return http.request<any, Response<any>>('/api/qrlogin/cancel', 'post_json', { sessionId }).then(r => r);
  }
```

并在文件末尾 `return { ... }` 的导出对象里加上（与 `UpdateConfig` 等并列）：

```typescript
    QrLoginStart,
    QrLoginPoll,
    QrLoginCancel,
```

- [ ] **步骤 2：编写扫码弹窗组件**

创建 `app/src/pages/cok/QrLoginModal.vue`：

```vue
<template>
  <a-modal
    :visible="visible"
    title="扫码登录抖音"
    :footer="null"
    :maskClosable="false"
    width="360px"
    @cancel="handleClose"
  >
    <div style="text-align: center; padding: 12px 0;">
      <a-spin v-if="loading" tip="正在启动浏览器..." />

      <template v-else>
        <div v-if="qrImage" style="position: relative; display: inline-block;">
          <img :src="qrImage" alt="登录二维码" style="width: 220px; height: 220px;" />
          <div
            v-if="statusText === 'expired'"
            style="position:absolute;inset:0;background:rgba(255,255,255,.9);display:flex;flex-direction:column;align-items:center;justify-content:center;"
          >
            <span style="margin-bottom:8px;">二维码已失效</span>
            <a-button type="primary" size="small" @click="start">刷新二维码</a-button>
          </div>
        </div>

        <p style="margin-top: 12px; color: #666;">{{ hint }}</p>

        <a-button v-if="statusText === 'error'" type="primary" @click="start">重试</a-button>
      </template>
    </div>
  </a-modal>
</template>

<script lang="ts" setup>
import { ref, watch, onBeforeUnmount } from 'vue';
import { message } from 'ant-design-vue';
import { useApiStore } from '@/store';

const props = defineProps<{ visible: boolean }>();
const emit = defineEmits<{
  (e: 'update:visible', v: boolean): void;
  (e: 'success', data: { cookies: string; secUserId?: string; userName?: string; myUserId?: string }): void;
}>();

const loading = ref(false);
const qrImage = ref('');
const sessionId = ref('');
const statusText = ref('');
let timer: ReturnType<typeof setInterval> | null = null;

const hint = ref('请用抖音 App 扫描二维码并确认登录');

function stopPoll() {
  if (timer) {
    clearInterval(timer);
    timer = null;
  }
}

async function start() {
  stopPoll();
  loading.value = true;
  qrImage.value = '';
  statusText.value = '';
  hint.value = '请用抖音 App 扫描二维码并确认登录';
  try {
    const res = await useApiStore().QrLoginStart();
    if (res.code === 0 && res.data?.sessionId) {
      sessionId.value = res.data.sessionId;
      qrImage.value = res.data.qrImageBase64;
      statusText.value = 'waiting';
      timer = setInterval(poll, 1500);
    } else {
      statusText.value = 'error';
      hint.value = res.message || '启动失败';
    }
  } catch (e: any) {
    statusText.value = 'error';
    hint.value = e?.data?.erro || e?.message || '启动扫码失败';
  } finally {
    loading.value = false;
  }
}

async function poll() {
  if (!sessionId.value) return;
  try {
    const res = await useApiStore().QrLoginPoll(sessionId.value);
    if (res.code !== 0 || !res.data) return;
    const st = res.data.status as string;
    statusText.value = st;
    if (st === 'success') {
      stopPoll();
      message.success('扫码登录成功');
      emit('success', {
        cookies: res.data.cookies,
        secUserId: res.data.secUserId,
        userName: res.data.userName,
        myUserId: res.data.myUserId,
      });
      emit('update:visible', false);
    } else if (st === 'expired') {
      stopPoll();
      hint.value = '二维码已失效，请刷新';
    } else if (st === 'error' || st === 'notfound') {
      stopPoll();
      hint.value = '登录会话异常，请重试';
    }
  } catch {
    /* 单次轮询失败忽略，等下次 */
  }
}

async function handleClose() {
  stopPoll();
  if (sessionId.value) {
    try { await useApiStore().QrLoginCancel(sessionId.value); } catch { /* ignore */ }
    sessionId.value = '';
  }
  emit('update:visible', false);
}

watch(
  () => props.visible,
  (v) => {
    if (v) start();
    else { stopPoll(); }
  }
);

onBeforeUnmount(() => {
  stopPoll();
});
</script>
```

- [ ] **步骤 3：前端构建验证**

运行：`cd app && pnpm exec vite build`
预期：构建成功（无类型/编译错误）。

- [ ] **步骤 4：Commit**

```bash
git add app/src/store/coreapi.ts app/src/pages/cok/QrLoginModal.vue
git commit -m "feat(qrlogin): 前端扫码 API 封装 + QrLoginModal 弹窗组件"
```

---

## 任务 8：CookieTable 接线（新增回填 + 过期重扫）

把扫码弹窗接进现有 Cookie 管理页：新增流程回填表单；cookie 异常行提供"重新扫码"直接更新落库。

**文件：**
- 修改：`app/src/pages/cok/CookieTable.vue`

- [ ] **步骤 1：引入组件与状态**

在 `CookieTable.vue` 的 `<script setup>` 中引入并声明状态（放在已有 `import { Modal } from 'ant-design-vue';` 等 import 附近）：

```typescript
import QrLoginModal from './QrLoginModal.vue';

// 扫码弹窗状态
const qrVisible = ref(false);
// 'new' = 新增回填表单；'reset' = 过期重扫某条记录
const qrMode = ref<'new' | 'reset'>('new');
const qrTargetRecord = ref<DataItem | null>(null);

function openQrForNew() {
  qrMode.value = 'new';
  qrTargetRecord.value = null;
  qrVisible.value = true;
}

function openQrForReset(record: DataItem) {
  qrMode.value = 'reset';
  qrTargetRecord.value = record;
  qrVisible.value = true;
}

function onQrSuccess(data: { cookies: string; secUserId?: string; userName?: string; myUserId?: string }) {
  if (qrMode.value === 'new') {
    // 回填新增表单，路径等其余字段仍由用户填写后保存
    form.cookies = data.cookies;
    if (data.secUserId) form.secUserId = data.secUserId;
    if (data.userName) form.userName = data.userName;
    showModal.value = true;
    message.success('已获取 Cookie，请补全存储路径后保存');
  } else if (qrMode.value === 'reset' && qrTargetRecord.value) {
    // 过期重扫：用该记录 + 新 cookie 直接更新落库
    const payload: any = { ...qrTargetRecord.value, cookies: data.cookies };
    if (data.secUserId) payload.secUserId = data.secUserId;
    useApiStore()
      .UpdateConfig(payload)
      .then((res) => {
        if (res.code === 0) {
          message.success('Cookie 已更新，同步任务将重启');
          GetRecords();
        } else {
          message.error('更新失败：' + (res.message || '未知错误'));
        }
      });
  }
}
```

> 注：`form`、`showModal`、`GetRecords`、`useApiStore`、`message` 均为 `CookieTable.vue` 现有符号（见文件现状）。`DataItem` 已含 `cookies/secUserId/userName` 字段。

- [ ] **步骤 2：加入口按钮**

在新增按钮（`addNew()` 触发处）附近的模板里，新增一个"扫码登录"按钮：

```vue
    <a-button type="primary" style="margin-left: 8px;" @click="openQrForNew">扫码登录获取Cookie</a-button>
```

在行操作区（现有"停止/开启同步""删除"等按钮所在的表格列）新增一个按钮，仅当该行同步异常时展示（`status !== 1` 视为需要重扫；如有 `StatusMsg` 字段也可据其判断）：

```vue
    <a-button type="link" size="small" @click="openQrForReset(record)">重新扫码</a-button>
```

- [ ] **步骤 3：挂载弹窗**

在模板根节点内（与其它 modal 并列处）加：

```vue
  <qr-login-modal v-model:visible="qrVisible" @success="onQrSuccess" />
```

- [ ] **步骤 4：前端构建验证**

运行：`cd app && pnpm exec vite build`
预期：构建成功。

- [ ] **步骤 5：Commit**

```bash
git add app/src/pages/cok/CookieTable.vue
git commit -m "feat(qrlogin): CookieTable 接入扫码（新增回填 + 过期重扫更新）"
```

---

## 任务 9：Docker 装系统 chromium

给运行时镜像装 chromium，并设 `CHROMIUM_PATH`。与现有 ffmpeg 同一 apt 模式。

**文件：**
- 修改：`Dockerfile`
- 修改：`Dockerfile.ci`

- [ ] **步骤 1：本地 Dockerfile**

在 `Dockerfile` 里现有装 ffmpeg 的 `apt-get install` 行里加上 `chromium`，即把

```dockerfile
    apt-get install -y --no-install-recommends ffmpeg && \
```

改为

```dockerfile
    apt-get install -y --no-install-recommends ffmpeg chromium && \
```

并在 `ENV TZ=Asia/Shanghai` 附近加一行：

```dockerfile
ENV CHROMIUM_PATH=/usr/bin/chromium
```

- [ ] **步骤 2：Dockerfile.ci 的 final 阶段**

在 `Dockerfile.ci` 第 3 阶段（`FROM ... AS final`）的 apt 安装处，把

```dockerfile
    && apt-get install -y --no-install-recommends ffmpeg \
```

改为

```dockerfile
    && apt-get install -y --no-install-recommends ffmpeg chromium \
```

并在该阶段 `ENV TZ=Asia/Shanghai` 附近加：

```dockerfile
ENV CHROMIUM_PATH=/usr/bin/chromium
```

- [ ] **步骤 3：构建验证**

运行：`docker build -f Dockerfile.ci -t dysync:qrlogin-test .`
预期：构建成功；镜像内 `chromium --version` 可用（可 `docker run --rm dysync:qrlogin-test chromium --version` 抽查，若 entrypoint 覆盖则改用 `docker run --rm --entrypoint chromium dysync:qrlogin-test --version`）。

- [ ] **步骤 4：Commit**

```bash
git add Dockerfile Dockerfile.ci
git commit -m "build(qrlogin): 运行时镜像装系统 chromium + CHROMIUM_PATH"
```

---

## 任务 10：端到端手动验证

自动化测已覆盖状态机与拼装纯函数；真实抖音登录与前端交互靠手动。

- [ ] **步骤 1：起容器**

```bash
docker run --rm -p 10101:10101 dysync:qrlogin-test
```

- [ ] **步骤 2：新增账号扫码**

1. 浏览器打开系统、登录管理员，进 Cookie 管理页。
2. 点"扫码登录获取Cookie"→ 弹窗出现二维码。
3. 用抖音 App 扫码并确认。
4. 预期：弹窗关闭、提示"已获取 Cookie"，新增表单里 `cookies` 已回填，`secUserId/userName` 尽量回填。
5. 补全存储路径 → 保存 → 列表出现新账号、状态"正常"，同步任务重启。

- [ ] **步骤 3：过期重扫**

1. 对某条 cookie 异常的账号点"重新扫码"。
2. 扫码成功后预期：提示"Cookie 已更新"，列表刷新，该账号恢复正常。

- [ ] **步骤 4：异常路径抽查**

- 二维码放置 >2min 不扫 → 弹窗显示"二维码已失效" + 刷新按钮，点刷新出新码。
- 扫码中途关闭弹窗 → 后端会话被 cancel（无残留 chromium 进程；可 `docker exec <id> ps aux | grep chromium` 抽查）。
- 抓账号信息失败时仍能仅回填 cookie（观察日志"降级为仅返回 cookie"）。

- [ ] **步骤 5：全量回归**

运行：`dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
预期：全部 PASS（含既有测试 + 新增 QrCookieBuilderTests、QrLoginServiceTests）。

---

## 附：规格覆盖度自检

| 规格要求 | 覆盖任务 |
|---|---|
| 落地①新增回填表单 | 任务 8 步骤 1（`onQrSuccess` new 分支）+ 复用 `/config/update` |
| 落地②过期重扫直接更新落库 | 任务 8 步骤 1（reset 分支调 `UpdateConfig`）|
| 浏览器用完即关 | 任务 3（成功/取消/过期均 dispose）+ 任务 4 清理 + 任务 5 DisposeAsync |
| apt chromium + PuppeteerSharp | 任务 5（包）+ 任务 9（apt）|
| 自动抓账号信息回填 | 任务 5 `GetProfileAsync` + 任务 3 成功分支 + 任务 8 回填 |
| 二维码截图获取 | 任务 5 `ScreenshotQrAsync` |
| 单会话约束 | 任务 3 `CancelAllAsync` + 测试 `Start_Twice_DisposesPrevious` |
| 二维码~2min/总~3min 超时 | 任务 3 `QrTtl`/`SessionMaxAge` + 对应测试 |
| 鉴权=管理员登录 | 任务 6 控制器 `[Authorize]` |
| 抓取失败降级 | 任务 3 `Poll_ProfileThrows_Degrades` 测试 |
| 状态机单测 + 纯函数单测 | 任务 1、任务 3 |
| Docker 镜像加 chromium | 任务 9 |
```
