# 设计：抽取头像 URL 选取逻辑（PickAuthorAvatarUrl）+ 特征化测试

日期：2026-05-22
分支：`decompile/dy-sync-lib`
关联：`docs/code-review-2026-05-18.md`（WARNING：~1297 行 god-class `job/DouyinBasicSyncJob.cs`）；
前序刀：
- `docs/superpowers/specs/2026-05-19-extract-syncjob-pure-logic-design.md`
- `docs/superpowers/specs/2026-05-19-extract-createvideoentity-mapping-design.md`
- `docs/superpowers/specs/2026-05-20-extract-pick-best-video-bitrate-design.md`
- `docs/superpowers/specs/2026-05-20-extract-buildvideofilename-design.md`
- `docs/superpowers/specs/2026-05-21-extract-buildsavefolder-candidates-design.md`
- `docs/superpowers/specs/2026-05-21-extract-cover-decision-logic-design.md`
- `docs/superpowers/specs/2026-05-22-extract-autodistinct-priority-decision-design.md`

特征化安全网纪律见 `tests/README.md`。

## 背景与目标

`DouyinBasicSyncJob.DownAuthorAvatar`（`job/DouyinBasicSyncJob.cs:1121-1140`）下载作者头像，
方法体里混了**纯决策逻辑**与 **HTTP/FS I/O**：

- `config.CloseNfo` / `item.Author == null` / `string.IsNullOrWhiteSpace(avatarUrl)` 守卫
  —— I/O 编排前置门；
- 一行**头像 URL 选取**：高清 `AvatarLarger` 优先、回落 `AvatarThumb`，各取 `UrlList`
  首个 —— **纯决策**；
- `GetAuthorAvatarBasePath(cookie)`（抽象方法）、`Path.Combine`、`Path.GetDirectoryName`、
  `Directory.Exists`/`CreateDirectory`、`File.Exists`、`DownloadAsync` —— I/O。

延续「最低风险、行为保持、先有特征化测试」纪律：把那一行 URL 选取逐字搬到
`SyncDecisionHelper.PickAuthorAvatarUrl`，`DownAuthorAvatar` 内保留薄壳，全部 I/O 留在 job。
这是与第六刀 `PickCoverUrl` 对称的「头像决策」微刀，收齐 `Down*` 家族最后一个纯逻辑接缝。

**显式不做**：
- **不抽** `BuildAuthorAvatarPath` —— 路径派生只是一行 `Path.Combine(基目录, $"{Uid}.jpg")`，
  且基目录来自抽象方法 `GetAuthorAvatarBasePath`，天然属 job 侧；单独抽太薄，YAGNI；
- **不动** 3 个调用点（`ProcessSingleVideo:799`、`ProcessDynamicVideo:865`、
  `ProcessImageSetAndMergeToVideo:1038`）；
- **不引入新抽象**（无 `IFileSystem`、无头像策略接口）；
- **不修既有怪行为**（见下"现状分析"的 quirk）。

## 现状分析

### `DownAuthorAvatar` 全貌（行 1121-1140）

```csharp
protected async Task<string> DownAuthorAvatar(DouyinCookie cookie, Aweme item,AppConfig config)
{
    if (config.CloseNfo) return string.Empty;
    if (item.Author == null) return string.Empty;
    // 优先获取高清头像
    var avatarUrl = item.Author.AvatarLarger?.UrlList?.FirstOrDefault() ?? item.Author.AvatarThumb?.UrlList?.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(avatarUrl)) return string.Empty;

    // 拼接头像保存路径
    var avatarSavePath = Path.Combine(GetAuthorAvatarBasePath(cookie), $"{item.Author.Uid}.jpg");
    var avatarDir = Path.GetDirectoryName(avatarSavePath);
    // 创建头像保存文件夹
    if (!Directory.Exists(avatarDir)) Directory.CreateDirectory(avatarDir);
    // 如果头像文件不存在，则下载
    if (!File.Exists(avatarSavePath))
    {
        await douyinHttpClientService.DownloadAsync(avatarUrl, avatarSavePath, cookie.Cookies);
    }
    return avatarSavePath;
}
```

| 部分 | 纯度 |
|------|------|
| `config.CloseNfo` / `item.Author == null` 守卫 | I/O 编排前置门，**留在 job** |
| `avatarUrl` 计算（`item.Author.AvatarLarger?.... ?? ....AvatarThumb?....`） | **纯**（仅读 `Aweme`，无 I/O、无字段写入） |
| `string.IsNullOrWhiteSpace(avatarUrl)` 守卫 | I/O 编排前置门，**留在 job** |
| `avatarSavePath` 拼接（`GetAuthorAvatarBasePath` + `Path.Combine`）、`avatarDir`、`Directory.*`、`File.Exists`、`DownloadAsync` | **I/O / 抽象依赖，留在 job** |

