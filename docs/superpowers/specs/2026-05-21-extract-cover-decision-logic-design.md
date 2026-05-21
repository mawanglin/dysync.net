# 设计：抽取封面决策逻辑（PickCoverUrl + BuildCoverPosterPath）+ 特征化测试

日期：2026-05-21
分支：`decompile/dy-sync-lib`
关联：`docs/code-review-2026-05-18.md`（WARNING：~1420 行 god-class `job/DouyinBasicSyncJob.cs`）；
前序刀：
- `docs/superpowers/specs/2026-05-19-extract-syncjob-pure-logic-design.md`
- `docs/superpowers/specs/2026-05-19-extract-createvideoentity-mapping-design.md`
- `docs/superpowers/specs/2026-05-20-extract-pick-best-video-bitrate-design.md`
- `docs/superpowers/specs/2026-05-20-extract-buildvideofilename-design.md`
- `docs/superpowers/specs/2026-05-21-extract-buildsavefolder-candidates-design.md`

特征化安全网纪律见 `tests/README.md`。

## 背景与目标

`DouyinBasicSyncJob` 有两个 `DownVideoCover` 重载，各自混了**纯决策逻辑**与
**HTTP/FS I/O**：

- `DownVideoCover(Aweme item, string savePath, DouyinCookie cookie, DouyinCollectCate cate, AppConfig config)`
  （`job/DouyinBasicSyncJob.cs:1174-1199`）——按 `cate` 分支、多级 `??` 兜底从 `Aweme`
  里**选取封面 URL**，然后调用下面那个重载下载。选 URL 是纯逻辑。
- `DownVideoCover(string coverUrl, string savePath, DouyinCookie cookie, AppConfig config)`
  （`job/DouyinBasicSyncJob.cs:1297-1320`）——按 `VideoType` 派生**海报落地文件名/路径**，
  然后 `File.Exists` + `DownloadAsync`。派生路径是纯逻辑。

延续「最低风险、行为保持、先有特征化测试」纪律：把这两段纯逻辑逐字搬到
`SyncDecisionHelper`，两个 `DownVideoCover` 重载内保留薄壳，I/O 留在 job。

本刀同时抽出**两个**纯方法（封面主题闭环），因为单独的 `BuildCoverPosterPath`
太薄、不值一整套刀的流程开销，而两个 `DownVideoCover` 重载本就是一对。

**显式不做**：
- **不动 `DownAuthorAvatar`**（`job/DouyinBasicSyncJob.cs:1208`）——它的头像-URL 选取
  是另一个未来微刀；
- **不动 3 个调用点**：`DownVideoCover(Aweme,...)` 在 `ProcessSingleVideo:865`、
  `ProcessDynamicVideo:931` 调用；`DownVideoCover(string,...)` 在
  `ProcessImageSetAndMergeToVideo:1119` 直接调用；
- **不引入新抽象**（无 `IFileSystem`、无封面策略接口）；
- **不合并**两个 `DownVideoCover` 重载；
- **不修既有怪行为**（见下"现状分析"的两处 quirk）。

## 现状分析

### Pocket A —— `DownVideoCover(Aweme,...)` 的封面 URL 选取（行 1174-1199）

```csharp
protected async Task<string> DownVideoCover(Aweme item, string savePath, DouyinCookie cookie, DouyinCollectCate cate,AppConfig config)
{
    if (config.CloseNfo) return string.Empty;
    // 定义封面URL变量
    string coverUrl;

    // 按照优先级获取封面URL
    if (cate is not null)
    {
        // cate不为空时：优先MixInfo封面 → 其次Music高清封面 → 最后Video封面
        coverUrl = item.MixInfo?.CoverUrl?.UrlList?.FirstOrDefault()
                   ?? item.Video.Cover.UrlList?.LastOrDefault()
                   ?? item.Music?.CoverHd?.UrlList?.FirstOrDefault();
    }
    else
    {
        // cate为空时：只取Video封面
        coverUrl = item.Video?.Cover?.UrlList?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(coverUrl))
        {
            coverUrl = item.Images?.FirstOrDefault()?.DynamicVideo?.Cover?.UrlList?.FirstOrDefault();
        }
    }
    // 调用下载封面的方法
    return await DownVideoCover(coverUrl, savePath, cookie, config);
}
```

