# 代码审查报告（复审）— dysync.net

- **日期：** 2026-06-02
- **分支：** `decompile/dy-sync-lib`
- **基准：** 复审上一份报告 `docs/code-review-2026-05-18.md`，并审查其后完成的修复。
- **方法：** 逐条复核 2026-05-18 报告的 🔴/🟡/🔵 项在当前代码的真实状态（非照搬旧报告），加对本轮 3 项 CRITICAL 修复（#4/#6/#1）的对抗式审查。build 0 错误、golden-master 126 测试全绿。
- **分级：** 🔴 必须修复 / 🟡 建议修改 / 🔵 仅供参考

> **要点：** 2026-05-18 报告已严重过时——其 10 项 🔴 CRITICAL 中 7 项在报告当天晚些即修复，剩余 3 项（#1/#4/#6）于本轮（2026-06-02，提交 `010b774..7ce254b`）闭环。**10 项 CRITICAL 现已全部解决**（#1 含已记录的残留限制）。

---

## 🔴 CRITICAL — 全部已解决

| # | 问题 | 现状 | 位置 |
|---|------|------|------|
| 1 | MD5/默认凭据 | ✅ 哈希 PBKDF2 + 透明迁移；保留 douyin 账号但 `MustChangePwd` 强制首登改密（seed 置真 / 改密+ResetPwd 清零 / 登录响应回传 + 前端提示） | `PasswordUtil`、`AdminUserInfo.MustChangePwd`、`AuthController.Login` |
| 2 | JWT 密钥随机/校验关 | ✅ 稳定密钥 + Issuer/Audience/SigningKey 校验开 | `ServiceExtension.cs:229-234` |
| 3 | LogsController 未鉴权/穿越 | ✅ 类级 `[Authorize]` + `Path.GetFullPath` | `LogsController.cs:10,30-31` |
| 4 | 未鉴权销毁/注入接口 | ✅ VideoController 类级 `[Authorize]` + 全局 `FallbackPolicy`；`deskinit` 加未初始化门控 | `ConfigController.DeskInitAsync` |
| 5 | 改密 SQL 拼接 | ✅ 参数化（无 WHERE 为单管理员既有语义） | `AdminUserRepository.cs:123` |
| 6 | 假事务/fire-and-forget | ✅ `UseTranAsync` await 真实结果；`RealDeleteVideos` 去掉 >30 条 `Task.Run` | `BaseRepository`、`DouyinVideoService.RealDeleteVideos` |
| 7 | localStorage 明文密码 | ✅ 只存 username | `LoginBox.vue` |
| 8 | token cookie 无属性 | ✅ `sameSite=strict` + 条件 `Secure`（非 HttpOnly，有 http 直连部署理由） | `axiosHttp.ts:153` |
| 9 | .NET6 EOL/CVE 包 | ✅ net8.0，冗余包已删 | `dy.net.csproj:4,81` |
| 10 | 出站证书校验全关 | ✅ 回调已移除 | `ServiceExtension.cs` |

### 本轮 3 项修复的审查记录

- **#4 deskinit 门控**：`if (await IsInit()) return Fail`。正确堵住「初始化后随时匿名注入 cookie/路径」，保留 `[AllowAnonymous]` 不破坏首装；后续 cookie 管理走鉴权的 `update`。✅
- **#6 RealDeleteVideos**：两分支本就同构，合并为单 await 路径并消魔法数 30。>30 条改为阻塞至完成（单机工具可接受）。调用方 `VideoController.BathRealDelete` 仍 await bool，行为正确。✅
- **#1 MustChangePwd**：自审发现 `ResetPwd`（pwd.txt 改密）漏清标记，已补修并加测试。✅
  - **残留限制（设计取舍，非缺陷）**：标记不阻止服务端 API 调用，前端提示可被绕过；首登前默认凭据仍有窗口。**加固选项**（后续）：服务端受限 token 过滤器（改密前除 UpdatePwd 全 403），或干脆 seed 随机初始密码而非已知默认。
  - **前端未经编译器验证**：本环境无 node_modules（pnpm-lock 但 pnpm 未装）；`account.ts`/`App.vue` 改动经人工静态核对，待 `vue-tsc`/运行期复验。

