using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace dy.net.utils
{
    /// <summary>
    /// 密码哈希工具：新密码用 PBKDF2（ASP.NET Core 内置 PasswordHasher），
    /// 兼容历史无盐 MD5 并在登录成功时透明升级。
    /// </summary>
    public static class PasswordUtil
    {
        private static readonly PasswordHasher<object> _hasher = new();
        private static readonly object _dummy = new();
        private static readonly Regex _md5Shape = new("^[0-9a-fA-F]{32}$", RegexOptions.Compiled);

        /// <summary>用 PBKDF2 生成带盐的密码哈希。</summary>
        public static string Hash(string password)
        {
            return _hasher.HashPassword(_dummy, password ?? string.Empty);
        }

        /// <summary>判断存储值是否为历史无盐 MD5（32 位十六进制）。</summary>
        public static bool IsLegacyMd5(string stored)
        {
            return !string.IsNullOrEmpty(stored) && _md5Shape.IsMatch(stored);
        }

        /// <summary>验证密码，兼容历史 MD5。</summary>
        public static bool Verify(string stored, string provided)
        {
            if (string.IsNullOrEmpty(stored) || provided == null)
                return false;

            if (IsLegacyMd5(stored))
                return string.Equals(stored, provided.Md5(), System.StringComparison.OrdinalIgnoreCase);

            var result = _hasher.VerifyHashedPassword(_dummy, stored, provided);
            return result == PasswordVerificationResult.Success
                || result == PasswordVerificationResult.SuccessRehashNeeded;
        }
    }
}