| 部分 | 纯度 |
|------|------|
| `if (config.CloseNfo) return string.Empty;` 守卫 | I/O 编排前置门，**留在 job** |
| `coverUrl` 计算（`string coverUrl;` 起，到 if/else 结束） | **纯**（仅读 `Aweme`/`cate`，无 I/O、无字段写入） |
| `return await DownVideoCover(coverUrl, savePath, cookie, config);` | 调用下载重载，**留在 job** |

**Quirk 1（不修，逐字保留）**：cate 分支的 `item.Video.Cover.UrlList?.LastOrDefault()`
对 `item.Video`、`item.Video.Cover` **无 `?.` 空安全**——若二者为 null 会抛
`NullReferenceException`。而非-cate 分支 `item.Video?.Cover?.UrlList?.FirstOrDefault()`
**是**全链空安全。两分支不对称。本刀**不补**这个守卫（行为保持）。特征化测试**不构造**
触发 NRE 的输入（不去 pin "抛 NRE" 这种脆弱行为）。

**Quirk 2（不修，逐字保留）**：cate 分支的注释
`// cate不为空时：优先MixInfo封面 → 其次Music高清封面 → 最后Video封面` 与代码不符——
代码实际顺序是 MixInfo → **Video** → **Music**（`?? Video ?? Music`）。注释把后两者写反了。
这是既有的**注释错误**；本刀**逐字保留**注释（不"顺手修"），但测试按**代码真实顺序**
（MixInfo → Video → Music）来 pin。

**取值细节**：cate 分支对 Video 封面用 `LastOrDefault()`，非-cate 分支用
`FirstOrDefault()`——差异是真实行为，测试分别 pin。

### Pocket B —— `DownVideoCover(string,...)` 的海报路径派生（行 1297-1320）

```csharp
private async Task<string> DownVideoCover(string coverUrl, string savePath, DouyinCookie cookie,AppConfig config)
{
    if (config.CloseNfo) return string.Empty;
    if (string.IsNullOrWhiteSpace(coverUrl)) return string.Empty;
    if (string.IsNullOrWhiteSpace(savePath)) return string.Empty;

    string directoryPath = Path.GetDirectoryName(savePath); // 获取文件所在目录，
    string newFileName = "poster.jpg";
    if (VideoType != VideoTypeEnum.dy_mix && VideoType != VideoTypeEnum.dy_series)
    {
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(savePath); // 获取无后缀的原文件名，
        newFileName = $"{fileNameWithoutExt}-poster.jpg"; // 拼接新文件名，
    }

    var coverSavePath = Path.Combine(directoryPath, newFileName);


    // 如果封面文件不存在，则下载
    if (!File.Exists(coverSavePath))
    {
        await douyinHttpClientService.DownloadAsync(coverUrl, coverSavePath, cookie.Cookies);
    }
    return coverSavePath;
}
```

| 部分 | 纯度 |
|------|------|
| 三个 `if (...) return string.Empty;` 守卫 | I/O 编排前置门，**留在 job** |
| `directoryPath` / `newFileName` / `coverSavePath` 计算（行 ~1303-1311） | **纯**（`Path.*` 派生，无 I/O） |
| `File.Exists` + `DownloadAsync` | **I/O，留在 job** |

**形式参数情况**：纯 pocket 只读 `VideoType`（抽象属性）与 `savePath`。`VideoType`
按前几刀手法提升为 `videoType` 显式入参。

**`Path.GetDirectoryName` 边界**：job 守卫已保证 `savePath` 非空白才进 helper。
`Path.GetDirectoryName` 对"无目录段"的输入可能返回 `""` 或 `null`，`Path.Combine(null, x)`
会抛 `ArgumentNullException`——这是**既有行为**（原代码同样直接 `Path.Combine(directoryPath, ...)`），
逐字保留。测试用带目录段的 `savePath`，不触发该边界。

