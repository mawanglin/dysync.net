# 抖音扫码登录自动获取 Cookie — 设计规格

- 日期：2026-07-01
- 分支：improve/dysync-refactor
- 状态：设计已确认，待转实现计划
- 技术栈：C# .NET 8 + Vue3 + Docker

## 背景与目标

当前新增抖音账号需用户手动打开浏览器、F12 从已登录会话里整串复制 cookie 粘进系统；cookie 过期后也要手动重复该操作（现有 `FastResetCookie` LAN 工具 + 表单手改，另有被劝退的浏览器插件）。

痛点根因是架构选择：系统是"消费型"的，**没有登录能力、也不算任何抖音风控签名**——`DouyinHttpClientService.GetHttpResponseMessage` 只是把用户存的整串 cookie 塞进请求头，`DouyinRequestParamManager` 里 `a_bogus/msToken/verifyFp/fp/x-secsdk-web-signature` 全部是空占位从未计算，完全依赖"用户从真实已登录浏览器拷出的 cookie"通过风控。

**目标**：新增"无头浏览器扫码登录"功能（方案 B），用真实 Chromium 打开抖音登录页 → 前端展示二维码 → 用户抖音 App 扫码 → 服务端从浏览器上下文导出 cookie（并顺带抓取账号信息）→ 汇入现有落库链路。由真实浏览器承担全部 JS 签名/指纹，产出的 cookie 与手动提取等价，且避开项目一直刻意回避的签名逆向。

### 为什么选方案 B（无头浏览器）而非方案 A（直连 SSO 接口）

- 方案 A 需自行复刻 `sso.douyin.com` 的 QR 登录流程，而这些接口本身也要 `a_bogus/msToken` 签名——等于从零补上项目一直回避、最易被风控封禁的部分，维护成本高、易翻车。
- 方案 B 的签名/指纹全由真实 Chromium 计算，跟随官方页面，稳定；且完全复用现有 `DouyinCookie` 存储、`/config/update` 落库、`CheckCookie` 验活与过期检测。
- 代价：镜像加 Chromium（~300MB）、登录时临时占内存——已接受。

## 已确认的关键决策

1. **落地方式：两条路都做。**
   - 新增账号：扫码只负责回填现有新增表单（cookie + 能抓到的 `SecUserId/UserName/MyUserId`），存储路径等其余字段仍由用户填，最后走现有 `/config/update`（`id="0"` 新增）。
   - Cookie 过期重扫：行内"重新扫码"绑定该记录，成功后用该记录 + 新 cookie 走现有 `/config/update`（`id!=0` 更新），直接更新落库。
2. **浏览器生命周期：一次性启动、用完即关。** 每次登录才启动一个 Chromium 实例，登录完成/取消/超时后立即关闭释放内存。平时 0 占用。最适合 NAS（飞牛/fnOS）部署。
3. **依赖打包：apt 装系统 chromium + PuppeteerSharp。** Dockerfile 用 `apt-get install chromium`（与现有 ffmpeg 同一模式，离线友好），C# 用 PuppeteerSharp 指向该可执行文件。
4. **账号信息：自动抓取并回填。** 登录后在同一浏览器上下文抓 `sec_user_id / 昵称 / uid` 一并回填。
5. **二维码获取：截图登录页二维码 DOM 元素返回 base64**（不拦截 `get_qrcode` 接口），所见即所得、不依赖抖音接口字段结构。
6. **单会话约束**：同一时刻只允许一个扫码会话，新建会取消旧会话。
7. **超时**：二维码 ~2min 过期；总超时 ~3min 兜底关浏览器。
8. **鉴权**：QR 端点要求管理员已登录（与 `/config/update` 一致），不复用 `deskinit` 的匿名首装逻辑，收紧攻击面。

## 现有可复用资产（勿重复造）

- `DouyinCookie` 实体已有 `Cookies / SecUserId / MyUserId / UserName / SavePath(必填) / FavSavePath / UpSavePath / StatusCode / StatusMsg` 及下载开关。
- `DouyinHttpClientService.CheckCookie(cookie)` 已能拿 cookie 去抖音验活。
- `ConfigController.AddOrUpdateAsync`（`POST /config/update`）：`id=="0"` 新增、否则更新，内部已含 `CheckCookie` 复验 + 置 `StatusMsg="正常"` + `ReStartJob()`。
- 前端 `CookieTable.vue` 已有新增/编辑表单、行操作、状态展示。
- Docker 运行时 `mcr.microsoft.com/dotnet/aspnet:8.0`（Debian bookworm，已 apt 装 ffmpeg）；真实构建走 `Dockerfile.ci` 多阶段。

## 总体架构与数据流

