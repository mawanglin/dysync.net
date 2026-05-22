# 设计：抽取图片 URL 提取逻辑（BuildImageUrls）+ 特征化测试

日期：2026-05-23
分支：`decompile/dy-sync-lib`
关联：`docs/code-review-2026-05-18.md`（WARNING：~1276 行 god-class `job/DouyinBasicSyncJob.cs`）；
前序刀：
- `docs/superpowers/specs/2026-05-19-extract-syncjob-pure-logic-design.md`
- `docs/superpowers/specs/2026-05-19-extract-createvideoentity-mapping-design.md`
- `docs/superpowers/specs/2026-05-20-extract-pick-best-video-bitrate-design.md`
- `docs/superpowers/specs/2026-05-20-extract-buildvideofilename-design.md`
- `docs/superpowers/specs/2026-05-21-extract-buildsavefolder-candidates-design.md`
- `docs/superpowers/specs/2026-05-21-extract-cover-decision-logic-design.md`
- `docs/superpowers/specs/2026-05-22-extract-autodistinct-priority-decision-design.md`
- `docs/superpowers/specs/2026-05-22-extract-author-avatar-url-design.md`
- `docs/superpowers/specs/2026-05-23-extract-builddynamicvideourls-design.md`

特征化安全网纪律见 `tests/README.md`。

## 背景与目标

`DouyinBasicSyncJob.ProcessImageSetAndMergeToVideo`（`job/DouyinBasicSyncJob.cs:925-1049`，
~125 行）负责「图文视频」处理：下载图片、合成视频、处理封面/头像、写库实体。整个方法体
裹在一个 `try/catch` 里，混了大量 HTTP/FS I/O。

第九刀抽出了 `ProcessVideoList` 里从 `item.Images[].DynamicVideo.BitRate` 取**动图** URL 的
`BuildDynamicVideoUrls`。本刀抽的是同一个 `item.Images` 字段的**另一条消费路径**——
`ProcessImageSetAndMergeToVideo` 开头从 `item.Images[].UrlList` 取**静态图** URL 的那段
LINQ 链（`job/DouyinBasicSyncJob.cs:929-934`）：

- `item.Images?.Where(UrlList 非空).Select(取首 URL+宽高 → DouyinMergeVideoDto)
  .Where(Path 非空白).ToList()` —— **纯数据变换**（只读 `Aweme`，无 I/O、无 `await`、
  无字段写入）。

延续「最低风险、行为保持、先有特征化测试」纪律：把这段 LINQ 链逐字搬到
`SyncDecisionHelper.BuildImageUrls(Aweme item)`，`ProcessImageSetAndMergeToVideo` 内
保留薄壳调用。两刀做完，`item.Images` 的两种消费路径（动图 / 静态图）就被完整刻画，
`SyncDecisionHelper` 里 `BuildDynamicVideoUrls` + `BuildImageUrls` 成对、自洽。

**显式不做**：
- **不抽**该方法后续的合成编排（`MergeToVideo`、`config.DownImageVideo` 校验与清理、
  `coverUrl` 派生、`DownAuthorAvatar`/`DownVideoCover`、`virtualBitRate` 构造、
  `CreateVideoEntity` 调用、特殊字段赋值）——含 I/O 与 `await`，留在 job；
- **不动** `ProcessImageSetAndMergeToVideo` 签名、可见性、参数、`try/catch`、其余方法体；
- **不引入新抽象**（无接口）；**不引入新文件、不新增枚举**——复用既有 `SyncDecisionHelper`；
- **不修既有怪行为**（见下「现状分析」的两处 Quirk）。

## 现状分析

### 抽取目标全貌（`job/DouyinBasicSyncJob.cs:927-940`）

```csharp
try
{
    // 提取图片URL列表
    List<DouyinMergeVideoDto> imageUrls = item.Images?
    .Where(img => img.UrlList != null && img.UrlList.Any())
    .Select(img => new DouyinMergeVideoDto { Path = img.UrlList.FirstOrDefault(), Height = img.Height, Width = img.Width })
    .Where(img => !string.IsNullOrWhiteSpace(img.Path))
    .ToList();

    // 如果没有图片，返回null
    if (imageUrls == null || !imageUrls.Any())
    {
        return null;
    }
    ...
```