### 子类与调用点

两个 `DownVideoCover` 重载**均非 virtual**（`protected` / `private`），**全仓零子类
override**（grep `job/*.cs` 确认）。故本刀**只动 `DouyinBasicSyncJob.cs` 一个 job 文件**，
零子类风险——比第五刀（6 个 `CreateSaveFolder` override）更干净。

3 个调用点（`:865`、`:931`、`:1119`）不动；`DownVideoCover(Aweme,...)` 内对
`DownVideoCover(string,...)` 的调用（`:1198`）不动。

## 架构

### 1. `utils/SyncDecisionHelper.cs` 新增两个方法

复用已有 helper（`namespace dy.net.utils`，`public static class SyncDecisionHelper`）。
追加到类末尾，紧接当前最后一个方法 `BuildVideoSaveFolderCandidates` 之后、类关闭 `}`
之前。**不动**既有 8 个方法。

`Path` 属 `System.IO`，`dy.net.csproj` 已 `<ImplicitUsings>enable</ImplicitUsings>`，
全局 using 覆盖；`DouyinCollectCate`/`Aweme`/`VideoTypeEnum` 等已被既有 helper 方法引用。
**无需新增 `using`。**

```csharp
/// <summary>
/// 从 DouyinBasicSyncJob.DownVideoCover(Aweme,...) 抽出的纯封面 URL 选取逻辑（无 I/O）。
/// 行为逐字保留：cate 非 null → MixInfo → Video（LastOrDefault）→ Music 三级兜底；
/// cate == null → Video（FirstOrDefault），空白则回落 Images[0].DynamicVideo.Cover。
/// 注意 cate 分支对 item.Video/Cover 无 ?. 空安全（与非-cate 分支不对称）——既有行为，
/// 逐字保留；分支内的中文注释与代码实际顺序不符，亦逐字保留不修。
/// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
/// </summary>
public static string PickCoverUrl(DouyinCollectCate cate, Aweme item)
{
    // 定义封面URL变量
    string coverUrl;

    // 按照优先级获取封面URL
    if (cate is not null)
    {
        // cate不为空时：优先MixInfo封面 → 其次Music高清封面 → 最后Video封面
        coverUrl = item.MixInfo?.CoverUrl?.UrlList?.FirstOrDefault()
                   ?? item.Video.Cover.UrlList?.LastOrDefault()
                   ?? item.Music?.CoverHd?.UrlList?.FirstOrDefault();
    }
    else
    {
        // cate为空时：只取Video封面
        coverUrl = item.Video?.Cover?.UrlList?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(coverUrl))
        {
            coverUrl = item.Images?.FirstOrDefault()?.DynamicVideo?.Cover?.UrlList?.FirstOrDefault();
        }
    }
    return coverUrl;
}

/// <summary>
/// 从 DouyinBasicSyncJob.DownVideoCover(string,...) 抽出的纯海报路径派生逻辑（无 I/O）。
/// 行为逐字保留：dy_mix/dy_series → 同目录 "poster.jpg"；其余 → "{无后缀原名}-poster.jpg"。
/// 抽象属性 VideoType 提升为 videoType 入参。File.Exists / DownloadAsync 的 I/O 留在 job。
/// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
/// </summary>
public static string BuildCoverPosterPath(VideoTypeEnum videoType, string savePath)
{
    string directoryPath = Path.GetDirectoryName(savePath); // 获取文件所在目录，
    string newFileName = "poster.jpg";
    if (videoType != VideoTypeEnum.dy_mix && videoType != VideoTypeEnum.dy_series)
    {
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(savePath); // 获取无后缀的原文件名，
        newFileName = $"{fileNameWithoutExt}-poster.jpg"; // 拼接新文件名，
    }

    var coverSavePath = Path.Combine(directoryPath, newFileName);
    return coverSavePath;
}
```

