# 全栈代码审查报告 — dysync.net

- **日期：** 2026-05-18
- **分支：** `decompile/dy-sync-lib`
- **范围：** 全栈（.NET 6 后端 + Vue 3 前端 + 构建/部署/仓库卫生）
- **方法：** 四路并行审查（后端安全/正确性、后端架构/数据层、前端、构建/依赖/Ops），逐文件阅读真实代码后交叉去重。
- **分级：** 🔴 必须修复 / 🟡 建议修改 / 🔵 仅供参考

> **前置事实：** 提交本报告时仓库 228/273 文件显示"已修改"，但 `git diff` 为 46210 增 / 46210 删、逐行内容一致——这是一次 **CRLF→LF 全树行尾转换**，并非真实代码改动。本报告针对代码库本身，不针对该 diff（行尾问题本身见 WARNING）。

---

## 🔴 必须修复（CRITICAL）

### 1. 密码用无盐 MD5 存储 + 硬编码默认凭据
`utils/Md5Util.cs:11`、`repository/AdminUserRepository.cs:112`、`Program.cs:214`、`Controllers/AuthController.cs:78`

管理员密码为单次无盐 MD5；默认账号 `douyin/douyin2026` 写死在源码中，每次启动自动重建，`ResetPwd` 也回退到该常量。源码可反编译，等同无密码保护。

**修复：** 改用 `PasswordHasher<T>`(PBKDF2) 或 BCrypt/Argon2 + 每用户盐 + 高代价因子；首次运行强制改密或生成随机初始密码；登录时迁移旧哈希。

### 2. JWT 签名密钥进程内随机生成 + Issuer/Audience 校验关闭
`utils/Md5Util.cs:10`、`extension/ServiceExtension.cs:53,230-231`、`Controllers/AuthController.cs:100-108`

密钥 = 常量前缀 `"dysync.net-key-"` + 运行时 GUID（`Encoding.ASCII`）：每次重启所有 token 失效、多实例无法互验；`ValidateIssuer/ValidateAudience=false` 且 issuer/audience 为随机数，token 未与本服务绑定。

**修复：** 从配置/密钥库读取 ≥256bit 稳定密钥（UTF-8 编码），固定 issuer/audience 常量并开启校验。

### 3. LogsController 完全未鉴权 + 路径穿越任意文件读取
`Controllers/LogsController.cs:20-37`（无 `[Authorize]`，确认无全局 fallback 策略）

`GetLog(type,date)` 用未校验路由参数 `Path.Combine` 拼路径后经 `PhysicalFile` 返回；`Path.Combine` 不拦 `..`。日志含 Douyin cookie/SQL/异常，任意匿名者可读任意文件。`GetLogFiles`(`:45`)、`GetLogContent`(`:65`) 同样匿名。

**修复：** 控制器加 `[Authorize]`；`type` 走白名单、`date` 限 `^\d{8}$`；`Path.GetFullPath` 后校验仍位于日志根目录内。

### 4. 大量改变状态的接口未鉴权
`Controllers/VideoController.cs`（`ReDownload:278`、`BathRealDelete:304`、`DeleteVideo:324`、`DeleteByAuthor:359`、`RemoveInvalidVideo:399`、`Move:466` 等均无 `[Authorize]`）、`Controllers/ConfigController.cs:157`（匿名 `DeskInitAsync` 可植入任意 cookie 与文件路径）、`ConfigController.cs:431`（匿名 `GetMp3` 路径未校验）

任意匿名客户端可批量删视频、改目录、注入 cookie / 任意存储路径（数据外泄面）。

**修复：** 控制器级 `[Authorize]` + 全局 `FallbackPolicy`；仅对真正公开端点显式 `[AllowAnonymous]`；`deskinit` 服务端原子化"未初始化"硬门控；存储路径限制在允许的基目录内。

### 5. 原始字符串拼接 SQL + 无 WHERE 的全量改密
`repository/AdminUserRepository.cs:115`

```csharp
string sql = $" Update login_user_info SET Password='{password}'";
return this.Db.Ado.ExecuteCommand(sql) > 0;
```

无 WHERE，**重置所有用户密码**；且是危险的字符串插值 SQL 模板。

