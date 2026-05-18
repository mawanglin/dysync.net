# Auth Credential + JWT Redesign Implementation Plan (#1 + #2)

> **For agentic workers:** execute task-by-task; build (`dotnet build dy.net.csproj`) after each task; runtime smoke at the end.

**Goal:** Replace unsalted MD5 password storage with PBKDF2 (migrate-on-login, no lockout), and replace the ephemeral process-GUID JWT signing key with a stable persisted key + enforced issuer/audience.

**Architecture:** Two new static utilities (`PasswordUtil`, `JwtKeyProvider`) encapsulate the crypto; existing call sites are rewired to them. Legacy MD5 hashes are detected by shape (`^[0-9a-fA-F]{32}$`) and transparently re-hashed on successful login. `Md5Util.Md5` is retained (still used for the share-link key) but no longer used for passwords; the dead `JWT_TOKEN_KEY` field is removed.

**Tech Stack:** .NET 6, `Microsoft.AspNetCore.Identity.PasswordHasher` (in-box, no new package), `System.Security.Cryptography.RandomNumberGenerator`.

**Verification:** build = 0 errors; runtime smoke (fresh DB): default-cred login 200 + token, token validates against enforced issuer/audience on a protected endpoint, wrong password fails, restart keeps tokens valid (persisted key).

**Residual flagged (not changed here):** the documented default credential `douyin/douyin2026` is kept (README onboarding) — now PBKDF2-hashed. Forcing a random initial password is a product decision left to the user.

---

### Task 1: `utils/PasswordUtil.cs` — PBKDF2 hashing + legacy MD5 verify

**Files:** Create `utils/PasswordUtil.cs`

- [ ] Create the file:

```csharp
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

        public static string Hash(string password)
        {
            return _hasher.HashPassword(_dummy, password ?? string.Empty);
        }

        public static bool IsLegacyMd5(string stored)
        {
            return !string.IsNullOrEmpty(stored) && _md5Shape.IsMatch(stored);
        }

        /// <summary>验证密码。兼容历史 MD5。</summary>
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
```

- [ ] Build: `dotnet build dy.net.csproj -c Debug` → 0 errors.
- [ ] Commit: `fix(security): add PasswordUtil (PBKDF2 + legacy MD5 compat) (#1)`

---

### Task 2: `utils/JwtKeyProvider.cs` — stable persisted signing key

**Files:** Create `utils/JwtKeyProvider.cs`

- [ ] Create the file:

```csharp
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
```

- [ ] Build → 0 errors.
- [ ] Commit: `fix(security): add persisted JwtKeyProvider (env or db/jwt.key) (#2)`

---

### Task 3: Rewire JWT validation + generation

**Files:** Modify `extension/ServiceExtension.cs:54,226-234`, `Controllers/AuthController.cs:94-116`

- [ ] `extension/ServiceExtension.cs:54` replace
  `_jwtKeyBytes = Encoding.ASCII.GetBytes(Md5Util.JWT_TOKEN_KEY);`
  with
  `_jwtKeyBytes = JwtKeyProvider.GetKeyBytes();`

- [ ] `extension/ServiceExtension.cs` TokenValidationParameters block — replace:

```csharp
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(60)
```

with:

```csharp
                    ValidateIssuer = true,
                    ValidIssuer = JwtKeyProvider.Issuer,
                    ValidateAudience = true,
                    ValidAudience = JwtKeyProvider.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(60)
```

- [ ] `Controllers/AuthController.cs` GenerateJwtToken — replace:

```csharp
            var k = Md5Util.JWT_TOKEN_KEY;
            var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(k));
```
with
```csharp
            var key = new SymmetricSecurityKey(JwtKeyProvider.GetKeyBytes());
```

  and replace:
```csharp
                issuer: IdGener.GetLong().ToString(),
                audience: IdGener.GetLong().ToString(),
```
with
```csharp
                issuer: JwtKeyProvider.Issuer,
                audience: JwtKeyProvider.Audience,
```

- [ ] Build → 0 errors.
- [ ] Commit: `fix(security): use stable JWT key + enforce issuer/audience (#2)`

---

### Task 4: Rewire password storage + migrate-on-login