- 逐字搬移：表达式、`??` 链、`FirstOrDefault`/`LastOrDefault`、字符串插值、花括号、空白、
  中文注释全部原样。仅做必要替换：`PickCoverUrl` 无替换；`BuildCoverPosterPath` 把
  `VideoType` → `videoType`。
- `BuildCoverPosterPath` 保留原方法的局部变量 `coverSavePath`（不内联为 `return Path.Combine(...)`）
  与其后的中文行尾注释——逐字保留。

### 2. 两个 `DownVideoCover` 重载收薄为委托

签名/可见性/参数顺序/XML doc 全部不变。方法体替换为：

```csharp
protected async Task<string> DownVideoCover(Aweme item, string savePath, DouyinCookie cookie, DouyinCollectCate cate,AppConfig config)
{
    if (config.CloseNfo) return string.Empty;
    var coverUrl = SyncDecisionHelper.PickCoverUrl(cate, item);
    // 调用下载封面的方法
    return await DownVideoCover(coverUrl, savePath, cookie, config);
}
```

```csharp
private async Task<string> DownVideoCover(string coverUrl, string savePath, DouyinCookie cookie,AppConfig config)
{
    if (config.CloseNfo) return string.Empty;
    if (string.IsNullOrWhiteSpace(coverUrl)) return string.Empty;
    if (string.IsNullOrWhiteSpace(savePath)) return string.Empty;

    var coverSavePath = SyncDecisionHelper.BuildCoverPosterPath(VideoType, savePath);

    // 如果封面文件不存在，则下载
    if (!File.Exists(coverSavePath))
    {
        await douyinHttpClientService.DownloadAsync(coverUrl, coverSavePath, cookie.Cookies);
    }
    return coverSavePath;
}
```

- I/O（`CloseNfo` 守卫、两个空白守卫、内层重载调用、`File.Exists`、`DownloadAsync`）
  逐字留在 job。
- `// 调用下载封面的方法` / `// 如果封面文件不存在，则下载` 注释保留。
- 控制流等价：纯 pocket 是无副作用的取值，搬出后 job 在同一位置拿到同一 `coverUrl` /
  `coverSavePath`，后续 I/O 分支判定不变。

### 3. 特征化测试 `tests/dy.net.Tests/SyncDecisionHelperTests.cs` 追加

新增两个小节，紧接当前最后一节 `BuildVideoSaveFolderCandidates` 之后，类关闭 `}` 之前。

**`// ---- PickCoverUrl ----`（6 个 `[Fact]`）**

| # | 用例（示意） | 输入 | 期望 |
|---|---|---|---|
| 1 | `Cate_MixInfoCover_TakesMixInfoFirst` | `cate != null`；`MixInfo.CoverUrl.UrlList=["m1","m2"]` | `"m1"` |
| 2 | `Cate_NoMixInfo_TakesVideoCoverLast` | `cate != null`；`MixInfo=null`；`Video.Cover.UrlList=["v1","v2"]` | `"v2"`（pin cate 分支用 `LastOrDefault`） |
| 3 | `Cate_NoMixNoVideo_TakesMusicCoverHd` | `cate != null`；`MixInfo=null`；`Video.Cover.UrlList=null`；`Music.CoverHd.UrlList=["mu1"]` | `"mu1"` |
| 4 | `NoCate_TakesVideoCoverFirst` | `cate==null`；`Video.Cover.UrlList=["v1","v2"]` | `"v1"`（pin 非-cate 分支用 `FirstOrDefault`） |
| 5 | `NoCate_BlankVideoCover_FallsBackToImages` | `cate==null`；`Video.Cover.UrlList=[""]`；`Images[0].DynamicVideo.Cover.UrlList=["img1"]` | `"img1"` |
| 6 | `NoCate_AllNull_ReturnsNull` | `cate==null`；`Video=null`；`Images=null` | `null` |

**`// ---- BuildCoverPosterPath ----`（3 个 `[Fact]`）**

