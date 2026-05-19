# 设计：抽取 DouyinBasicSyncJob 纯逻辑 + 特征化测试

日期：2026-05-19
分支：`decompile/dy-sync-lib`
关联：`docs/code-review-2026-05-18.md`（WARNING：1520 行 god-class `job/DouyinBasicSyncJob.cs`）；
特征化安全网纪律见 `tests/README.md`。

## 背景与目标

`job/DouyinBasicSyncJob.cs` 是 1520 行的 `abstract` 类，有 6 个子类，注入 7 个 service，
约 30 个方法把「调度编排 / HTTP 抓取 / 文件系统 / 数据库 / 纯逻辑」纠缠在一起，
目前完全无测试覆盖。

完整拆解 god-class 风险最高。本轮只做**最低风险的第一刀**：把其中**真正无 I/O、
无非确定性**的纯决策逻辑抽到可独立测试的静态 helper，并用 golden-master 特征化测试
锁死当前行为。这一步本身就是后续更大拆分的安全网。

**显式不做**：不动编排/HTTP/FS/DB 流程；不碰 `CreateVideoEntity`（含 NFO 文件写入
副作用 + `IdGener`/`DateTime.Now` 非确定字段，留待后续计划）；不碰 `AutoDistinct`；
不做任何无关重构。

## 范围内的四个方法

| 方法 | 当前位置 | 纯度判定 |
|------|----------|----------|
| `GetNextCursor(DouyinVideoInfoResponse)` | 行 190，`private static` | 真纯：`data?.Cursor ?? data?.MaxCursor ?? "0"` |
| `IsAwemeValid(Aweme)` | 行 1327，`private static` | 真纯：item/Video/BitRate 三级非空校验 |
| `GetVideoTags(Aweme)` | 行 1335，`protected` 实例 | 不用任何实例状态，纯 LINQ over `item.VideoTags` |
| `IsSyncLimitReached(...)` | 行 483，`private` 实例 | 决策确定；仅依赖抽象属性 `VideoType`（改为入参）与 `Serilog.Log.Debug`（全局静态门面，非实例状态） |

## 架构

### 1. 新增 `utils/SyncDecisionHelper.cs`

- `namespace dy.net.utils`，`public static class SyncDecisionHelper`（与 `DouyinFileNameHelper`、
  `VideoTitleGenerator`、`Md5Util`、`NfoFileGenerator` 同目录同命名约定）。
- 四个方法逻辑**逐字**搬入，仅做必要的签名调整：
  - `GetNextCursor`、`IsAwemeValid`、`GetVideoTags`：签名不变，改为 `public static`。
  - `IsSyncLimitReached`：把抽象属性 `VideoType` 提升为首个参数
    `VideoTypeEnum videoType`；方法体内 `VideoType` 全部替换为 `videoType`；
    `Serilog.Log.Debug(...)` 与 `videoType.GetDesc()` 原样保留（测试不断言日志）。
- 保留 `IsSyncLimitReached` 既有行为，包括 `dy_follows` 分支对 `followed.FullSync`
  的解引用（若 `followed` 为 null 抛 NPE 是现有行为，特征化测试不去覆盖该不可达路径）。

### 2. `DouyinBasicSyncJob` 改为薄委托

不删原方法、不改任何签名/可见性/调用点；6 个子类零改动。四个原方法体替换为单行委托：

```csharp
private static string GetNextCursor(DouyinVideoInfoResponse data)
    => SyncDecisionHelper.GetNextCursor(data);

private static bool IsAwemeValid(Aweme item)
    => SyncDecisionHelper.IsAwemeValid(item);

protected (string tag1, string tag2, string tag3) GetVideoTags(Aweme item)
    => SyncDecisionHelper.GetVideoTags(item);

private bool IsSyncLimitReached(DouyinCookie cookie, AppConfig config, int syncCount,
        DouyinCollectCate cate, DouyinFollowed followed)
    => SyncDecisionHelper.IsSyncLimitReached(VideoType, cookie, config, syncCount, cate, followed);
```

Job 的可观察行为完全不变，纯逻辑变为可独立测试。注释/region 结构按现有风格保留。

### 3. 特征化测试 `tests/dy.net.Tests/SyncDecisionHelperTests.cs`

- 纯内存，**不使用** `TestDb`（无 DB 依赖）。
- 遵循 golden-master 纪律（`tests/README.md`）：先用占位常量跑出实际值，再把观察到的值锁死；
  绝不为让测试通过而弱化断言。
- 用例覆盖：
  - **GetNextCursor**：Cursor 非空优先；Cursor 空回落 MaxCursor；两者皆空返回 `"0"`；
    `data` 为 null 返回 `"0"`。
  - **IsAwemeValid**：`item`/`item.Video`/`item.Video.BitRate` 任一为 null → false；
    三者齐全 → true。
  - **GetVideoTags**：Level 1/2/3 全命中；缺某级返回该级 null；`item.VideoTags` 为 null
    返回三个 null。
  - **IsSyncLimitReached** 分支矩阵：
    - `cate != null && cate.CateType != dy_custom_collect` 且 `syncCount >= 30` → true；
    - `cate` 为 null 或 custom 且 `syncCount >= config.BatchCount` → true；
    - 未达上限时 `VideoType ∈ {dy_collects, dy_favorite}` → 返回 `config.OnlySyncNew`
      （取 true 与 false 两种 config）；
    - 未达上限时 `VideoType == dy_follows` → 返回 `!followed.FullSync`
      （`FullSync` 真/假各一例）；
    - 未达上限时 `VideoType ∈ {dy_mix, dy_series}` → 表达式
      `videoType == dy_follows && !followed.FullSync` 因首段为 false 而短路，
      返回 false（不解引用 `followed`）。

### 4. 文档更新 `tests/README.md`

- 「What is pinned」表新增一行：`SyncDecisionHelper` → `SyncDecisionHelperTests` →
  锁定 `GetNextCursor`/`IsAwemeValid`/`GetVideoTags`/`IsSyncLimitReached` 当前行为。
- 「What is intentionally NOT covered」中 `DouyinBasicSyncJob` 条目更新为：纯决策逻辑
  已抽出并 pinned；编排/HTTP/FS/DB 仍未覆盖，待后续接缝抽取计划。

## 测试策略与正确性

- 这是行为保持型重构：成功判据是构建 0 错误且 `dotnet test` 全绿
  （现有 17 个 + 新增 `SyncDecisionHelperTests`）。
- 委托是纯结构搬移，逻辑零改动；若某断言初次运行得到的就是「当前行为」，即锁死该值，
  并在测试旁注明它 pin 的是现状而非应然。
- 若发现搬移过程中行为确有变化，必须在同一提交内更新对应 golden 值并在 commit message
  注明原因（沿用 `tests/README.md` 的 refactor-safety 规则）。

## 验证与收尾

- 构建/测试统一用 `DOTNET_ROLL_FORWARD=LatestMajor`（本机 SDK 10，项目 target net8.0）。
- 显式 `git add <path>` 仅暂存目标文件（不用 `git add -A`，避免 CRLF 抖动）。
- 以 `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'` 提交。
- 推送到 `origin decompile/dy-sync-lib`；**不合并、不开 PR**（既定约束）。
- 收尾后更新项目记忆，标注本 WARNING 子项进展。
