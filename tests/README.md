# dy.net Characterization Test Suite

These are **golden-master / characterization tests**. They do not assert what the
code *should* do — they pin what it *currently* does, so the planned
WARNING-tier refactors (god-class split, in-memory→SQL aggregation rewrite,
async cleanup) can be proven non-behavior-changing.

## How to run

```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj
```

(`DOTNET_ROLL_FORWARD=LatestMajor` only because the local box has SDK 10 and the
projects target net8.0; drop it on a net8.0 SDK.) Filter a single class with
`--filter <ClassName>`.

## What is pinned

| Area | Test | Locks |
|------|------|-------|
| `DouyinFileNameHelper` | `DouyinFileNameHelperTests` | `SanitizeLinuxFileName` (illegal-char scrub, whitespace strip, folder mode, control chars, **null/空 入参永不抛异常·永不返回 null/空白·清洗为空回落 defaultName**), `KeepChineseLettersAndNumbers`, `RemoveNumberSuffix`, `LimitUnifiedCount` |
| `Md5Util.Md5` | `PureHelperTests` | Standard RFC-1321 MD5 output |
| `SyncJobScheduleDescriber` | `SyncJobScheduleDescriberTests` | 周期描述：SimpleTrigger 间隔→"每 N 分钟"·Cron→"Cron: {expr}"·无效(无表达式/间隔≤0)→"自定义" |
| `JobScheduleValidator` | `JobScheduleValidatorTests` | 周期校验：interval 正整数(含空格 trim)→归一化·0/负/非数字→失败·cron 经 Quartz 校验合法/非法·未知类型→失败 |
| `SyncRunState` | `SyncRunStateTests` | 同步运行状态机：空闲/运行标志、`RequestStop` 空闲→false·运行→取消令牌、批次全部结束后重建新令牌、多类型并发 `IsAnyRunning` 追踪、`TryBeginManualTrigger` 运行中拒绝、`OnDownloaded` 累加 downloaded/failed + 快照（返回全部类型含 running/endedAt·elapsedSec·最近日志最新在前）、日志环 50 条上限淘汰最旧、每类型独立持久一行（不再批次清空）·跑完保留 running=false+endedAt+计数·重跑刷新该类型行·多类型即便不重叠也各占一行 |
| `SyncDecisionHelper` | `SyncDecisionHelperTests` | `GetNextCursor` (Cursor→MaxCursor→`"0"`, null-safe), `IsAwemeValid` (3-level null guard), `GetVideoTags` (per-level pick, missing→null), `IsSyncLimitReached` (cate 30-cap vs `BatchCount` cap, `OnlySyncNew` passthrough, `dy_follows` `!FullSync`, mix/series short-circuit), `BuildVideoEntity` (纯字段映射 / cate 标题覆盖 / `OnlyImgOrOnlyMp3` / `DyUserId` 分支 / `AuthorAvatarUrl` 回落 / `FileSize` 零回落), `PickBestVideoBitRate` (encoder=265 优先 H.265 + 回退 H.264 / 默认或 ≠265 仅 H.264 / 空或 null `UrlList` 跳过), `BuildVideoFileName` (custom_collect Format vs mp4 兜底 / mix-series 数字 episode 的 S01E{D2} / 默认分支按 BitRate.Format 取后缀·Video 空回落 mp4 / 模板空走 AwemeId.{Format} / `FullFollowedTitleTemplate` 非空经 VideoTitleGenerator 生成主名 / cate 非 custom_collect 走默认), `BuildVideoSaveFolderCandidates` (primary = SavePath/sanitized-subFolder / collisionResolved 追加 _{AwemeId} / 空 Desc 走 AwemeId 兜底 / 非法字符经 Sanitize), `PickCoverUrl` (cate 三级兜底 MixInfo→Video(Last)→Music / 非-cate Video(First)→Images / 全空→null), `BuildCoverPosterPath` (dy_mix·dy_series→poster.jpg / 其余→{名}-poster.jpg), `ResolveDuplicateVideoAction` (优先级去重判定：空表→默认 dy_favorite 最高 / 配置表→Sort 最小者最高 / 四层嵌套产出 SkipDownload·ReplaceExisting / 缺项 Sort 回退 int.MaxValue), `PickAuthorAvatarUrl` (头像 URL 选取：AvatarLarger 优先 → AvatarThumb 回落，各取 UrlList 首个 / 全空→null), `BuildDynamicVideoUrls` (动态视频 URL 构建：遍历 Images→DynamicVideo.BitRate，取 PlayAddr.UrlList 首个 …/aweme/v1/play 前缀 URL 构造 DouyinMergeVideoDto / Images 空→空 list / 非匹配 URL 跳过 / 多 Image·BitRate 按序收集), `BuildImageUrls` (图片 URL 提取：遍历 Images 取每张图 UrlList 首个 URL + 宽高构造 DouyinMergeVideoDto / UrlList 空或首 URL 空白→滤除 / Images=null→null、Images=空→空 list / 多图保序), `CollectAlternateVideoUrls` (候选视频 URL 收集：遍历 item.Video.BitRate，跳过 null bit 与 PlayAddr/UrlList null·空 / payurl == excludeUrl ordinal 区分大小写 / 跨 BitRate 不去重 / 双层保序), `PickMergeVideoCoverUrl` (合成视频封面 URL 选取：cate 分支 MixInfo→imageUrls→Music 三级兜底 / 非-cate 或 cate.CateType=dy_custom_collect 仅取 imageUrls 首个 / 与 PickCoverUrl 互为兄弟·收齐 cover 决策家族) |
| `VideoTitleGenerator.Generate` | `PureHelperTests` | Placeholder substitution, char-filtering of title/author, unknown-token passthrough, empty-field placeholder, 60-char cap |
| `DouyinVideoService.GetStatics` | `VideoStatsCharacterizationTests` | Full `VideoStaticsDto` snapshot: counts by type, distinct author/category, GB size formatting incl. the `<0.01` zero-substitution branch, **plus the `Categories` list (Tag1 grouping, empty→`其他`, desc order) and `Authors` list (Author grouping, desc order, last-row `Icon`/`UperId` semantics)** |
| `DouyinVideoService.GetChartData` | `VideoStatsCharacterizationTests` | Per-day `SyncTime` grouping and per-type counts (Graphic = empty `FileHash`), **single-day and multi-day group ordering** |
| `NetworkGuard.IsPrivateOrLoopback` | `NetworkGuardTests` | LAN/loopback 来源门控（匿名 cookie-工具端点 `FastResetCookie`/`GetAllCookies`）：环回 v4/v6、`10/8`·`172.16/12`(含 172.15/172.16/172.31/172.32 边界)·`192.168/16`·`169.254/16` link-local→allow；公网 8.8.8.8/1.1.1.1/IPv6→deny；IPv4-mapped 解包；IPv6 ULA `fc00::/7` + link-local `fe80::/10`；null→false |
| `NetworkGuard.IsLocalToolRequest` | `NetworkGuardTests` | review 2026-06-07 #2 反向代理绕过加固：内网/本机 **且无任何转发头**→allow；内网但带转发头（公网经同机代理时 `RemoteIpAddress` 恒为 loopback/LAN）→deny；公网无论是否带转发头→deny；null→false |
| `PasswordChangeGate.ShouldBlock` | `PasswordChangeGateTests` | review 2026-06-07 #1 服务端强制首登改密门控：带 `[AllowWhenPasswordChangeRequired]` 端点（改密 / 取 UserId）→放行；已认证且 token 带 `must_change_pwd=true` 访问普通端点→拦截（大小写不敏感）；无 claim 或 `false`→放行；未认证→放行（交由各端点鉴权/匿名门控） |
| `AdminUserService.InitUser` / `AdminUserRepository.UpdatePwd` | `AdminUserRepositoryTests` | Admin-user seed + change-password against the real SQLite stack, incl. review-#1 **`MustChangePwd`** (force first-login change): seed sets it `true` & stores PBKDF2 (not MD5); duplicate `InitUser` → `(-1,"系统用户已存在")` no second row; correct old-pwd `UpdatePwd` clears the flag & rehashes; wrong old-pwd → `(-1,"原密码错误")` flag unchanged; unknown `UserId` → `(-1,"用户不存在")` |