**Quirk（不修，逐字保留）**：URL 选取行对 `item.Author` **无 `?.` 空安全**——直接
`item.Author.AvatarLarger`。这是安全的，因为上一行 `if (item.Author == null) return string.Empty;`
守卫已先跑。抽出的 `PickAuthorAvatarUrl` **逐字保留** `item.Author.`（不补 `?.`）——
调用方（job 薄壳）保留 `Author == null` 守卫，负责只在 `Author` 非 null 时调用。
若直接以 `Author == null` 调用 helper 会抛 `NullReferenceException`——这是**既有的隐式契约**，
特征化测试**不构造** `Author == null` 的输入（不去 pin "抛 NRE" 这种脆弱行为，沿用第六、
第七刀对 NRE 的处理）。

### DTO 形状

`item.Author.AvatarLarger` 与 `item.Author.AvatarThumb` 均为 `ImageInfo`
（`model/response/DouyinVideoInfoResponse.cs:363,366`），含 `UrlList`（`List<string>`）。
与第六刀 `PickCoverUrl` 用的 `Aweme.Video.Cover`（同为 `ImageInfo`）一致。

### 子类与调用点

`DownAuthorAvatar` 为 `protected async Task<string>`——**非 `virtual`**，故全仓**零子类
override**（非虚方法不可被 override；grep `job/*.cs` 亦确认仅 `DouyinBasicSyncJob.cs`
一处定义）。3 个调用点（`:799`、`:865`、`:1038`）均在 `DouyinBasicSyncJob.cs` 内、不动。
故本刀**只动 `DouyinBasicSyncJob.cs` 一个 job 文件**。

## 架构

### 1. `utils/SyncDecisionHelper.cs` 新增 `PickAuthorAvatarUrl`

复用已有 helper（`namespace dy.net.utils`，`public static class SyncDecisionHelper`）。
追加到类末尾，紧接当前最后一个方法 `ResolveDuplicateVideoAction` 之后、类关闭 `}`
之前。**不动**既有 11 个方法。

`Aweme` 属 `dy.net.model.response`，`SyncDecisionHelper` 已 `using dy.net.model.response;`；
`System.Linq`（`FirstOrDefault`）由 `<ImplicitUsings>enable</ImplicitUsings>` 覆盖。
**无需新增 `using`。无新文件、无新枚举。**

```csharp
/// <summary>
/// 从 DouyinBasicSyncJob.DownAuthorAvatar 抽出的纯头像 URL 选取逻辑（无 I/O）。
/// 行为逐字保留：优先高清 AvatarLarger，回落 AvatarThumb，各取 UrlList 首个。
/// 注意对 item.Author 无 ?. 空安全——原代码 Author==null 守卫先跑，调用方（job 薄壳）
/// 保留该守卫并负责只在 Author 非 null 时调用；逐字保留不补守卫。
/// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
/// </summary>
public static string PickAuthorAvatarUrl(Aweme item)
{
    // 优先获取高清头像
    return item.Author.AvatarLarger?.UrlList?.FirstOrDefault() ?? item.Author.AvatarThumb?.UrlList?.FirstOrDefault();
}
```

- 逐字搬移：`??` 链、`FirstOrDefault`、`?.` 全部原样。`avatarUrl` 局部变量在 job 内由
  `var avatarUrl = SyncDecisionHelper.PickAuthorAvatarUrl(item);` 替代，故 helper 直接
  `return`。
- 中文行内注释 `// 优先获取高清头像` 随逻辑迁入 helper。

### 2. `DownAuthorAvatar` 收薄为薄壳

签名、可见性、参数顺序、方法体除一行外全部不变：

```csharp
protected async Task<string> DownAuthorAvatar(DouyinCookie cookie, Aweme item,AppConfig config)
{
    if (config.CloseNfo) return string.Empty;
    if (item.Author == null) return string.Empty;
    var avatarUrl = SyncDecisionHelper.PickAuthorAvatarUrl(item);
    if (string.IsNullOrWhiteSpace(avatarUrl)) return string.Empty;

    // 拼接头像保存路径
    var avatarSavePath = Path.Combine(GetAuthorAvatarBasePath(cookie), $"{item.Author.Uid}.jpg");
    var avatarDir = Path.GetDirectoryName(avatarSavePath);
    // 创建头像保存文件夹
    if (!Directory.Exists(avatarDir)) Directory.CreateDirectory(avatarDir);
    // 如果头像文件不存在，则下载
    if (!File.Exists(avatarSavePath))
    {
        await douyinHttpClientService.DownloadAsync(avatarUrl, avatarSavePath, cookie.Cookies);
    }
    return avatarSavePath;
}
```

- 唯一改动：原 `var avatarUrl = item.Author.AvatarLarger?....` 一行 → `var avatarUrl =
  SyncDecisionHelper.PickAuthorAvatarUrl(item);`。`// 优先获取高清头像` 注释随逻辑迁入
  helper，job 内该行不再保留注释。
