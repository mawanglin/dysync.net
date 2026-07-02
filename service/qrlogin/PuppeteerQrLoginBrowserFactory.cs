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
                    "--lang=zh-CN",
                    // 隐藏自动化特征，降低抖音风控弹滑块验证的概率
                    "--disable-blink-features=AutomationControlled",
                    // 给足窗口尺寸，避免登录面板被挤成很小一块
                    "--window-size=1400,1000"
                }
            };

            var browser = await Puppeteer.LaunchAsync(launchOptions);
            return new PuppeteerQrLoginBrowser(browser);
        }
    }
}