| # | 用例（示意） | 输入 | 期望 |
|---|---|---|---|
| 7 | `Mix_UsesPlainPosterJpg` | `videoType=dy_mix`；`savePath="/data/v/123.mp4"` | `Path.Combine("/data/v", "poster.jpg")` |
| 8 | `Series_UsesPlainPosterJpg` | `videoType=dy_series`；`savePath="/data/v/123.mp4"` | `Path.Combine("/data/v", "poster.jpg")` |
| 9 | `OtherType_UsesFileNamePrefixedPoster` | `videoType=dy_collects`；`savePath="/data/v/123.mp4"` | `Path.Combine("/data/v", "123-poster.jpg")` |

**测试构造原则**：
- 纯内存，不用 `TestDb`、不碰文件系统。
- 助手方法在小节内部定义（如 `AwemeWithMixCover(...)`、`AwemeWithVideoCover(...)`、
  `CoverImage(params string[])` 构造 `ImageInfo`、`Cate()` 等），与既有小节助手隔离、
  命名不撞（沿用前五刀做法）。
- 期望路径用 `Path.Combine` + `Path.GetFileNameWithoutExtension` **现场计算**，不硬编码
  跨平台分隔符（沿用第五刀做法）。
- 每个 Fact 一个不变量；断言用 `Assert.Equal`（`null` 用例用 `Assert.Null`）。
- 测试旁短注释 `// pin: current behavior, not aspirational`。
- **不写** NRE 路径测试（cate 分支 `Video`/`Cover` 为 null 会抛——见 Quirk 1）。

### 4. 文档更新 `tests/README.md`

- 「What is pinned」`SyncDecisionHelper` 行追加 `PickCoverUrl`（cate 三级兜底 MixInfo→Video(Last)→Music /
  非-cate Video(First)→Images / 全空→null）与 `BuildCoverPosterPath`（mix·series→poster.jpg /
  其余→{名}-poster.jpg）。
- 「What is intentionally NOT covered」`DouyinBasicSyncJob` 条目更新：两个 `DownVideoCover`
  重载的**纯逻辑**（封面 URL 选取 + 海报路径派生）已抽出并 pinned；其 `CloseNfo`/空白守卫、
  `File.Exists`/`DownloadAsync` I/O 编排仍在 job 薄壳内、未覆盖；`DownAuthorAvatar` 仍未覆盖；
  cate 分支 `Video`/`Cover` 无空安全的 NRE 路径与既有错误注释保留、不测。

## 测试策略与正确性

- 行为保持型重构：成功判据 = 构建 0 错误（`dotnet build` 含 `dy.net` Web 项目）且
  `dotnet test` 全绿（现 65 个 + 新增 9 个 = 74）。
- 两个 helper 都是纯结构搬移，逻辑零改动；首次运行得到的即「当前行为」，锁死该值。
- job 薄壳仅把无副作用取值外移，I/O 分支判定逐字保留，控制流等价。
- 若搬移过程中行为确有变化，必须在同一提交内更新对应 golden 值并在 commit message
  注明原因（沿用 `tests/README.md` 的 refactor-safety 规则）。
- 子类不动：两个 `DownVideoCover` 重载非 virtual、零 override，由 grep + diff 双重确认
  只改 `DouyinBasicSyncJob.cs`。

## 验证与收尾

- 构建/测试统一用 `DOTNET_ROLL_FORWARD=LatestMajor`（本机 SDK 10，项目 target net8.0）。
- 显式 `git add <path>` 仅暂存目标文件（不用 `git add -A`）。涉及文件：
  `utils/SyncDecisionHelper.cs`、`job/DouyinBasicSyncJob.cs`、
  `tests/dy.net.Tests/SyncDecisionHelperTests.cs`、`tests/README.md`、
  实现计划与本 spec（`docs/superpowers/{specs,plans}/...`）。
- 以 `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'` 提交，
  沿用前五刀风格。
- 推送到 `origin decompile/dy-sync-lib`；**不合并、不开 PR**（既定约束）。
- 收尾后更新项目记忆，标注 god-class 拆分进度：六刀完成，测试 65 → 74。
