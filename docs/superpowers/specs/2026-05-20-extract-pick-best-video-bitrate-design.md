# 设计：抽取 GetBestMatchedVideoUrl 纯码流选择逻辑 + 特征化测试

日期：2026-05-20
分支：`decompile/dy-sync-lib`
关联：`docs/code-review-2026-05-18.md`（WARNING：~1440 行 god-class `job/DouyinBasicSyncJob.cs`）；
前序刀 `docs/superpowers/specs/2026-05-19-extract-syncjob-pure-logic-design.md`、
`docs/superpowers/specs/2026-05-19-extract-createvideoentity-mapping-design.md`（已落地）；
特征化安全网纪律见 `tests/README.md`。

## 背景与目标

`GetBestMatchedVideoUrl`（`job/DouyinBasicSyncJob.cs:902-923`）是 `private static` 方法，
根据 `AppConfig.VideoEncoder` 在 H.265/H.264 码流间挑选最高 `BitRateValue` 的可播放档位。
无 I/O、无字段访问、唯一调用点为 `ProcessSingleVideo` 行 844，与子类 override 无关。

延续「最低风险、行为保持、先有特征化测试」纪律，本轮把此方法**逐字**搬到
`SyncDecisionHelper.PickBestVideoBitRate`，job 内保留 `private static` 薄壳委托，
与已落地的 `IsAwemeValid` 风格完全一致。

**显式不做**：不改方法签名（继续接 `Aweme` + `AppConfig`，不瘦身为 `IEnumerable<VideoBitRate>+int?`）；
不动唯一调用点 `ProcessSingleVideo`；不动 7 个子类；不改其他纯逻辑；不引入新抽象；
不修复既有的 `item.Video.BitRate` 在 null 时会 NRE 的现状（由 `IsAwemeValid` 守卫，保持不变）。

## 现状分析

`GetBestMatchedVideoUrl` 组成（`job/DouyinBasicSyncJob.cs:902-923`）：

| 部分 | 行 | 纯度 |
|------|----|------|
| `if (config.VideoEncoder.HasValue && config.VideoEncoder.Value == 265)` 分支 | 905-913 | 纯（H.265 优先 + H.264 回退） |
| `else` 分支 | 916-920 | 纯（只挑 H.264） |
| 返回 `v`（可能为 null） | 922 | 纯 |

依赖：`item.Video.BitRate`（`List<VideoBitRate>`）、`v.IsH265`、`v.PlayAddr.UrlList`、`v.BitRateValue`。
全部为 model 字段读取，无方法调用副作用。

调用点：仅 `DouyinBasicSyncJob.ProcessSingleVideo` 行 844；前置守卫
`IsAwemeValid(item)` 在行 841（已通过 `SyncDecisionHelper.IsAwemeValid` 委托），
保证 `item.Video.BitRate != null`。

可见性：`private static`（基类内），子类零引用，子类零改动。

## 架构

### 1. `utils/SyncDecisionHelper.cs` 新增 `PickBestVideoBitRate`

复用已有 helper（`namespace dy.net.utils`，`public static class SyncDecisionHelper`）。
追加方法，**不动**既有 `GetNextCursor`/`IsAwemeValid`/`GetVideoTags`/`IsSyncLimitReached`/`BuildVideoEntity`。

```csharp
/// <summary>
/// 从 DouyinBasicSyncJob.GetBestMatchedVideoUrl 抽出的纯码流选择逻辑。
/// 行为逐字保留：encoder=265 时优先 H.265，无则回退 H.264；
/// 否则只挑 H.264。二者均按 BitRateValue 降序取首；
/// 只考虑 PlayAddr.UrlList 非空（非 null 且 Any()）的码流。
/// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
/// </summary>
public static VideoBitRate PickBestVideoBitRate(Aweme item, AppConfig config)
{
    VideoBitRate v;
    if (config.VideoEncoder.HasValue && config.VideoEncoder.Value == 265)
    {
        v = item.Video.BitRate.Where(v => v.IsH265 == 1 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                        .OrderByDescending(v => v.BitRateValue)
                        .FirstOrDefault();
        v ??= item.Video.BitRate.Where(v => v.IsH265 == 0 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                        .OrderByDescending(v => v.BitRateValue)
                        .FirstOrDefault();
    }
    else
    {
        v = item.Video.BitRate.Where(v => v.IsH265 == 0 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                          .OrderByDescending(v => v.BitRateValue)
                          .FirstOrDefault();
    }
    return v;
}
```

- 行 902-923 的逻辑**逐字**搬入。表达式、lambda 变量名（外层 `v` 与 `Where` lambda 内的 `v`
  同名 shadowing）、`OrderByDescending`/`FirstOrDefault` 用法、`v ??= …` 回退写法、
  空白与花括号风格原样保留。
- 不做任何"美化"或"防御性"修改（不补 `item.Video?` 空判，不引入提前 return，
  不把 `else` 改成早返回）。

### 2. `DouyinBasicSyncJob.GetBestMatchedVideoUrl` 改为薄委托

签名/可见性/调用点不变（仍 `private static VideoBitRate`，调用点行 844 不动）。
方法体替换为：

```csharp
private static VideoBitRate GetBestMatchedVideoUrl(Aweme item, AppConfig config)
    => SyncDecisionHelper.PickBestVideoBitRate(item, config);
```

XML 文档注释（若有；当前为空，无须搬）保持原状。
与 `IsAwemeValid` 在 line 1288 的薄壳格式（expression-bodied + 同行 `=>`）完全一致。

### 3. 特征化测试 `tests/dy.net.Tests/SyncDecisionHelperTests.cs` 追加

