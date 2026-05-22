# 设计：抽取动态视频 URL 构建逻辑（BuildDynamicVideoUrls）+ 特征化测试

日期：2026-05-23
分支：`decompile/dy-sync-lib`
关联：`docs/code-review-2026-05-18.md`（WARNING：~1296 行 god-class `job/DouyinBasicSyncJob.cs`）；
前序刀：
- `docs/superpowers/specs/2026-05-19-extract-syncjob-pure-logic-design.md`
- `docs/superpowers/specs/2026-05-19-extract-createvideoentity-mapping-design.md`
- `docs/superpowers/specs/2026-05-20-extract-pick-best-video-bitrate-design.md`
- `docs/superpowers/specs/2026-05-20-extract-buildvideofilename-design.md`
- `docs/superpowers/specs/2026-05-21-extract-buildsavefolder-candidates-design.md`
- `docs/superpowers/specs/2026-05-21-extract-cover-decision-logic-design.md`
- `docs/superpowers/specs/2026-05-22-extract-autodistinct-priority-decision-design.md`
- `docs/superpowers/specs/2026-05-22-extract-author-avatar-url-design.md`

特征化安全网纪律见 `tests/README.md`。

## 背景与目标

`DouyinBasicSyncJob.ProcessVideoList`（`job/DouyinBasicSyncJob.cs:465-660`，~196 行）是
分页同步的核心编排方法。前八刀都收的是 `Down*`/`Build*`/`Get*` 这类微方法里的纯逻辑接缝；
本刀第一次进入**编排体内部**——在 `ProcessVideoList` 的 `else`（单视频处理失败、转图文/
动图分支）里，有一段嵌套三层循环把 `Aweme.Images` 里的动态视频码流抽成
`List<DouyinMergeVideoDto>`（`job/DouyinBasicSyncJob.cs:547-574`）：

- 遍历 `item.Images` → 每个 `img.DynamicVideo.BitRate` → 每个 `btv`；
- 从 `btv.PlayAddr.UrlList` 取**首个**以 `https://www.douyin.com/aweme/v1/play` 打头的 URL；
- 命中则构造 `DouyinMergeVideoDto { Path, Height, Width }` 入列。

这一段是**纯数据变换**——只读 `Aweme`，无 I/O、无字段写入、无 `await`。它外层裹着
`config.DownDynamicVideo` 开关与 `item.Images` 非空判断；开关属编排门、留 job 薄壳。

延续「最低风险、行为保持、先有特征化测试」纪律：把这段三层循环逐字搬到
`SyncDecisionHelper.BuildDynamicVideoUrls(Aweme item)`，`ProcessVideoList` 的 `else` 分支内
保留薄壳调用，`config.DownDynamicVideo` 开关留在 job。

**显式不做**：
- **不抽**该 `else` 分支里后续的 `ProcessDynamicVideo`/`MergeMultipleVideosAsync`/
  `ProcessImageSetAndMergeToVideo` 编排（`job/DouyinBasicSyncJob.cs:576-656`）——含 `await`、
  文件删除、NFO 生成，是 I/O 编排，留在 job；
- **不动** `ProcessVideoList` 签名、可见性、参数、其余方法体；
- **不引入新抽象**（无 `IDynamicVideoSource`、无策略接口）；
- **不引入新文件、不新增枚举**——复用既有 `SyncDecisionHelper`；
- **不修既有怪行为**（见下「现状分析」的 Quirk——死代码兜底）。

## 现状分析

### 抽取目标全貌（`job/DouyinBasicSyncJob.cs:547-574`）

`ProcessVideoList` 的 `else` 分支（单视频 `ProcessSingleVideo` 返回 null 时进入）开头：

```csharp
else
{
    //处理多个视频-组合的图文视频--类似动图。
    List<DouyinMergeVideoDto> dynamicVideoUrls = new List<DouyinMergeVideoDto>();
    // 当需要下载动态视频时，获取其他URL
    if (config.DownDynamicVideo && item.Images != null && item.Images.Count > 0)
    {
        foreach (var img in item.Images)
        {
            if (img.DynamicVideo?.BitRate?.Count > 0)
            {
                foreach (var btv in img.DynamicVideo.BitRate)
                {
                    var targetUrl = btv.PlayAddr?.UrlList?.FirstOrDefault(x => x.StartsWith("https://www.douyin.com/aweme/v1/play"));
                    if (targetUrl != null)
                    {
                        var height = btv.PlayAddr?.Height ?? 1920;
                        var width = btv.PlayAddr?.Width ?? 1080;
                        DouyinMergeVideoDto info = new DouyinMergeVideoDto
                        {
                            Path = targetUrl,
                            Height = height,
                            Width = width
                        };
                        dynamicVideoUrls.Add(info);
                    }
                }
            }
        }
    }

    // 处理核心逻辑
    if (dynamicVideoUrls.Count > 0)
    { ... }   // ← 此后是 ProcessDynamicVideo 等 I/O 编排，不动
```

