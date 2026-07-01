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