**修复：** 参数化：`Db.Ado.ExecuteCommand("UPDATE login_user_info SET Password=@pwd WHERE Id=@id", new {pwd, id})`，或用 `Db.Updateable<AdminUserInfo>()`。

### 6. `UseTranAsync` 未 await，事务原子性是假的——静默数据/文件丢失
`repository/BaseRepository.cs:107-115`

返回的 `Task` 从不 await，靠 `IsCompletedSuccessfully` 同步判断（几乎恒 false），方法在事务体完成前即返回。影响 `DouyinVideoService.BatchInsertOrUpdate`(`service/DouyinVideoService.cs:56`) 与 `ReDownloadViedoAsync`(`service/DouyinVideoService.cs:286`)：删原记录与建重下载记录非原子，随后据未观测的事务结果**物理删除文件**。叠加 `RealDeleteVideos` 的 `Task.Run` fire-and-forget(`service/DouyinVideoService.cs:489`)，可在用户被告知"成功"时永久丢数据。

**修复：** `return await Db.Ado.UseTranAsync(action, errorCallBack);` 用其 `DbResult.IsSuccess`；销毁性 DB+文件操作禁止 fire-and-forget，改后台队列；逐一审计调用方成功判断。

### 7. 明文密码写入 localStorage
`app/src/pages/login/LoginBox.vue:172-179`（mount 时 `:143` 回读）

"记住密码"把账号**和明文密码**以 JSON 存 `localStorage`，永久留存、任意同源 JS/扩展可读，管理面板凭据窃取面。

**修复：** 绝不存密码，只记 username；持久登录靠服务端 token / refresh-token。

### 8. 鉴权 token 存在 JS 可读且无安全属性的 Cookie
`app/src/store/http.ts:13-14`、`app/src/utils/axiosHttp.ts:150-158`

`Cookie.set('Authorization','Bearer ...')` 无 `HttpOnly/Secure/SameSite`，配合 `xsrfHeaderName:'Authorization'`，任意 XSS 即完全接管会话，明文 HTTP 可泄漏。

**修复：** 优先服务端下发 `HttpOnly;Secure;SameSite=Strict` 会话 cookie，前端不再管理 token；退出需打服务端端点。

### 9. .NET 6 已 EOL + 含 CVE 的传递包
`dy.net.csproj:5`（net6.0，2024-11 停止支持）、`dy.net.csproj:73`（`System.Net.Http` 4.3.4，历史 CVE）、`dy.net.csproj:74`（`System.Text.RegularExpressions` 4.3.1，ReDoS CVE-2019-0820）；两个 Dockerfile 也用 `aspnet:6.0`。

**修复：** 升级到 .NET 8 LTS（含两个 Dockerfile 基础镜像、两个 `.pubxml` 的 TargetFramework）；删除 `System.Net.Http` / `System.Text.RegularExpressions` 这两个多余的 PackageReference（运行时内置）。

### 10. 所有出站 HTTPS 证书校验被完全关闭
`extension/ServiceExtension.cs:329-332`

`RemoteCertificateValidationCallback => true`，Douyin API 与下载客户端接受任意证书，携带 cookie 的请求可被 MITM。

**修复：** 移除回调，使用默认校验；确需固定时对单端点做证书 pinning。

---

## 🟡 建议修改（WARNING）