| 部分 | 纯度 |
|------|------|
| `config.DownDynamicVideo` 开关 | 编排门，**留 job 薄壳** |
| `item.Images != null && item.Images.Count > 0` 判断 | **纯**（仅读 `Aweme`），**移入 helper** |
| 三层循环 + URL 前缀过滤 + `DouyinMergeVideoDto` 构造 | **纯**（仅读 `Aweme`，无 I/O/无字段写入/无 `await`），**移入 helper** |
| `if (dynamicVideoUrls.Count > 0) { ... }` 及之后 | I/O 编排（`await ProcessDynamicVideo` 等），**留 job** |

### DTO 与数据链形状

`Aweme.Images` 为 `List<ImageItemInfo>`（`model/response/DouyinVideoInfoResponse.cs:169`）；
`ImageItemInfo.DynamicVideo` 为 `Video` 类型，`Video.BitRate` 为 `List<VideoBitRate>`；
`VideoBitRate.PlayAddr` 为 `PlayAddr`；`PlayAddr` 含 `List<string> UrlList`、
**非空** `int Height`、**非空** `int Width`（`model/response/DouyinVideoInfoResponse.cs:1231`）。
产物 `DouyinMergeVideoDto { string Path; int Height; int Width; }`（`model/dto/DouyinDynamicVideoDto.cs`）。

### Quirk（不修，逐字保留）：`?? 1920` / `?? 1080` 兜底是不可达死代码

`var height = btv.PlayAddr?.Height ?? 1920;` 与 `?? 1080` 的兜底分支**永不触发**：
执行到该两行的前提是 `targetUrl != null`，而 `targetUrl` 来自 `btv.PlayAddr?.UrlList?...`——
`targetUrl` 非 null 意味着 `btv.PlayAddr` 非 null；`PlayAddr.Height`/`Width` 又是**非空 `int`**，
故 `btv.PlayAddr?.Height` 在此处恒非 null，`?? 1920`/`?? 1080` 恒取左值。
**逐字保留不删**（与第四刀 `BuildVideoFileName` 的「TryParse 失败」死代码同样处理），
特征化测试**不构造**触发该兜底的输入（无法构造），仅 pin `Height`/`Width` 来自
`PlayAddr` 实际值。

### 子类与调用点

`ProcessVideoList` 为 `protected async Task<...>`——**非 `virtual`**，全仓**零子类 override**
（grep 确认仅 `DouyinBasicSyncJob.cs:465` 一处定义、`:421` 一处调用，均在本文件内）。
故本刀**只动 `DouyinBasicSyncJob.cs` 一个 job 文件**。`dynamicVideoUrls` 为该 `else`
分支内的局部变量，无外部引用。

## 架构

### 1. `utils/SyncDecisionHelper.cs` 新增 `BuildDynamicVideoUrls`

复用已有 helper（`namespace dy.net.utils`，`public static class SyncDecisionHelper`）。
追加到类末尾，紧接当前最后一个方法 `PickAuthorAvatarUrl` 之后、类关闭 `}` 之前。
**不动**既有 12 个方法。

`Aweme`/`VideoBitRate` 等属 `dy.net.model.response`，`DouyinMergeVideoDto` 属
`dy.net.model.dto`——`SyncDecisionHelper` 已 `using` 两者；`List<>`/`System.Linq`
（`FirstOrDefault`）由 `<ImplicitUsings>enable</ImplicitUsings>` 覆盖。
**无需新增 `using`。无新文件、无新枚举。**

```csharp
/// <summary>
/// 从 DouyinBasicSyncJob.ProcessVideoList 抽出的纯动态视频 URL 构建逻辑（无 I/O）。
/// 行为逐字保留：遍历 item.Images → 每个 DynamicVideo.BitRate → 取 PlayAddr.UrlList
/// 中首个以 https://www.douyin.com/aweme/v1/play 打头的 URL，命中则构造
/// DouyinMergeVideoDto { Path, Height, Width } 入列；Images 为 null/空时返回空 list。
/// 调用方（job 薄壳）保留 config.DownDynamicVideo 开关，仅在开关开启时调用本方法。
/// PlayAddr.Height/Width 为非空 int，?? 1920 / ?? 1080 兜底为不可达死代码，逐字保留不删。
/// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
/// </summary>
public static List<DouyinMergeVideoDto> BuildDynamicVideoUrls(Aweme item)
{
    List<DouyinMergeVideoDto> dynamicVideoUrls = new List<DouyinMergeVideoDto>();
    // 当需要下载动态视频时，获取其他URL
    if (item.Images != null && item.Images.Count > 0)
    {
        foreach (var img in item.Images)
        {
            if (img.DynamicVideo?.BitRate?.Count > 0)
            {
                foreach (var btv in img.DynamicVideo.BitRate)
                {
                    var targetUrl = btv.PlayAddr?.UrlList?.FirstOrDefault(x => x.StartsWith("https://www.douyin.com/aweme/v1/play"));
                    if (targetUrl != null)
                    {
                        var height = btv.PlayAddr?.Height ?? 1920;
                        var width = btv.PlayAddr?.Width ?? 1080;
                        DouyinMergeVideoDto info = new DouyinMergeVideoDto
                        {
                            Path = targetUrl,
                            Height = height,
                            Width = width
                        };
                        dynamicVideoUrls.Add(info);
                    }
                }
            }
        }
    }
    return dynamicVideoUrls;
}
```