> These tests were deliberately widened (Categories/Authors/multi-day) before
> the in-memory→SQL aggregation refactor, then `GetStatics`/`GetChartData` were
> rewritten to push counts/sums to SQL and use narrow column projections
> instead of `GetAllAsync()`. The suite staying green is the proof that the
> refactor changed performance only, not behavior.

DB-bound tests run against a real temporary SQLite file built by SqlSugar
CodeFirst (`TestDb`), i.e. the production data stack — not mocks.

## What is intentionally NOT covered (and why)

- **`DouyinBasicSyncJob` orchestration** — HTTP + filesystem + DB coupled with
  no seams. Pure decision logic extracted so far: `GetNextCursor`,
  `IsAwemeValid`, `GetVideoTags`, `IsSyncLimitReached`, `BuildVideoEntity` (除
  `DynamicVideos`/`NfoFileGenerator` 副作用块外), `PickBestVideoBitRate`,
  `BuildVideoFileName` (基类体；`DouyinFollowedSyncJob.GetVideoFileName`
  override 仍是子类业务实现，未覆盖；「TryParse 失败」fallback 在当前 model
  下不可达，保留代码不写测试), `BuildVideoSaveFolderCandidates`
  (`CreateSaveFolder` 基类体的纯路径构造已抽出并 pinned；其
  `Directory.Exists`/`CreateDirectory` I/O 编排仍在 job 薄壳内、未覆盖；
  6 个子类 `CreateSaveFolder` override 仍是子类业务实现，未覆盖),
  `PickCoverUrl`/`BuildCoverPosterPath` (两个 `DownVideoCover` 重载的纯逻辑
  ——封面 URL 选取 + 海报路径派生——已抽出并 pinned；其 `CloseNfo`/空白守卫、
  `File.Exists`/`DownloadAsync` I/O 编排仍在 job 薄壳内、未覆盖；cate 分支
  对 `Video`/`Cover` 无空安全的 NRE 路径保留不测),
  `ResolveDuplicateVideoAction` (`AutoDistinct` 的四层嵌套优先级判定已抽出并
  pinned；其 `config.AutoDistinct`/`File.Exists` 守卫、`JsonConvert` 反序列化、
  `DeleteOldViedo`/`DeleteById` I/O、本地文件缺失分支（`OnlyImgOrOnlyMp3` 判定）仍在 job 薄壳内、未覆盖；
  `priorityLevels` 为 null 的 NRE 路径保留不测；薄壳 `DeleteOldViedo` 的
  try/catch 归一化为本刀唯一行为偏差、不在测试覆盖内),
  `PickAuthorAvatarUrl` (`DownAuthorAvatar` 的头像 URL 选取已抽出并 pinned；其
  `CloseNfo`/`Author`/blank 守卫、`GetAuthorAvatarBasePath`/`Path.Combine` 路径派生、
  `Directory`/`File`/`DownloadAsync` I/O 仍在 job 薄壳内、未覆盖；`Author == null`
  的 NRE 路径保留不测),
  `BuildDynamicVideoUrls` (`ProcessVideoList` 的动态视频 URL 构建段已抽出并 pinned；其
  `config.DownDynamicVideo` 开关、`else` 分支后续 `ProcessDynamicVideo`/`MergeMultipleVideosAsync`/
  `ProcessImageSetAndMergeToVideo` I/O 编排仍在 job 薄壳内、未覆盖；`?? 1920`/`?? 1080`
  不可达死代码保留不测),
  `BuildImageUrls` (`ProcessImageSetAndMergeToVideo` 的图片 URL 提取段已抽出并 pinned；其
  `MergeToVideo` 合成、`config.DownImageVideo` 校验与文件清理、`coverUrl` 派生、
  `DownAuthorAvatar`/`DownVideoCover` I/O、`virtualBitRate` 构造（含 `FileInfo` 读盘）、
  `CreateVideoEntity` 调用与特殊字段赋值、整体 `try/catch` 仍在 job、未覆盖),
  `CollectAlternateVideoUrls` (`SwitchOtherUrlAddressDown` 的候选 URL 收集双循环已抽出并
  pinned；其开头 `Log.Debug`、`otherUrls.Count > 0` 守卫下的 `DownloadAsync` HTTP 重试、
  空分支错误日志、tristate `(flowControl, value)` 返回仍在 job 薄壳内、未覆盖；
  `item.Video` / `item.Video.BitRate` 为 null 的 NRE 路径保留不测),
  `PickMergeVideoCoverUrl` (`ProcessImageSetAndMergeToVideo` 的合成视频封面 URL 选取
  已抽出并 pinned，与 slice 6 `PickCoverUrl` 互为兄弟、收齐 cover 决策家族；
  其 `DownAuthorAvatar`/`DownVideoCover` I/O、`virtualBitRate` 构造（含 `FileInfo`
  读盘）、`CreateVideoEntity` 调用与特殊字段赋值、整体 `try/catch` 仍在 job 薄壳内、
  未覆盖；`imageUrls`/`item` 为 null 的 NRE 路径保留不测，由调用方 :911 守护) — all pinned (see table
  above). Still uncovered:
  `ProcessSingleVideo`/`ProcessDynamicVideo`/`ProcessImageSetAndMergeToVideo`
  orchestration bodies, `SaveVideos`,
  `CleanupFailedVideos`, `HandleSyncCompletion` — all retain HTTP / FS / DB
  coupling and will be characterized as further seams are extracted in
  follow-up plans.
- **`AutoDistinct` shell** — the priority 判定 is now extracted to
  `SyncDecisionHelper.ResolveDuplicateVideoAction` and pinned. What remains
  in `AutoDistinct` is a private, DB-coupled I/O shell (guards, JsonConvert,
  DeleteOldViedo, DeleteById) — not directly reachable, not characterized.
- **Frontend (`http.ts` interceptor, `account.ts`)** — no JS toolchain in this
  environment; out of scope for this .NET suite.

## Refactor-safety rule

**A refactor change is only safe if `dotnet test` stays green.**

If a refactor *legitimately* changes observable behavior, update the affected
golden value **in the same commit**, with a one-line justification in the commit
message explaining why the new value is correct. Never weaken or delete an
assertion just to make a refactor pass — that defeats the purpose of the
safety net.