- I/O（两个 `return string.Empty` 守卫之间与之后）逐字保留。
- 控制流等价：纯 pocket 是无副作用取值，搬出后 job 在同一位置拿到同一 `avatarUrl`，
  后续 blank 守卫与 I/O 分支判定不变。

### 3. 特征化测试 `tests/dy.net.Tests/SyncDecisionHelperTests.cs` 追加

新增一个小节 `// ---- PickAuthorAvatarUrl ----`，紧接当前最后一节
`ResolveDuplicateVideoAction` 之后、类关闭 `}` 之前。

**`// ---- PickAuthorAvatarUrl ----`（4 个 `[Fact]`）**

| # | 用例（示意） | 输入 | 期望 |
|---|---|---|---|
| 1 | `AvatarLargerPresent_TakesLargerFirst` | `Author.AvatarLarger.UrlList=["L1","L2"]`；`AvatarThumb.UrlList=["T1"]` | `"L1"`（pin 优先 Larger + `FirstOrDefault`） |
| 2 | `AvatarLargerNull_FallsBackToThumb` | `Author.AvatarLarger=null`；`AvatarThumb.UrlList=["T1"]` | `"T1"` |
| 3 | `AvatarLargerEmptyUrlList_FallsBackToThumb` | `Author.AvatarLarger.UrlList=[]`（空）；`AvatarThumb.UrlList=["T1"]` | `"T1"`（空列表 `FirstOrDefault` → null → `??` 回落） |
| 4 | `BothNull_ReturnsNull` | `Author.AvatarLarger=null`；`AvatarThumb=null` | `null` |

**测试构造原则**：
- 纯内存，不用 `TestDb`、不碰文件系统。
- 助手方法在小节内部定义（如 `AuthorWithAvatars(ImageInfo larger, ImageInfo thumb)`
  构造带 `Author` 的 `Aweme`、`Img(params string[])` 构造 `ImageInfo`），与既有小节助手
  隔离、命名不撞（沿用前七刀做法）。注意 `Img` 等名若与既有小节助手撞名需另取名。
- 每个 Fact 一个不变量；断言用 `Assert.Equal`（`null` 用例用 `Assert.Null`）。
- 测试旁短注释 `// pin: current behavior, not aspirational`。
- **不写** `Author == null` 路径测试（会抛 NRE——见 Quirk）。

### 4. 文档更新 `tests/README.md`

- 「What is pinned」`SyncDecisionHelper` 行追加 `PickAuthorAvatarUrl`
  （头像 URL 选取：AvatarLarger 优先 → AvatarThumb 回落，各取 UrlList 首个 / 全空→null）。
- 「What is intentionally NOT covered」`DouyinBasicSyncJob` 条目更新：`DownAuthorAvatar`
  的**纯 URL 选取逻辑**已抽出并 pinned；其 `CloseNfo`/`Author`/blank 守卫、
  `GetAuthorAvatarBasePath`/`Path.Combine` 路径派生、`Directory`/`File`/`DownloadAsync`
  I/O 仍在 job 薄壳内、未覆盖；`Author == null` 的 NRE 路径保留、不测。
  并从「Still uncovered」列表移除 `DownAuthorAvatar`。

## 测试策略与正确性

- 行为保持型重构：成功判据 = 构建 0 错误（`dotnet build` 含 `dy.net` Web 项目）且
  `dotnet test` 全绿（现 82 个 + 新增 4 个 = 86）。
- helper 是纯结构搬移，逻辑零改动；首次运行得到的即「当前行为」，锁死该值。
- job 薄壳仅把无副作用取值外移，守卫与 I/O 分支判定逐字保留，控制流等价。
- 若搬移过程中行为确有变化，必须在同一提交内更新对应 golden 值并在 commit message
  注明原因（沿用 `tests/README.md` 的 refactor-safety 规则）。
- 子类不动：`DownAuthorAvatar` 非 virtual、零 override，由 grep + diff 双重确认
  只改 `DouyinBasicSyncJob.cs`。

## 验证与收尾

- 构建/测试统一用 `DOTNET_ROLL_FORWARD=LatestMajor`（本机 SDK 10，项目 target net8.0）。
- 显式 `git add <path>` 仅暂存目标文件（不用 `git add -A`）。涉及文件：
  `utils/SyncDecisionHelper.cs`、`job/DouyinBasicSyncJob.cs`、
  `tests/dy.net.Tests/SyncDecisionHelperTests.cs`、`tests/README.md`、
  实现计划与本 spec（`docs/superpowers/{specs,plans}/...`）。
- 以 `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'` 提交，
  沿用前七刀风格。
- 推送到 `origin decompile/dy-sync-lib`；**不合并、不开 PR**（既定约束）。
- 收尾后更新项目记忆，标注 god-class 拆分进度：八刀完成，测试 82 → 86。