- 逐字搬移：三层循环、`?.` 链、`FirstOrDefault` 谓词、`StartsWith` 前缀、
  `DouyinMergeVideoDto` 对象初始化、`?? 1920`/`?? 1080` 全部原样。
- 唯一改动：原 `if` 条件去掉 `config.DownDynamicVideo &&` 一截——该开关留 job 薄壳；
  `item.Images != null && item.Images.Count > 0` 留在 helper 的 `if` 里。
- `dynamicVideoUrls` 局部变量与 `return` 在 helper 内自洽。
- 中文行内注释 `// 当需要下载动态视频时，获取其他URL` 随循环逻辑迁入 helper。
  该注释提到的「下载动态视频」开关（`DownDynamicVideo`）实际留在 job 薄壳，故注释在
  新位置略显错位——**逐字保留不改写**（与第六刀对「注释与代码顺序不符」的处理一致）。

### 2. `ProcessVideoList` 的 `else` 分支收薄为薄壳

`ProcessVideoList` 签名、可见性、参数、其余方法体全部不变。仅替换该 `else` 分支
开头 `:547-574` 这段（`//处理多个视频...` 注释下、`// 处理核心逻辑` 之上）为：

```csharp
else
{
    //处理多个视频-组合的图文视频--类似动图。
    List<DouyinMergeVideoDto> dynamicVideoUrls = new List<DouyinMergeVideoDto>();
    if (config.DownDynamicVideo)
    {
        dynamicVideoUrls = SyncDecisionHelper.BuildDynamicVideoUrls(item);
    }

    // 处理核心逻辑
    if (dynamicVideoUrls.Count > 0)
    { ... }   // ← 此后逐字不动
```

- `//处理多个视频-组合的图文视频--类似动图。` 注释留在薄壳。
- `List<DouyinMergeVideoDto> dynamicVideoUrls = new List<DouyinMergeVideoDto>();`
  声明保留——`config.DownDynamicVideo` 为 false 时 `dynamicVideoUrls` 仍是空 list，
  后续 `if (dynamicVideoUrls.Count > 0)` 判定不变。
- `config.DownDynamicVideo` 为 true 时改为 `dynamicVideoUrls = SyncDecisionHelper.
  BuildDynamicVideoUrls(item);`——helper 在 `item.Images` 为 null/空时返回空 list，
  与原代码「`if` 条件因 `item.Images` 为空而整体为 false、`dynamicVideoUrls` 留空」
  可观察行为等价。
- 这是**方法体内的局部块替换**，非整方法替换；`else` 分支 `:576` 起的
  `// 处理核心逻辑`、`ProcessDynamicVideo` 编排等逐字不动。

**控制流等价论证**：
- `DownDynamicVideo == false`：原代码 `if` 整体短路 → `dynamicVideoUrls` 空；
  新代码跳过 `if` → `dynamicVideoUrls` 空。等价。
- `DownDynamicVideo == true`：原代码进入 `if`，由 `item.Images`/循环决定 `dynamicVideoUrls`
  内容；新代码调 `BuildDynamicVideoUrls(item)`——该方法体即原 `if` 内逻辑逐字移植，
  返回同一 list。等价。

### 3. 特征化测试 `tests/dy.net.Tests/SyncDecisionHelperTests.cs` 追加

新增小节 `// ---- BuildDynamicVideoUrls ----`，紧接当前最后一节
`PickAuthorAvatarUrl` 之后、类关闭 `}` 之前。

**`// ---- BuildDynamicVideoUrls ----`（8 个 `[Fact]`）**

