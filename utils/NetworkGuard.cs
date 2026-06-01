using System.Net;
using System.Net.Sockets;

namespace dy.net.utils
{
    /// <summary>
    /// 网络来源判定。用于把匿名工具端点（cookie 重置工具 dy.cookie.exe）限制为
    /// 仅内网 / 本机来源，堵住公网匿名访问面。纯函数，便于特征化测试。
    /// </summary>
    public static class NetworkGuard
    {
        /// <summary>
        /// 是否为环回或私有（内网 / 链路本地 / ULA）地址。null 视为不可信 → false。
        /// </summary>
        public static bool IsPrivateOrLoopback(IPAddress ip)
        {
            if (ip == null)
                return false;

            if (IPAddress.IsLoopback(ip)) // 127.0.0.0/8、::1
                return true;

            // IPv4-mapped IPv6（::ffff:a.b.c.d）按 IPv4 处理
            if (ip.IsIPv4MappedToIPv6)
                ip = ip.MapToIPv4();

            var bytes = ip.GetAddressBytes();

            if (ip.AddressFamily == AddressFamily.InterNetwork) // IPv4
            {
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                // 169.254.0.0/16 链路本地
                if (bytes[0] == 169 && bytes[1] == 254) return true;
                return false;
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6) // IPv6
            {
                // fc00::/7 唯一本地地址（ULA）
                if ((bytes[0] & 0xFE) == 0xFC) return true;
                // fe80::/10 链路本地
                if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true;
                return false;
            }

            return false;
        }
    }
}