```
前端(QrLoginModal) ──start──▶ 后端 DouyinQrLoginService
                                   │ 启动 chromium(用完即关) → 打开抖音登录页
                                   │ 截二维码元素 → base64
        ◀──{sessionId,qrImg,expiresAt}──┘
   用户抖音App扫码确认
        ──poll(轮询~1.5s)──▶ 检测登录态
                                   │ 成功: page.GetCookies + 抓账号信息 → 关浏览器
        ◀──{status,cookies,secUserId,userName,uid}──┘
   ① 新增: 回填新增表单 → 用户填路径 → 保存(现有 /config/update, id="0")
   ② 过期重扫: 用该记录+新cookie → 现有 /config/update(id!=0, 更新+重启任务)
```

两条路都汇入现有 `/config/update`，**不新增落库端点**。

## 后端组件（新增）

### DouyinQrLoginService（会话状态机，核心）

- `Start()`：启动 chromium → 打开登录页 → 等二维码出现 → 截图 → 生成 `sessionId`，存 `ConcurrentDictionary` 内存会话（含 Browser 实例引用、创建时间、状态），返回 `{sessionId, qrImageBase64, expiresAt}`。**单会话约束**：已有活跃会话则先取消旧的。
- `Poll(sessionId)`：返回状态 `waiting│scanned│confirmed│success│expired│error│notfound`。`success` 时：`page.GetCookiesAsync()` 拼 `k=v; k=v`（只取 `.douyin.com` 域）→ 导航个人页/调档案接口抓 `sec_user_id/昵称/uid` → 关浏览器 → 附带账号字段返回。
- `Cancel(sessionId)`：关浏览器、移除会话。
- 后台清理：会话 TTL（二维码 ~2min → `expired`；总超时 ~3min 兜底关浏览器）。

### IQrLoginBrowser 抽象

把 PuppeteerSharp 交互（launch / screenshotQr / getCookies / getProfile / dispose）收在一个接口后面，便于给状态机写单测（不依赖真实 Chromium）。

### QrLoginController（或并入 ConfigController）

- `POST /config/qrlogin/start` → `{sessionId, qrImageBase64, expiresAt}`
- `GET  /config/qrlogin/poll?sessionId=` → `{status, cookies?, secUserId?, userName?, myUserId?}`
- `POST /config/qrlogin/cancel` → `{ok}`
- 鉴权：与 `/config/update` 一致，要求管理员已登录。

### 落库复用

两条路都汇入现有 `/config/update`（含 `CheckCookie` 复验 + `StatusMsg="正常"` + `ReStartJob`）。不新增落库端点。

## 前端组件（CookieTable.vue）

- **QrLoginModal.vue（新）**：调 start 显示二维码 + 状态文案，`setInterval` 轮询 poll（~1.5s）；success 时 `emit` 账号数据；expired 显示"刷新二维码"（重启会话）；关闭时调 cancel。
- **新增流程**：新增弹窗里加"扫码登录"入口 → QrLoginModal → success 回填 `cookies/secUserId/userName/myUserId` → 用户补路径 → 现有保存。
- **过期重扫**：cookie 状态异常的行加"重新扫码"操作 → QrLoginModal 绑定该 `record.id` → success → 用 record + 新 cookie 调 `/config/update` 更新。

## 错误处理与降级

- chromium 启动失败/崩溃 → `error`，前端提示"浏览器组件异常"。
- 二维码过期 → `expired`，前端"刷新二维码"重启会话。
- 登录总超时（~3min）→ 关浏览器、`expired`。
- **抓账号信息失败降级**：仍返回 cookie，账号字段留空让用户手填，不让整个流程失败。
- poll 到已关闭/不存在会话 → `notfound`，前端回到起始态。
- 容器必需 `--no-sandbox --disable-dev-shm-usage`。
- 双保险：即便扫码成功，保存时仍走现有 `CheckCookie` 复验。

## 测试

- **后端单测**（遵循项目 characterization / 纯函数纪律）：用 `IQrLoginBrowser` 假实现驱动 `DouyinQrLoginService` 状态机——start/poll/cancel、二维码过期、总超时、并发取消旧会话、抓取失败降级；cookie 字符串拼装（page cookies → `k=v; k=v`，域过滤）纯函数单测。
- 真实 chromium 交互 + 前端扫码/轮询/过期/取消：手动 / 集成验证（真实抖音登录无法自动化断言）。

## Docker

- 本地 `Dockerfile` + `Dockerfile.ci` 的 final 阶段：`apt-get install -y --no-install-recommends chromium`（与现有 ffmpeg 同一 RUN 模式，离线友好）。
- csproj 引 `PuppeteerSharp` NuGet；`LaunchOptions{ ExecutablePath="/usr/bin/chromium", Headless=true, Args=["--no-sandbox","--disable-dev-shm-usage"] }`。
- 镜像增大约 ~300MB（已接受）。

## 范围边界（YAGNI）

- 不做 SSO 接口直连（方案 A）。
- 不做多会话并发登录（单会话足够个人 NAS 场景）。
- 不做常驻浏览器池 / 空闲回收（用完即关已满足）。
- 不新增落库端点（复用 `/config/update`）。
- 不改动现有同步/下载链路与签名逻辑。
