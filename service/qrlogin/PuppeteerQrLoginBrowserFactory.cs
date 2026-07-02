using System;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace dy.net.service.qrlogin
{
    /// <summary>
    /// 用 apt 装的系统 chromium 启动浏览器。用完即关（每会话一个实例）。
    /// chromium 路径可用环境变量 CHROMIUM_PATH 覆盖，默认 /usr/bin/chromium。
    /// 有 DISPLAY（容器里 xvfb 提供虚拟屏）时用「有头」模式——抖音风控对 headless 权重极高，
    /// 有头 + 虚拟屏更像真人、更易通过；无 DISPLAY（本机/无 xvfb）时回退无头。
    /// </summary>
    public sealed class PuppeteerQrLoginBrowserFactory : IQrLoginBrowserFactory
    {
        public async Task<IQrLoginBrowser> CreateAsync()
        {
            var executablePath = Environment.GetEnvironmentVariable("CHROMIUM_PATH");
            if (string.IsNullOrWhiteSpace(executablePath))
                executablePath = "/usr/bin/chromium";

            var hasDisplay = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));

            var launchOptions = new LaunchOptions
            {
                Headless = !hasDisplay, // 有虚拟屏 → 有头
                ExecutablePath = executablePath,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                    "--lang=zh-CN"
                    // 注：--disable-blink-features / --window-size 曾导致该环境 chromium 连接超时，已撤回。
                    // 反检测改由页面层的 UA + navigator.webdriver 覆盖处理（见 PuppeteerQrLoginBrowser）。
                }
            };

            var browser = await Puppeteer.LaunchAsync(launchOptions);
            return new PuppeteerQrLoginBrowser(browser);
        }
    }
}
