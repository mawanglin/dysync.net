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
| `DouyinFileNameHelper` | `DouyinFileNameHelperTests` | `SanitizeLinuxFileName` (illegal-char scrub, whitespace strip, folder mode, control chars), `KeepChineseLettersAndNumbers`, `RemoveNumberSuffix`, `LimitUnifiedCount` |
| `Md5Util.Md5` | `PureHelperTests` | Standard RFC-1321 MD5 output |
| `SyncDecisionHelper` | `SyncDecisionHelperTests` | `GetNextCursor` (Cursor→MaxCursor→`"0"`, null-safe), `IsAwemeValid` (3-level null guard), `GetVideoTags` (per-level pick, missing→null), `IsSyncLimitReached` (cate 30-cap vs `BatchCount` cap, `OnlySyncNew` passthrough, `dy_follows` `!FullSync`, mix/series short-circuit), `BuildVideoEntity` (纯字段映射 / cate 标题覆盖 / `OnlyImgOrOnlyMp3` / `DyUserId` 分支 / `AuthorAvatarUrl` 回落 / `FileSize` 零回落), `PickBestVideoBitRate` (encoder=265 优先 H.265 + 回退 H.264 / 默认或 ≠265 仅 H.264 / 空或 null `UrlList` 跳过), `BuildVideoFileName` (custom_collect Format vs mp4 兜底 / mix-series 数字 episode 的 S01E{D2} / 默认 AwemeId.mp4 / cate 非 custom_collect 走默认), `BuildVideoSaveFolderCandidates` (primary = SavePath/sanitized-subFolder / collisionResolved 追加 _{AwemeId} / 空 Desc 走 AwemeId 兜底 / 非法字符经 Sanitize), `PickCoverUrl` (cate 三级兜底 MixInfo→Video(Last)→Music / 非-cate Video(First)→Images / 全空→null), `BuildCoverPosterPath` (dy_mix·dy_series→poster.jpg / 其余→{名}-poster.jpg), `ResolveDuplicateVideoAction` (优先级去重判定：空表→默认 dy_favorite 最高 / 配置表→Sort 最小者最高 / 四层嵌套产出 SkipDownload·ReplaceExisting / 缺项 Sort 回退 int.MaxValue), `PickAuthorAvatarUrl` (头像 URL 选取：AvatarLarger 优先 → AvatarThumb 回落，各取 UrlList 首个 / 全空→null) |
| `VideoTitleGenerator.Generate` | `PureHelperTests` | Placeholder substitution, char-filtering of title/author, unknown-token passthrough, empty-field placeholder, 60-char cap |
| `DouyinVideoService.GetStatics` | `VideoStatsCharacterizationTests` | Full `VideoStaticsDto` snapshot: counts by type, distinct author/category, GB size formatting incl. the `<0.01` zero-substitution branch, **plus the `Categories` list (Tag1 grouping, empty→`其他`, desc order) and `Authors` list (Author grouping, desc order, last-row `Icon`/`UperId` semantics)** |
| `DouyinVideoService.GetChartData` | `VideoStatsCharacterizationTests` | Per-day `SyncTime` grouping and per-type counts (Graphic = empty `FileHash`), **single-day and multi-day group ordering** |

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
  的 NRE 路径保留不测) — all pinned (see table
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