**Files:** Modify `Controllers/AuthController.cs:78-83`, `repository/AdminUserRepository.cs:32,38,103,112-117`, `service/AdminUserService.cs`

- [ ] Add a passthrough update to `service/AdminUserService.cs` (after `UpdateAvatar`):

```csharp
        public async Task<bool> UpdateUser(model.entity.AdminUserInfo user)
        {
            return await _userRepository.UpdateAsync(user);
        }
```

- [ ] `Controllers/AuthController.cs` Login — replace:

```csharp
                    if (user.Password == Md5Util.Md5(loginUserInfo.Password))
                    {

                        var tokenString = GenerateJwtToken(user.UserName);
```
with:
```csharp
                    if (PasswordUtil.Verify(user.Password, loginUserInfo.Password))
                    {
                        // 登录成功且仍是历史 MD5 → 透明升级为 PBKDF2
                        if (PasswordUtil.IsLegacyMd5(user.Password))
                        {
                            user.Password = PasswordUtil.Hash(loginUserInfo.Password);
                            await _userService.UpdateUser(user);
                        }
                        var tokenString = GenerateJwtToken(user.UserName);
```

- [ ] `repository/AdminUserRepository.cs:32` replace `if (loginUser?.OldPassword?.Md5() != user.Password)` with `if (!PasswordUtil.Verify(user.Password, loginUser?.OldPassword))`

- [ ] `repository/AdminUserRepository.cs:38` replace `var newpassword = loginUser.Password.Md5();` with `var newpassword = PasswordUtil.Hash(loginUser.Password);`

- [ ] `repository/AdminUserRepository.cs:103` replace `userInfo.Password = Md5Util.Md5(userInfo.Password);` with `userInfo.Password = PasswordUtil.Hash(userInfo.Password);`

- [ ] `repository/AdminUserRepository.cs` ResetPwd — replace:

```csharp
            if (string.IsNullOrWhiteSpace(pwd)) pwd = "douyin2026";
            var password = Md5Util.Md5(pwd);
```
with:
```csharp
            if (string.IsNullOrWhiteSpace(pwd))
            {
                Serilog.Log.Warning("ResetPwd 收到空密码，已拒绝（不再静默重置为已知默认密码）");
                return false;
            }
            var password = PasswordUtil.Hash(pwd);
```

- [ ] Build → 0 errors.
- [ ] Commit: `fix(security): store passwords with PBKDF2, migrate MD5 on login (#1)`

---

### Task 5: Remove dead ephemeral JWT key field

**Files:** Modify `utils/Md5Util.cs:10`

- [ ] Delete the line `public static string JWT_TOKEN_KEY = "dysync.net-key-" + IdGener.GetGuid();` and the now-unused `using ClockSnowFlake;` if `IdGener` is no longer referenced in the file.
- [ ] Build → 0 errors (confirms no remaining references).
- [ ] Commit: `chore(security): remove dead ephemeral JWT_TOKEN_KEY field (#2)`

---

### Task 6: Runtime smoke

- [ ] Fresh-DB run (`DOTNET_ROLL_FORWARD=LatestMajor dotnet run ... /tmp/dyauth`):
  1. `POST /api/Auth/Login` douyin/douyin2026 → 200 + token (PBKDF2 seed verified).
  2. Protected endpoint with token → 200 (issuer/audience now enforced & valid).
  3. Wrong password → fail json.
  4. Stop app, restart, login again, reuse a token minted before restart on a protected endpoint → still 200 (persisted key; previously would 401 after restart).
  5. Confirm `db/jwt.key` file was created.
- [ ] If all pass, push branch.

## Self-Review
- #1 covered: Tasks 1,4,5. #2 covered: Tasks 2,3,5.
- No placeholders; all code shown.
- Type consistency: `PasswordUtil.Verify/Hash/IsLegacyMd5`, `JwtKeyProvider.GetKeyBytes/Issuer/Audience`, `AdminUserService.UpdateUser` consistent across tasks. `Md5()` extension retained for legacy path + share key.
- Risk: Task 3 enforces issuer/audience — any token minted by the OLD code (random issuer) becomes invalid; acceptable (old keys were ephemeral anyway, users re-login). Smoke step 4 validates new tokens survive restart.