- 纯内存，**不使用** `TestDb`（无 DB 依赖）。
- 遵循 golden-master 纪律（`tests/README.md`）：每个用例针对单一不变量，
  先跑出实际值再锁死，绝不为通过而弱化断言。
- 用例覆盖（每个用例对应一个 `[Fact]`）：

| # | 用例名（示意） | 输入 | 期望 |
|---|---|---|---|
| 1 | `PickBestVideoBitRate_H265Preferred_PicksHighestH265` | `VideoEncoder=265`，BitRate 含 H.265 多档（不同 BitRateValue）+ H.264 多档 | 返回 `IsH265==1` 中最高 `BitRateValue` 的实例 |
| 2 | `PickBestVideoBitRate_H265Preferred_FallsBackToH264_WhenNoH265Playable` | `VideoEncoder=265`，H.265 全部 `UrlList` 空 / `PlayAddr=null`，H.264 多档 | 返回 H.264 中最高 `BitRateValue` 的实例 |
| 3 | `PickBestVideoBitRate_H265Preferred_ReturnsNull_WhenNoPlayableAtAll` | `VideoEncoder=265`，所有码流 `PlayAddr=null` 或 `UrlList` 空 | 返回 null |
| 4 | `PickBestVideoBitRate_DefaultEncoder_PicksHighestH264_IgnoresH265` | `VideoEncoder=null`，BitRate 含 H.265（最高 BitRateValue）+ H.264 多档 | 返回 H.264 最高 `BitRateValue`，**不**返回任何 H.265 |
| 5 | `PickBestVideoBitRate_EncoderNot265_PicksHighestH264_IgnoresH265` | `VideoEncoder=264`（任何 `≠265` 的有值情况） | 同 #4：只挑 H.264 |
| 6 | `PickBestVideoBitRate_SkipsBitRatesWithNullOrEmptyUrlList` | `VideoEncoder=null`，H.264 列表包含：`PlayAddr=null`、`UrlList=null`、`UrlList=[]`、合法 `UrlList=["x"]` 各一条；其中"合法"那条的 `BitRateValue` 并非最高 | 返回唯一可播放（`UrlList.Any()` 为真）的那条，**不会**因为其它"更高码率但不可播放"的存在而被遮蔽 |

每个用例独立构造最小 `Aweme`（仅含 `Video.BitRate` 列表 + 必需引用），
其余 Aweme 字段保持默认（避免与 BuildVideoEntity 用例的 Aweme builder 耦合，必要时复用现有 helper）。

- 断言粒度：每个 `[Fact]` 只断言**一个**不变量（例如 #1 同时断言 `IsH265==1` 且
  `BitRateValue==<最高值>` 是允许的，因为这是"挑选结果"的语义不变量；但
  "encoder=null 不返回 H.265" 与 "encoder=null 返回最高 H.264" 应拆成两个 Fact）。
- 在测试旁短注释标注「pin 的是现状而非应然」（沿用前两刀风格）。

**预期数量**：当前 SyncDecisionHelperTests 包含 29 个（已含 BuildVideoEntity 的 9 个），
全套 dy.net.Tests = 46 全绿。本刀新增 6–7 个 Fact，目标全套 52–53 全绿。
（实际数以执行时点 `dotnet test` 输出为准，spec 不锁死整数。）

### 4. 文档更新 `tests/README.md`

- 「What is pinned」`SyncDecisionHelper` 行追加 `PickBestVideoBitRate`（H.265 优先 +
  H.264 回退 / 默认 H.264 only / 跳过空 UrlList）。
- 「What is intentionally NOT covered」`DouyinBasicSyncJob` 条目更新：
  `GetBestMatchedVideoUrl` 纯码流挑选已抽出并 pinned；剩余 `CreateSaveFolder` /
  `GetVideoFileName` / `AutoDistinct` / 编排 / HTTP / FS / DB 仍待后续接缝抽取。

## 测试策略与正确性

- 行为保持型重构：成功判据 = 构建 0 错误（`dotnet build` 含 `dy.net` Web 项目）且
  `dotnet test` 全绿（现 46 个 + 新增 PickBestVideoBitRate 用例）。
- 委托是纯结构搬移，逻辑零改动；首次运行得到的即「当前行为」，锁死该值并在测试旁
  注明 pin 的是现状而非应然。
- 若搬移过程中行为确有变化，必须在同一提交内更新对应 golden 值并在 commit message
  注明原因（沿用 `tests/README.md` 的 refactor-safety 规则）。
- 子类不动：DouyinFollowedSyncJob / DouYinCollectSyncJob / DouYinFavoritSyncJob /
  DouyinCollectCustomSyncJob / DouyinMixSyncJob / DouyinSeriesSyncJob / DouyinJob
  全部零改动，由编译器保证（搜索确认 `GetBestMatchedVideoUrl` 引用仅在基类）。

## 验证与收尾

- 构建/测试统一用 `DOTNET_ROLL_FORWARD=LatestMajor`（本机 SDK 10，项目 target net8.0）。
- 显式 `git add <path>` 仅暂存目标文件（不用 `git add -A`，避免 CRLF 抖动）。
  涉及文件：`utils/SyncDecisionHelper.cs`、`job/DouyinBasicSyncJob.cs`、
  `tests/dy.net.Tests/SyncDecisionHelperTests.cs`、`tests/README.md`、
  实现计划与本 spec（`docs/superpowers/{specs,plans}/...`）。
- 以 `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'` 提交，
  沿用前两刀的提交作者风格（最近 3 个 commit 均为此 author）。
- 推送到 `origin decompile/dy-sync-lib`；**不合并、不开 PR**（既定约束）。
- 收尾后更新项目记忆，标注本 WARNING 子项进展（god-class 拆分进度：
  上两刀 + 本刀 PickBestVideoBitRate 完成）。