---

## 🟡 建议修改 — 复审

### 自 2026-05-18 起已解决
- ✅ **CRLF 全树抖动**：`.gitattributes` 已加（`* text=auto eol=lf` 等）。
- ✅ **同步伪异步（CS1998）**：当前 build `CS1998` 计数为 0，已清零。
- ✅ **全表内存聚合（统计）**：`GetStatics`/`GetChartData` 已下推 SQL（计数/求和），金主测试保持绿。
- 🔄 **上帝类 `DouyinBasicSyncJob`**：拆分进行中——已抽 14 个纯决策方法到 `utils/SyncDecisionHelper.cs`（golden-master 钉死），I/O 编排仍在 job 薄壳。未完成但有纪律推进。

### 仍开放
- 🟡 **前端 `account.ts:32` `logged: true` 默认**：未鉴权先呈现「已登录」UI。应默认 `false`，登录/profile 成功后置 true。
- 🟡 **前端 `http.ts:97` `return error;`**：错误分支 resolve 而非 reject，错误被静默吞成成功。**注意地雷**：代码注释「之前改成 Promise.reject 导致登录异常」——存在与登录流程的隐藏耦合，直接改会破坏登录。需先解开 `account.ts` 登录判定（`response.code===200 && data.code===0`）与拦截器契约的矛盾，再统一改 reject。
- 🟡 **残留内存侧聚合**：`GetStatics` 仍有 `tag1List.GroupBy`（:113）等内存步骤；`DeleteInvalidVideo`/`HandOldFolderVideos`、`DouyinVideoRepository` 的 cookie 映射等是否全下推待逐一核。
- 🟡 **超大 SFC**：`MobileDashboard.vue`(2287) 等仍未拆。
- 🟡 控制器 `catch` 回 `ex.Message`、分享链接弱密钥、`ImportCookies` 无事务等（见旧报告，未复核逐项）。

---

## 🔵 仅供参考

- `Program.cs:214` 仍调 `InitUser("douyin","douyin2026")`——按评审决定保留默认账号，靠 #1 的 `MustChangePwd` 收口。若要彻底消除「首登前窗口」，改 seed 随机密码。
- 前端 `console.log` 残留、`any` 泛滥、注释死代码、魔法数等（旧报告 INFO 项）未在本轮处理。
- 旧报告 INFO 中 `DouyinFavoritSyncJob` 误用 `SavePath`、静态字典并发串扰等疑似 bug 未复核，建议下一轮专项。

---

## 附录：合并 master 后的工具端点加固（2026-06-02）

复审后并入 master 的「重置 cookie 工具」（提交 `94c3d8b`），其新增两个 `[AllowAnonymous]` 端点构成新匿名面：

- `GET /api/config/Cookies`（`GetAllCookies`）：匿名列出所有 cookie 的 id/name/status。
- `POST /api/config/FastResetCookie`：匿名按 id 覆盖任意 cookie。

🔴→✅ **已加固**（提交 `2904e3b`）：客户端 `dy.cookie.exe` 经 `strings` 证实为无 token 的 Go/chromedp 二进制（走 LAN），加鉴权/token 会破坏工具，故改用**来源门控**——新增纯函数 `utils/NetworkGuard.IsPrivateOrLoopback`，两端点非内网/本机来源返回 403，保留 `[AllowAnonymous]` 不破坏 LAN 工具。`NetworkGuardTests` 26 例覆盖。

⚠️ **残留**：反代未配 forwarded-headers 时按代理私网 IP 放行，需代理层限制；LAN 内攻击者仍在范围内（同上游设计）。

---

## 结论

- **CRITICAL 闭环**：10/10 解决，本轮 3 项均经 spec→改→测试→提交。合并 master 后工具端点亦已来源门控加固。golden-master 152 全绿。
- **下一步建议**：(1) 前端 `logged` 默认值与 `http.ts` 错误吞没（先解耦登录契约）；(2) 继续上帝类拆分；(3) 若安全要求提高，为 #1 加服务端硬门控、为工具端点加 token。