| 部分 | 纯度 |
|------|------|
| `item.Images?.Where(...).Select(...).Where(...).ToList()` 链 | **纯**（仅读 `Aweme`，无 I/O / 无 `await` / 无字段写入），**移入 helper** |
| `if (imageUrls == null || !imageUrls.Any()) return null;` 守卫 | 编排前置门，**留在 job** |
| 其后的合成 I/O 编排 | I/O，**留在 job** |

### DTO 与数据形状

`Aweme.Images` 为 `List<ImageItemInfo>`（`model/response/DouyinVideoInfoResponse.cs:1260`）；
`ImageItemInfo` 含 `List<string> UrlList`、**非空** `int Height`、**非空** `int Width`。
产物 `DouyinMergeVideoDto { string Path; int Height; int Width; }`
（`model/dto/DouyinDynamicVideoDto.cs`）。

### Quirk 1（不修，逐字保留）：`item.Images?` 为 null 时返回 `null`（非空 list）

LINQ 链以 `item.Images?` 起头。`item.Images` 为 null 时整条 `?.` 链短路，
`BuildImageUrls` 返回 **`null`**——与第九刀 `BuildDynamicVideoUrls`（`item.Images` 为 null
时返回**空 list**）不同。这是既有行为，逐字保留 `?.`。调用方那行守卫
`if (imageUrls == null || !imageUrls.Any())` 同时吃 `null` 与空 list，故 helper 返回 `null`
不影响下游。特征化测试明确 pin「Images=null → 返回 null」与「Images=空 → 返回空 list」
两种结果的区别。

### Quirk 2（不修，逐字保留）：只看 `UrlList` 首个 URL

`.Select` 内 `Path = img.UrlList.FirstOrDefault()` 只取每张图 `UrlList` 的**首个** URL。
若首个 URL 为空白字符串，随后的 `.Where(img => !string.IsNullOrWhiteSpace(img.Path))`
会把整张图丢掉——**即便该图 `UrlList` 后面还有有效 URL**。这是既有行为，逐字保留。
（首个 `.Where(img.UrlList != null && img.UrlList.Any())` 仅保证 `UrlList` 非 null 且非空，
不保证首元素非空白。）

### 子类与调用点

`ProcessImageSetAndMergeToVideo` 为 `protected async Task<DouyinVideo>`——**非 `virtual`**，
全仓**零子类 override**（grep 确认仅 `DouyinBasicSyncJob.cs:925` 一处定义、`:623` 一处调用，
均在本文件内）。故本刀**只动 `DouyinBasicSyncJob.cs` 一个 job 文件**。`imageUrls` 在
`ProcessImageSetAndMergeToVideo` 内另有引用（`:937` 守卫、`:974` 合成请求参数 `ImageUrls`、
`:1012-1013` `coverUrl` 派生），均**不动**——它们继续读 job 内的局部变量 `imageUrls`。

## 架构

### 1. `utils/SyncDecisionHelper.cs` 新增 `BuildImageUrls`

复用已有 helper（`namespace dy.net.utils`，`public static class SyncDecisionHelper`）。
追加到类末尾，紧接当前最后一个方法 `BuildDynamicVideoUrls` 之后、类关闭 `}` 之前
（Build*Urls 家族成对相邻）。**不动**既有 13 个方法。

`Aweme` 属 `dy.net.model.response`、`DouyinMergeVideoDto` 属 `dy.net.model.dto`——
`SyncDecisionHelper` 已 `using` 两者；`List<>`/`System.Linq` 由
`<ImplicitUsings>enable</ImplicitUsings>` 覆盖。**无需新增 `using`。无新文件、无新枚举。**

