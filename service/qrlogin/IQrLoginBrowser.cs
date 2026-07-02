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

        /// <summary>整页截图（实时预览用），返回 JPEG 字节。</summary>
        Task<byte[]> ScreenshotFullAsync();

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