| # | 用例（示意） | 输入 | 期望 |
|---|---|---|---|
| 1 | `ImagesNull_ReturnsEmptyList` | `item.Images = null` | 非 null 的空 list（`Count==0`） |
| 2 | `ImagesEmpty_ReturnsEmptyList` | `item.Images = []` | 空 list |
| 3 | `DynamicVideoNull_ImageContributesNothing` | 1 个 Image，`DynamicVideo = null` | 空 list（`?.` 短路） |
| 4 | `BitRateEmpty_ImageContributesNothing` | `DynamicVideo.BitRate = []`（Count 0） | 空 list（`?.Count > 0` 为 false） |
| 5 | `PlayAddrNull_BitRateContributesNothing` | 1 个 BitRate，`PlayAddr = null` | 空 list（`btv.PlayAddr?.UrlList` 短路 → `targetUrl` null） |
| 6 | `MatchingUrl_BuildsDtoWithPathAndPlayAddrDimensions` | 1 BitRate，`PlayAddr.UrlList=["https://www.douyin.com/aweme/v1/play/x"]`、`Height=1280`、`Width=720` | 1 个 DTO：`Path` = 该 URL，`Height=1280`，`Width=720` |
| 7 | `UrlListMixed_PicksFirstPlayPrefixUrl` | `UrlList=["https://other.com/a", ".../v1/play/1", ".../v1/play/2"]` | 1 个 DTO，`Path` = `.../v1/play/1`（`FirstOrDefault` 带谓词；非匹配 URL 被跳过；全非匹配则不产 DTO 为其推论） |
| 8 | `MultipleImagesAndBitRates_CollectsEachMatchInEncounterOrder` | 2 个 Image，各含含匹配 URL 的 BitRate | 全部匹配按遍历顺序收进 list |

**测试构造原则**：
- 纯内存，不用 `TestDb`、不碰文件系统、不碰 HTTP。
- 助手方法在小节内部定义（如 `AwemeWithImages(params ImageItemInfo[])` 构造带 `Images`
  的 `Aweme`、`Img(Video dynamicVideo)` 构造 `ImageItemInfo`、`BitRate(int h, int w,
  params string[] urls)` 构造 `VideoBitRate`），与既有小节助手隔离、命名不撞
  （沿用前八刀做法）。若 `Img`/`BitRate` 等名与既有小节助手撞名需另取名。
- 每个 Fact 一个不变量；断言用 `Assert.Empty`/`Assert.Equal`/`Assert.Collection`。
- 测试旁短注释 `// pin: current behavior, not aspirational`。
- **不写** `?? 1920`/`?? 1080` 死代码兜底的测试（无法构造触发输入——见 Quirk）。

### 4. 文档更新 `tests/README.md`

- 「What is pinned」`SyncDecisionHelper` 行追加 `BuildDynamicVideoUrls`
  （动态视频 URL 构建：遍历 Images→DynamicVideo.BitRate，取 PlayAddr.UrlList 首个
  `…/aweme/v1/play` 前缀 URL，构造 `DouyinMergeVideoDto`；Images 空→空 list）。
- 「What is intentionally NOT covered」`DouyinBasicSyncJob` 条目更新：`ProcessVideoList`
  的**纯动态视频 URL 构建段**已抽出并 pinned；其 `config.DownDynamicVideo` 开关、
  `else` 分支后续 `ProcessDynamicVideo`/`MergeMultipleVideosAsync`/`ProcessImageSetAndMergeToVideo`
  I/O 编排仍在 job、未覆盖；`?? 1920`/`?? 1080` 不可达死代码保留、不测。

## 测试策略与正确性

- 行为保持型重构：成功判据 = 构建 0 错误（`dotnet build` 含 `dy.net` Web 项目）且
  `dotnet test` 全绿（现 86 个 + 新增 8 个 = 94）。
- helper 是纯结构搬移，逻辑零改动；首次运行得到的即「当前行为」，锁死该值。
- job 薄壳把纯数据变换段外移，开关门与后续 I/O 编排逐字保留，控制流等价
  （见上「控制流等价论证」）。
- 若搬移过程中行为确有变化，必须在同一提交内更新对应 golden 值并在 commit message
  注明原因（沿用 `tests/README.md` 的 refactor-safety 规则）。
- 子类不动：`ProcessVideoList` 非 virtual、零 override，由 grep + diff 双重确认
  只改 `DouyinBasicSyncJob.cs`。

## 验证与收尾

- 构建/测试统一用 `DOTNET_ROLL_FORWARD=LatestMajor`（本机 SDK 10，项目 target net8.0）。
- 显式 `git add <path>` 仅暂存目标文件（不用 `git add -A`）。涉及文件：
  `utils/SyncDecisionHelper.cs`、`job/DouyinBasicSyncJob.cs`、
  `tests/dy.net.Tests/SyncDecisionHelperTests.cs`、`tests/README.md`、
  实现计划与本 spec（`docs/superpowers/{specs,plans}/...`）。
- 以 `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'` 提交，
  沿用前八刀风格。
- 推送到 `origin decompile/dy-sync-lib`；**不合并、不开 PR**（既定约束）。
- 收尾后更新项目记忆，标注 god-class 拆分进度：九刀完成，测试 86 → 94，
  `SyncDecisionHelper` 达 13 个纯方法。