```csharp
/// <summary>
/// 从 DouyinBasicSyncJob.ProcessImageSetAndMergeToVideo 抽出的纯图片 URL 提取逻辑（无 I/O）。
/// 行为逐字保留：遍历 item.Images，保留 UrlList 非空者，取每张图 UrlList 首个 URL 与宽高
/// 构造 DouyinMergeVideoDto，再滤掉 Path 为空白者。
/// 注意 item.Images 为 null 时 ?. 短路 → 返回 null（非空 list），与 BuildDynamicVideoUrls 不同；
/// 调用方守卫同时吃 null 与空 list。只取每张图 UrlList 首个 URL，首个为空白则整张图被丢弃。
/// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
/// </summary>
public static List<DouyinMergeVideoDto> BuildImageUrls(Aweme item)
{
    // 提取图片URL列表
    return item.Images?
        .Where(img => img.UrlList != null && img.UrlList.Any())
        .Select(img => new DouyinMergeVideoDto { Path = img.UrlList.FirstOrDefault(), Height = img.Height, Width = img.Width })
        .Where(img => !string.IsNullOrWhiteSpace(img.Path))
        .ToList();
}
```

- 逐字搬移：`?.` 链、两个 `.Where` 谓词、`.Select` 对象初始化、`FirstOrDefault`、`.ToList()`
  全部原样。原 `List<DouyinMergeVideoDto> imageUrls = item.Images?...;` 的赋值在 job 内由
  `var imageUrls = SyncDecisionHelper.BuildImageUrls(item);` 替代，故 helper 直接 `return`。
- 唯一与原文不同的是缩进：原 LINQ 链的 `.Where`/`.Select`/`.ToList()` 在原方法里平齐于
  16 空格列，迁入 helper 后按 helper 体嵌套层级重新缩进——纯排版、无行为变化。
- 中文行内注释 `// 提取图片URL列表` 随逻辑迁入 helper。

### 2. `ProcessImageSetAndMergeToVideo` 收薄为薄壳

`ProcessImageSetAndMergeToVideo` 签名、可见性、参数、`try/catch`、其余方法体全部不变。
仅替换 `try` 块开头 `:929-934` 那条声明（`// 提取图片URL列表` 注释 + LINQ 链）为单行：

```csharp
try
{
    List<DouyinMergeVideoDto> imageUrls = SyncDecisionHelper.BuildImageUrls(item);

    // 如果没有图片，返回null
    if (imageUrls == null || !imageUrls.Any())
    {
        return null;
    }
    ...   // ← 此后逐字不动
```

- 唯一改动：原 `// 提取图片URL列表` + `List<DouyinMergeVideoDto> imageUrls = item.Images?
  ....ToList();` → 单行 `List<DouyinMergeVideoDto> imageUrls = SyncDecisionHelper.
  BuildImageUrls(item);`。`// 提取图片URL列表` 注释随逻辑迁入 helper，job 内该行不再保留注释。
- `if (imageUrls == null || !imageUrls.Any()) return null;` 守卫与其后所有逻辑逐字保留。
- 控制流等价：纯 pocket 是无副作用取值，搬出后 job 在同一位置拿到同一 `imageUrls`
  （同为 `null` 或同一内容的 list），后续守卫与合成编排判定不变。
- 这是**方法体内的局部块替换**，非整方法替换。

### 3. 特征化测试 `tests/dy.net.Tests/SyncDecisionHelperTests.cs` 追加

新增小节 `// ---- BuildImageUrls ----`，紧接当前最后一节 `BuildDynamicVideoUrls` 之后、
类关闭 `}` 之前。

**`// ---- BuildImageUrls ----`（8 个 `[Fact]`）**

| # | 用例（示意） | 输入 | 期望 |
|---|---|---|---|
| 1 | `ImagesNull_ReturnsNull` | `item.Images = null` | `null`（`?.` 短路；区别于 BuildDynamicVideoUrls） |
| 2 | `ImagesEmpty_ReturnsEmptyList` | `item.Images = []` | 非 null 的空 list（`Count==0`） |
| 3 | `UrlListNull_ImageFilteredOut` | 1 张图，`UrlList = null` | 空 list（首个 `.Where` 滤除） |
| 4 | `UrlListEmpty_ImageFilteredOut` | 1 张图，`UrlList = []` | 空 list（`UrlList.Any()` false） |
| 5 | `ValidImage_BuildsDtoWithFirstUrlAndDimensions` | 1 张图，`UrlList=["u1"]`、`Height=1920`、`Width=1080` | 1 个 DTO：`Path="u1"`、`Height=1920`、`Width=1080` |
| 6 | `MultipleUrls_TakesFirstUrl` | 1 张图，`UrlList=["u1","u2"]` | 1 个 DTO，`Path="u1"`（`.Select` 取 `FirstOrDefault`） |
| 7 | `FirstUrlBlank_ImageFilteredOut` | 1 张图，`UrlList=["   ","u2"]` | 空 list（首 URL 空白 → 次个 `.Where` 滤除，即便有有效次 URL——Quirk 2） |
| 8 | `MultipleImagesMixed_KeepsOnlyValidInEncounterOrder` | 多张图：有效 / `UrlList` null / 首 URL 空白 / 有效 混排 | 仅有效图按遍历顺序进 list |

