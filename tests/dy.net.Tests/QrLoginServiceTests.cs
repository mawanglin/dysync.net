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
            public bool ThrowOnStatus = false;
            public bool ThrowOnProfile = false;
            public bool Disposed = false;
            public List<BrowserCookie> Cookies = new() { new BrowserCookie("sessionid", "s1", ".douyin.com") };
            public QrProfile Profile = new() { SecUserId = "sec1", UserName = "张三", MyUserId = "uid1" };

            public Task OpenLoginPageAsync() => Task.CompletedTask;
            public Task<byte[]> ScreenshotQrAsync() => Task.FromResult(new byte[] { 1, 2, 3 });
            public Task<byte[]> ScreenshotFullAsync() => Task.FromResult(new byte[] { 4, 5, 6 });
            public Task<QrLoginStatus> GetLoginStatusAsync()
                => ThrowOnStatus ? throw new InvalidOperationException("status fail") : Task.FromResult(Status);
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

        private static (DouyinQrLoginService svc, Action<TimeSpan> advance) NewService(
            IQrLoginBrowserFactory factory)
        {
            var now = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
            Func<DateTime> clock = () => now;
            var svc = new DouyinQrLoginService(factory, clock);
            void Advance(TimeSpan by) => now = now.Add(by);
            return (svc, Advance);
        }

        [Fact]
        public async Task Start_ReturnsSessionAndQr()
        {
            var (svc, _) = NewService(new FakeFactory(new FakeBrowser()));

            var r = await svc.StartAsync();

            Assert.False(string.IsNullOrWhiteSpace(r.SessionId));
            Assert.StartsWith("data:image/png;base64,", r.QrImageBase64);
        }

        [Fact]
        public async Task Start_Twice_DisposesPreviousBrowser()
        {
            var first = new FakeBrowser();
            var second = new FakeBrowser();
            var (svc, _) = NewService(new FakeFactory(first, second));

            await svc.StartAsync();
            await svc.StartAsync();

            Assert.True(first.Disposed);
            Assert.False(second.Disposed);
        }

        [Fact]
        public async Task Poll_UnknownId_ReturnsNotFound()
        {
            var (svc, _) = NewService(new FakeFactory());
            var r = await svc.PollAsync("nope");
            Assert.Equal("notfound", r.Status);
        }

        [Fact]
        public async Task Poll_Waiting_ReturnsWaiting()
        {
            var b = new FakeBrowser { Status = QrLoginStatus.Waiting };
            var (svc, _) = NewService(new FakeFactory(b));
            var start = await svc.StartAsync();

            var r = await svc.PollAsync(start.SessionId);

            Assert.Equal("waiting", r.Status);
            Assert.False(b.Disposed);
        }

        [Fact]
        public async Task Poll_Success_ReturnsCookiesAndProfile_AndDisposes()
        {
            var b = new FakeBrowser { Status = QrLoginStatus.Success };
            var (svc, _) = NewService(new FakeFactory(b));
            var start = await svc.StartAsync();

            var r = await svc.PollAsync(start.SessionId);

            Assert.Equal("success", r.Status);
            Assert.Equal("sessionid=s1", r.Cookies);
            Assert.Equal("sec1", r.SecUserId);
            Assert.Equal("张三", r.UserName);
            Assert.Equal("uid1", r.MyUserId);
            Assert.True(b.Disposed);
            Assert.Equal("notfound", (await svc.PollAsync(start.SessionId)).Status);
        }

        [Fact]
        public async Task Poll_ProfileThrows_DegradesToCookiesOnly()
        {
            var b = new FakeBrowser { Status = QrLoginStatus.Success, ThrowOnProfile = true };
            var (svc, _) = NewService(new FakeFactory(b));
            var start = await svc.StartAsync();

            var r = await svc.PollAsync(start.SessionId);

            Assert.Equal("success", r.Status);
            Assert.Equal("sessionid=s1", r.Cookies);
            Assert.Null(r.SecUserId);
            Assert.True(b.Disposed);
        }

        [Fact]
        public async Task Poll_StatusThrows_ReturnsError()
        {
            var b = new FakeBrowser { ThrowOnStatus = true };
            var (svc, _) = NewService(new FakeFactory(b));
            var start = await svc.StartAsync();

            var r = await svc.PollAsync(start.SessionId);

            Assert.Equal("error", r.Status);
        }

        [Fact]
        public async Task Poll_AfterQrTtl_ReturnsExpired_AndDisposes()
        {
            var b = new FakeBrowser { Status = QrLoginStatus.Waiting };
            var (svc, advance) = NewService(new FakeFactory(b));
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
            var (svc, _) = NewService(new FakeFactory(b));
            var start = await svc.StartAsync();

            await svc.CancelAsync(start.SessionId);

            Assert.True(b.Disposed);
            Assert.Equal("notfound", (await svc.PollAsync(start.SessionId)).Status);
        }

        [Fact]
        public async Task SweepExpired_DisposesOldSessions()
        {
            var b = new FakeBrowser();
            var (svc, advance) = NewService(new FakeFactory(b));
            await svc.StartAsync();

            advance(TimeSpan.FromMinutes(3).Add(TimeSpan.FromSeconds(1)));
            await svc.SweepExpiredAsync();

            Assert.True(b.Disposed);
        }

        [Fact]
        public async Task SweepExpired_KeepsYoungSessions()
        {
            var b = new FakeBrowser();
            var (svc, advance) = NewService(new FakeFactory(b));
            var start = await svc.StartAsync();

            advance(TimeSpan.FromMinutes(1));
            await svc.SweepExpiredAsync();

            Assert.False(b.Disposed);
            Assert.Equal("waiting", (await svc.PollAsync(start.SessionId)).Status);
        }
    }
}