### 后端 — 数据 / 性能
- **全表载入内存做聚合：** `service/DouyinVideoService.cs:109`（`GetStatics` 拉全表后约 20 次 LINQ）、`:390`（`GetChartData`）、`:412`（`DeleteInvalidVideo`）、`:521`（`HandOldFolderVideos`）；`repository/DouyinVideoRepository.cs:67` 每次分页查询都把整个 `DouyinCookie` 表载入内存映射用户名。→ 聚合下推 SQL（`GroupBy/CountAsync`），cookie 用 join。
- **同步循环无取消令牌：** `job/DouyinBasicSyncJob.cs:427`、`job/DouyinFollowsAndCollnectsSyncJob.cs:150,203` 仅靠远端 `HasMore`，从不读 `context.CancellationToken`，关停时阻塞 `WaitForJobsToComplete`。→ 串联 `CancellationToken` 并加最大页/最大时长护栏。
- **手动事务孤儿：** `repository/DouyinFollowRepository.cs:117-124` `BeginTranAsync` 后若 `!currentSecUids.Any()` 直接 `return` 未提交/回滚，污染共享 scoped 连接（`ServiceExtension.cs:245,275` 任务与 `ISqlSugarClient` 均 Scoped，长循环复用同一非线程安全连接）。→ 校验后再开事务，或 `finally` 统一提交/回滚。
- **`service/DouyinCookieService.cs:130-135`** `ImportCookies` 先全删后插入无事务，插入失败则所有 cookie 丢失。→ 包正确事务（见 #6）。
- **同步伪异步遍布：** `service/DouyinCommonService.cs:20,143,177`、`repository/DouyinFollowRepository.cs:19-34`、`AdminUserRepository.InitUser/ResetPwd` 声明 `async` 但无 `await`（CS1998），阻塞线程。→ 用 `*Async` API 并真正 await。

### 后端 — 安全 / 可维护性
- **控制器 `catch` 直接回 `ex.Message`/完整路径：** `Controllers/VideoController.cs:72,147,152,255,462`、`LogsController.cs:56,89`，绕过全局过滤器泄漏内部信息。→ 对外通用文案，细节仅服务端日志。
- **分享链接弱密钥：** `Controllers/VideoController.cs:245` `(FileHash+AuthorId).Md5()`，低熵、可枚举、无过期，永久未授权访问。→ 改 HMAC(服务端密钥, vid+过期戳) 并校验过期。
- **`appsettings.json` 入库** 且 `dockerTagsBaseUrl` 为明文 HTTP 第三方域名；`Program.cs:216` 经 `db/pwd.txt` 明文改密机制脆弱。→ 部署配置出库或模板化，外联用 HTTPS，改密走鉴权流程。
- **`job/DouyinBasicSyncJob.cs` 1520 行上帝类**，混编排/cookie 遍历/分页/下载/文件布局/NFO/去重业务/持久化（`ProcessVideoList:533-728` 约 200 行深嵌套）。→ 抽 `VideoDownloadPipeline`/`DedupPolicy`/`FilesystemLayout`，job 仅作薄编排。
- **catch-and-swallow / 靠异常消息文本判分支：** `service/DouyinMergeVideoService.cs:274-283`（子串匹配中文异常消息控制流，应换 typed exception）、`repository/DouyinCollectCateRepository.cs:156`（`Ado?.RollbackTranAsync()` 产生未 await 的 Task）。

### 前端
- **`app/src/store/account.ts:30`** `logged` 初始为 `true`，未鉴权时先呈现"已登录"UI。→ 默认 `false`，登录/profile 成功后置 true。
- **`app/src/store/account.ts:39`** 登录判定 `response.code===200 && data.code===0` 与 `http.ts:36-37` 拦截器契约矛盾。→ 统一响应契约。
- **`app/src/store/http.ts:97`** 错误分支 `return error;`（resolve 而非 reject），错误被静默吞成成功；全局无请求取消（大表频繁刷新）。→ 改 `Promise.reject(error)`；接 `AbortController`，路由切换/卸载时取消。
- **超大 SFC：** `pages/mobile/MobileDashboard.vue`(2287)、`workplace/RecordTable.vue`(1608)、`followd/index.vue`(1331)、`cok/CookieTable.vue`(1232)、`workplace/statics.vue`(1106)、`set/AppSet.vue`(971)。→ 抽 composable + 拆子组件。