**测试构造原则**：
- 纯内存，不用 `TestDb`、不碰文件系统、不碰 HTTP。
- 助手方法在小节内部定义（如 `ImageUrlItem(int height, int width, params string[] urls)`
  构造 `ImageItemInfo`、`AwemeWithImageItems(params ImageItemInfo[])` 构造带 `Images` 的
  `Aweme`），与既有小节助手隔离、命名不撞（沿用前九刀做法）。注意第九刀已用
  `AwemeWithImages`/`DynImage` 等名，本节助手须另取名（如 `ImageUrlItem`/
  `AwemeWithImageItems`），实现前 grep 校验。
- 每个 Fact 一个不变量；断言用 `Assert.Null`/`Assert.Empty`/`Assert.Equal`/`Assert.Collection`。
- 测试旁短注释 `// pin: current behavior, not aspirational`。

### 4. 文档更新 `tests/README.md`

- 「What is pinned」`SyncDecisionHelper` 行追加 `BuildImageUrls`
  （图片 URL 提取：遍历 Images 取每张图 UrlList 首个 URL 构造 DouyinMergeVideoDto /
  UrlList 空或首 URL 空白→滤除 / Images=null→null、Images=空→空 list）。
- 「What is intentionally NOT covered」`DouyinBasicSyncJob` 条目更新：追加一句
  `BuildImageUrls`（`ProcessImageSetAndMergeToVideo` 的图片 URL 提取段已抽出并 pinned；
  其 `MergeToVideo` 合成、`config.DownImageVideo` 校验与文件清理、`coverUrl` 派生、
  `DownAuthorAvatar`/`DownVideoCover` I/O、`virtualBitRate` 构造（含 `FileInfo` 读盘）、
  `CreateVideoEntity` 调用与特殊字段赋值、整体 `try/catch` 仍在 job、未覆盖）。

## 测试策略与正确性

- 行为保持型重构：成功判据 = 构建 0 错误（`dotnet build` 含 `dy.net` Web 项目）且
  `dotnet test` 全绿（现 94 个 + 新增 8 个 = 102）。
- helper 是纯结构搬移，逻辑零改动；首次运行得到的即「当前行为」，锁死该值。
- job 薄壳仅把无副作用取值外移，守卫与 I/O 编排逐字保留，控制流等价。
- 若搬移过程中行为确有变化，必须在同一提交内更新对应 golden 值并在 commit message
  注明原因（沿用 `tests/README.md` 的 refactor-safety 规则）。
- 子类不动：`ProcessImageSetAndMergeToVideo` 非 virtual、零 override，由 grep + diff
  双重确认只改 `DouyinBasicSyncJob.cs`。

## 验证与收尾

- 构建/测试统一用 `DOTNET_ROLL_FORWARD=LatestMajor`（本机 SDK 10，项目 target net8.0）。
- 显式 `git add <path>` 仅暂存目标文件（不用 `git add -A`）。涉及文件：
  `utils/SyncDecisionHelper.cs`、`job/DouyinBasicSyncJob.cs`、
  `tests/dy.net.Tests/SyncDecisionHelperTests.cs`、`tests/README.md`、
  实现计划与本 spec（`docs/superpowers/{specs,plans}/...`）。
- 以 `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'` 提交，
  沿用前九刀风格。
- 推送到 `origin decompile/dy-sync-lib`；**不合并、不开 PR**（既定约束）。
- 收尾后更新项目记忆，标注 god-class 拆分进度：十刀完成，测试 94 → 102，
  `SyncDecisionHelper` 达 14 个纯方法。
