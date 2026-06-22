# Spec: 服务端强制首登改密 + 工具端点 IP 门控加固（review 2026-06-07 #1/#2）

## 背景

2026-06-07 复审发现两处「安全控制存在但被绕过」：

1. **#1 强制首登改密形同虚设**：默认凭据 `douyin/douyin2026` 种入时带 `MustChangePwd=true`，
   但登录仍签发完整有效的 7 天 JWT，`mustChangePwd` 仅回传给前端弹窗提示（软提示，可关闭）。
   知道默认密码者绕过 SPA 直接调 API 即可在从不改密的情况下拥有全部权限。
2. **#2 `IsLocalToolRequest` 用裸 `RemoteIpAddress`**：反向代理（NAS 常见同机代理）后，
   `RemoteIpAddress` 恒为代理 loopback/LAN，所有公网请求都通过 LAN-only 门控，
   匿名 `FastResetCookie` / `GetAllCookies` 攻击面重新打开。

## 方案

### #1 服务端强制改密（JWT claim + 端点门控中间件）

- 登录时若 `user.MustChangePwd`，在 JWT 内附加 claim `must_change_pwd=true`。
- 新增标记特性 `[AllowWhenPasswordChangeRequired]`，打在改密流程必需的端点上：
  `AuthController.UpdatePwd`（改密）与 `AuthController.GetUserAvatar`（改密表单取 UserId）。
- 新增中间件 `PasswordChangeGateMiddleware`，置于 `UseAuthorization` 之后、`MapControllers` 之前：
  - 端点带标记特性 → 放行；
  - 已认证且 token 带 `must_change_pwd=true` → 返回 HTTP 403 + `{code:-2, mustChangePwd:true}`；
  - 其余放行。
- 闭环：前端改密成功后已 `removeAuthorization()` 强制重登；重登时 `MustChangePwd` 已在库中清零
  （`AdminUserRepository.UpdatePwd` / `ResetPwd` 均已清零），新 token 不再带 claim。

判定基于**端点元数据**（标记特性）而非路径字符串匹配，避免路由大小写/前缀脆弱性。

### #2 工具端点 IP 门控加固（拒绝携带转发头的请求）

- 工具 `dy.cookie.exe` 直连本机/内网，请求**不含**转发头；公网经反向代理的请求**通常带**
  `X-Forwarded-For` / `X-Real-IP` / `Forwarded` / `X-Forwarded-Host`。
- 门控改为：`!存在任一转发头 && IsPrivateOrLoopback(RemoteIpAddress)`。
- `NetworkGuard` 保持纯函数可测：新增 `IsLocalToolRequest(IPAddress, bool hasForwardedHeaders)`，
  控制器侧只负责从 `HttpContext` 提取转发头存在性。

## 不在本次范围

- 接入 `ForwardedHeaders` + 可信代理白名单（需部署侧配置，另行处理）。
- 移除/轮换硬编码默认凭据 `douyin/douyin2026`（强制改密已覆盖其风险）。

## 测试

- `NetworkGuardTests`：扩充 `IsLocalToolRequest(ip, hasForwarded)` 真值表
  （内网+无转发头=放行；内网+有转发头=拒绝；公网=拒绝）。
- `PasswordChangeGateTests`：claim 判定纯逻辑（带标记放行 / 带 claim 拦截 / 无 claim 放行）。