### 仓库卫生 / Ops
- **无 `.gitattributes`** 导致全树 CRLF 抖动（228/273 文件假改动）。→ 加 `* text=auto eol=lf`（及 `*.sh eol=lf`、`*.png binary`），单独提交 `git add --renormalize .`。
- **入库的临时/死/敏感文件：** `.env`/`.env.development`/`.env.github` 入库且 `.gitignore` 未排除；4 个 `app/vite.config.ts.timestamp-*.mjs`；`FrontView copy.vue`/`statics copy.vue` 死代码副本；`dy.net.csproj.user` 泄漏作者绝对路径 `E:\code\dysync\...`。→ `git rm --cached` 并补 `.gitignore`（`.env*`+`!.env.example`、`*.timestamp-*.mjs`、`*.user`、`* copy.*`）。
- **Dockerfile** 以 root 运行、无 `USER`、基础镜像浮动大版本标签；无 `.dockerignore` 且 `COPY . .` 把 `.git/node_modules/obj/bin/.env/logs` 全打进镜像；无多阶段构建。→ 加非 root `USER` + 镜像 digest 固定 + `.dockerignore` + `sdk→publish→aspnet` 多阶段。
- **过时依赖：** NuGet `JwtBearer 6.0.16`/`SpaServices 6.0.10`/`Serilog.AspNetCore 3.4.0`/`SqlSugarCore 5.1.4.128`；npm `axios ^0.21.1`（pre-1.0，SSRF/ReDoS 历史告警）、`js-cookie ^2.x`。→ 随框架升级一并升级，`axios→^1.x`、`js-cookie→^3.x`，跑 `pnpm audit`。

---

## 🔵 仅供参考（INFO）

- `extension/EnumExtensions.cs:35` `GetDescription` 无 `[Description]` 时返回 `GetHashCode()`（数字）而非名称——但实际代码调用的是 `utils/DouyinRequestParamManager.cs:447` 的 `GetDesc`，此方法疑似死陷阱代码，建议删除或修正并合并。
- `job/DouyinFavoritSyncJob.cs:66` 同名文件夹分支误用 `cookie.SavePath` 而非 `FavSavePath`，疑似 copy-paste bug，导致重名收藏落到错误根目录。
- `utils/DouyinRequestParamManager.cs:77+` 静态字典按引用返回并在 `DouyinHttpClientService` 内逐请求 mutate，多 job 并发下参数串扰（数据完整性竞态）；`DouyinCommonService` 的 `IsFirstRunning` 全局可变标志同类风险。
- `Program.cs:263-266` `InitApplicationServices` 吞掉所有启动异常，可能在半初始化状态对外服务（如 admin 用户尚未建）。
- `extension/ServiceExtension.cs:224` `RequireHttpsMetadata=false` 且只监听明文 `http://*:10101`（`Program.cs:13`）——token/默认密码明文传输，需强制 TLS 反代并文档化。
- 前端约 49 处 `console.log` 残留，其中 `app/src/pages/login/LoginBox.vue:187` `console.log(res)` 打印含 token 的登录响应；约 72 处 `any`，`app/src/store/coreapi.ts` 约 40 个 API 全 `any`，零类型安全。
- 大量注释死代码（`service/DouyinVideoService.cs:332-364`、`extension/ServiceExtension.cs:402-661` 整段 Swagger、`app/src/store/coreapi.ts:6-29`、`app/src/router/routes.ts:42-53` 等）；魔法数遍布（同步批上限 30、随机延时、分页数等）应进 `AppConfig`。
- `app/src/router/guards.ts:176-210` 在 import 期注册 `window load` 监听 + 模块级可变重定向标志，与正常导航竞态。
- `mp3/silent_10.mp3` 与 `mp3_0/silent_10.mp3` 重复 157KB 二进制；`package.json` 脚本仍用 `yarn` 但锁文件是 `pnpm-lock.yaml`。
- ✅ 已核查无误：源码/配置**无硬编码生产密钥**；前端**无 `v-html`/XSS sink**；锁文件无冲突；路由守卫确实拦截受保护路径（仅 UX 层，真正鉴权应在服务端）。

---

## 建议修复顺序

1. **#5 全量改密 + #3 LogsController + #4 未鉴权销毁接口** — 远程未授权数据破坏，最高危
2. **#6 假事务 + RealDeleteVideos fire-and-forget** — 静默数据/文件丢失
3. **#1 MD5/默认凭据 + #2 JWT 密钥/校验 + #7/#8 前端凭据存储** — 认证体系
4. **#10 出站证书校验 + #9 .NET 6 / CVE 包升级**
5. WARNING 优先做 `.gitattributes`（解除全树抖动，使后续 diff 可审）与全表内存载入两项
