using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace dy.net.utils
{
    /// <summary>
    /// JWT 签名密钥提供器：优先环境变量 DYSYNC_JWT_KEY，否则在 db/jwt.key
    /// 持久化一个 32 字节随机密钥（重启/多实例共享同一文件即稳定）。
    /// </summary>
    public static class JwtKeyProvider
    {
        public const string Issuer = "dysync.net";
        public const string Audience = "dysync.net";

        private static readonly object _lock = new();
        private static byte[] _cached;

        /// <summary>获取稳定的签名密钥字节。</summary>
        public static byte[] GetKeyBytes()
        {
            if (_cached != null) return _cached;
            lock (_lock)
            {
                if (_cached != null) return _cached;

                var env = Environment.GetEnvironmentVariable("DYSYNC_JWT_KEY");
                if (!string.IsNullOrWhiteSpace(env) && env.Length >= 32)
                {
                    _cached = Encoding.UTF8.GetBytes(env);
                    return _cached;
                }

                var dir = Path.Combine(AppContext.BaseDirectory, "db");
                var keyFile = Path.Combine(dir, "jwt.key");
                try
                {
                    if (File.Exists(keyFile))
                    {
                        var b64 = File.ReadAllText(keyFile).Trim();
                        var bytes = Convert.FromBase64String(b64);
                        if (bytes.Length >= 32)
                        {
                            _cached = bytes;
                            return _cached;
                        }
                    }
                    Directory.CreateDirectory(dir);
                    var fresh = RandomNumberGenerator.GetBytes(32);
                    File.WriteAllText(keyFile, Convert.ToBase64String(fresh));
                    _cached = fresh;
                    return _cached;
                }
                catch
                {
                    // 文件不可用时退回进程内随机密钥（重启会失效，但不至于崩溃）
                    _cached = RandomNumberGenerator.GetBytes(32);
                    return _cached;
                }
            }
        }
    }
}
