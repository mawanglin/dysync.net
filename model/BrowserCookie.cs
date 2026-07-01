namespace dy.net.utils
{
    /// <summary>
    /// 浏览器 cookie 的纯数据表示（脱离 PuppeteerSharp 类型，便于纯函数单测）。
    /// </summary>
    public sealed record BrowserCookie(string Name, string Value, string Domain);
}
