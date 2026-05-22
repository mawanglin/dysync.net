# 设计：抽取 AutoDistinct 优先级去重决策（ResolveDuplicateVideoAction）+ 特征化测试

日期：2026-05-22
分支：`decompile/dy-sync-lib`
关联：`docs/code-review-2026-05-18.md`（WARNING：~1365 行 god-class `job/DouyinBasicSyncJob.cs`）；
前序刀：
- `docs/superpowers/specs/2026-05-19-extract-syncjob-pure-logic-design.md`
- `docs/superpowers/specs/2026-05-19-extract-createvideoentity-mapping-design.md`
- `docs/superpowers/specs/2026-05-20-extract-pick-best-video-bitrate-design.md`
- `docs/superpowers/specs/2026-05-20-extract-buildvideofilename-design.md`
- `docs/superpowers/specs/2026-05-21-extract-buildsavefolder-candidates-design.md`
- `docs/superpowers/specs/2026-05-21-extract-cover-decision-logic-design.md`

特征化安全网纪律见 `tests/README.md`。

## 背景与目标

`DouyinBasicSyncJob.AutoDistinct`（`job/DouyinBasicSyncJob.cs:662-778`）是去重逻辑：
当数据库已存在同 `AwemeId` 的视频时，按「视频类型优先级」决定当前这次是跳过下载、
还是删旧文件后重新下载。方法体里**纯决策逻辑**与**文件/DB I/O 副作用**纠缠在一起：

- `config.AutoDistinct` 总开关、`exitVideo != null`、`File.Exists(exitVideo.VideoSavePath)`
  —— I/O 编排守卫；
- `File.Exists` 为真时的一段**四层嵌套 if/else 优先级判定** —— **纯决策**：给定「当前
  视频类型 / 已存在视频类型 / 优先级配置列表」，产出「跳过」或「替换」；
- `DeleteOldViedo(exitVideo)`（删文件夹）、`douyinVideoService.DeleteById`（删 DB 记录）、
  `Log.Error` —— 副作用。

延续「最低风险、行为保持、先有特征化测试」纪律：把那段四层嵌套的优先级判定逐字搬到
`SyncDecisionHelper`，`AutoDistinct` 内保留薄壳，全部 I/O/DB 留在 job。这是本轮重构里
逻辑最绕、最值得 pin 的一段。

**显式不做**：
- **不动 BRANCH 2**（`File.Exists` 为假分支：`OnlyImgOrOnlyMp3` / `await DeleteById`）；
- **不动** `config.AutoDistinct` / `exitVideo != null` / `File.Exists` 三道守卫；
- **不动** `config.PriorityLevel` 的 `JsonConvert` 反序列化（沿用第二刀「序列化留在 wrapper」
  先例——解析是配置/外部依赖关注点，不进 helper）；
- **不动** `DeleteOldViedo`（行 780-798）、调用点 `ProcessVideoList:491`；
- **不引入新抽象**（无 `IFileSystem`、无优先级策略接口）。

## 现状分析

### `AutoDistinct` 全貌（行 662-778）

```
if (config.AutoDistinct)
{
    if (exitVideo != null)
    {
        if (File.Exists(exitVideo.VideoSavePath))      // ── BRANCH 1：本地文件存在
        {
            // 解析 priLevs（config.PriorityLevel JSON）
            // 算 maxPriority / maxPriorityType / exitVideoType
            // ── 四层嵌套优先级判定 ──（本刀抽取对象）
        }
        else                                            // ── BRANCH 2：本地文件缺失
        {
            if (exitVideo.OnlyImgOrOnlyMp3) return true;
            else await douyinVideoService.DeleteById(exitVideo.Id);
        }
    }
}
return true;
```

### Pocket —— BRANCH 1 的四层嵌套优先级判定（行 672-759）

```csharp
List<PriorityLevelDto> priLevs = new List<PriorityLevelDto>();
if (!string.IsNullOrWhiteSpace(config.PriorityLevel))
{
    priLevs = JsonConvert.DeserializeObject<List<PriorityLevelDto>>(config.PriorityLevel);
}

// 4. 处理优先级：获取「最高优先级」（Sort 越小优先级越高）
PriorityLevelDto maxPriority = null;
if (priLevs.Any())
{
    maxPriority = priLevs.OrderBy(x => x.Sort).FirstOrDefault();
}
else
{
    maxPriority = new PriorityLevelDto { Id = 1, Sort = 1, Name = "喜欢的" }; // 默认「喜欢的视频」最高
}

var maxPriorityType = (VideoTypeEnum)maxPriority.Id;
var exitVideoType = exitVideo.ViedoType;

if (VideoType == maxPriorityType)
{
    if (exitVideoType == VideoType)        return false;         // 叶① 跳过
    else                                   { DeleteOldViedo; }   // 叶② 替换（带 try/catch）
}
else
{
    if (exitVideoType == maxPriorityType)  return false;         // 叶③ 跳过
    else
    {
        var currentSort = priLevs.FirstOrDefault(x => x.Id == (int)VideoType)?.Sort ?? int.MaxValue;
        var exitSort    = priLevs.FirstOrDefault(x => x.Id == (int)exitVideoType)?.Sort ?? int.MaxValue;
        if (currentSort < exitSort)        { DeleteOldViedo; }   // 叶④ 替换（无 try/catch）
        else                               return false;         // 叶⑤ 跳过
    }
}
```

| 部分 | 纯度 |
|------|------|
| `priLevs` 的 `JsonConvert` 反序列化 | 解析/外部依赖关注点，**留在 job** |
| `maxPriority` 默认值 + `OrderBy` 选取、`(VideoTypeEnum)maxPriority.Id` 强转 | **纯**，进 helper |
| 四层嵌套 if/else（叶①–⑤）、`currentSort`/`exitSort` 的 `?? int.MaxValue` | **纯**（仅读类型与列表），进 helper |
| `return false` / `DeleteOldViedo` / `Log.Error` | 决策结果与副作用，**留在 job** |

判定的五个叶子归并为两种**决策**：叶①③⑤ = 跳过下载；叶②④ = 替换（删旧文件后继续）。

### 关键发现：两条「替换」分支的 try/catch 不对称

叶② 与叶④ 都执行 `DeleteOldViedo(exitVideo)`，但包裹方式不一致：

```
叶②（行 713-723）  try { DeleteOldViedo(exitVideo); }
                    catch (Exception ex) { Log.Error($"[{cookie.UserName}]...", ex); }   ← 有 try/catch

叶④（行 748-750）  DeleteOldViedo(exitVideo);                                            ← 无 try/catch
```

抽取后薄壳里只剩**一个** `DeleteOldViedo` 调用点（`action == ReplaceExisting` 时），
两个调用点被迫并成一个。这几乎可以肯定是原作者的疏漏（叶② 显式写了 guard，
说明本意要 guard）。

**决定（已与用户确认）——归一化**：薄壳里的 `DeleteOldViedo` **统一**纳入 try/catch。
这是本轮七刀以来**唯一一处有意行为偏差**：原叶④ 的 `DeleteOldViedo` 抛异常会外抛出
`AutoDistinct`，归一化后会被捕获并 `Log.Error`。归一化方向是「更安全」（异常被记录
而非外抛），且无歧义即原作者意图。备选方案（纯函数返回 3 值枚举、把 shell 的错误处理
差异泄漏进决策语义）被否决——它会打掉本刀「把决策抽成干净纯函数」的核心目的。

**保留怪癖（不修，逐字保留）**：`priLevs` 在 `config.PriorityLevel` 非空白但反序列化为
`null` 时（如 JSON 字面量 `"null"`）会变成 `null`，随后 `priLevs.Any()` 抛
`NullReferenceException`。这是既有行为；helper 内 `priorityLevels.Any()` 与之逐字一致，
**不加 null 守卫**，特征化测试**不构造**触发 NRE 的输入（不去 pin「抛 NRE」这种脆弱
行为，沿用第六刀对 NRE 的处理）。

### 子类与调用点

`AutoDistinct` 为 `private async Task<bool>`——`private`，**全仓零子类 override**（`private`
不可被 override）。唯一调用点 `ProcessVideoList:491`（`bool Goon = await AutoDistinct(...)`）
不动。故本刀 job 侧**只动 `DouyinBasicSyncJob.cs` 一个文件**。

`AutoDistinct` 收薄后 BRANCH 2 仍有 `await douyinVideoService.DeleteById`，故 `async`
保留；签名、可见性、参数顺序全部不变。

## 架构

### 1. 新枚举 `model/dto/DuplicateVideoAction.cs`

项目枚举集中在 `model/dto/`（现仅 `VideoTypeEnum.cs`），新枚举以同样方式落地，
`namespace dy.net.model.dto`。

```csharp
namespace dy.net.model.dto
{
    /// <summary>
    /// 去重决策结果：当 DB 已存在同 AwemeId 视频、且其本地文件也存在时，
    /// 按视频类型优先级判定本次同步应如何处理。
    /// 由 SyncDecisionHelper.ResolveDuplicateVideoAction 产出。
    /// </summary>
    public enum DuplicateVideoAction
    {
        /// <summary>跳过下载——已存在同等或更高优先级的视频。</summary>
        SkipDownload,

        /// <summary>删除旧文件后继续下载——当前类型优先级更高。</summary>
        ReplaceExisting
    }
}
```

### 2. `utils/SyncDecisionHelper.cs` 新增 `ResolveDuplicateVideoAction`

复用已有 helper（`namespace dy.net.utils`，`public static class SyncDecisionHelper`）。
追加到类末尾，紧接当前最后一个方法 `BuildCoverPosterPath` 之后、类关闭 `}` 之前。
**不动**既有 10 个方法。

`PriorityLevelDto`/`VideoTypeEnum` 同属 `dy.net.model.dto`，`DuplicateVideoAction`
新增于同命名空间；`SyncDecisionHelper` 既有方法已引用该命名空间类型。**无需新增 `using`。**

```csharp
/// <summary>
/// 从 DouyinBasicSyncJob.AutoDistinct 抽出的纯优先级去重判定（无 I/O）。
/// 行为逐字保留：priorityLevels 为空 → 默认最高优先级 {Id=1,Sort=1}（即 dy_favorite）；
/// 否则取 Sort 最小者为最高优先级。判定见方法体四层嵌套 if/else。
/// 抽象属性 VideoType 提升为 currentType 入参。
/// 注意 priorityLevels 为 null 时 .Any() 会抛 NRE（与原 priLevs.Any() 逐字一致）——
/// 既有行为，不加守卫。JsonConvert 反序列化、DeleteOldViedo/DeleteById 的 I/O 留在 job。
/// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
/// </summary>
public static DuplicateVideoAction ResolveDuplicateVideoAction(
    VideoTypeEnum currentType,
    VideoTypeEnum exitVideoType,
    List<PriorityLevelDto> priorityLevels)
{
    // 4. 处理优先级：获取「最高优先级」（Sort 越小优先级越高）
    PriorityLevelDto maxPriority = null;
    if (priorityLevels.Any())
    {
        // 前端已配置优先级：取 Sort 最小的（1最高）
        maxPriority = priorityLevels.OrderBy(x => x.Sort).FirstOrDefault();
    }
    else
    {
        // 前端未配置：使用默认优先级（喜欢 > 收藏 > 关注）
        maxPriority = new PriorityLevelDto { Id = 1, Sort = 1, Name = "喜欢的" }; // 默认「喜欢的视频」最高
    }

    // 5. 转换为当前上下文的视频类型
    var maxPriorityType = (VideoTypeEnum)maxPriority.Id; // 配置的最高优先级类型

    // 7. 优先级逻辑判断（核心）
    if (currentType == maxPriorityType)
    {
        // 情况1：当前要下载的是「最高优先级」视频
        if (exitVideoType == currentType)
        {
            // 已存在同优先级视频 → 跳过下载（避免重复）
            return DuplicateVideoAction.SkipDownload;
        }
        else
        {
            // 已存在「低优先级」视频 → 替换（删除旧文件，继续下载新的最高优先级视频）
            return DuplicateVideoAction.ReplaceExisting;
        }
    }
    else
    {
        // 情况2：当前要下载的是「非最高优先级」视频
        if (exitVideoType == maxPriorityType)
        {
            // 已存在「最高优先级」视频 → 跳过（不替换最高优先级）
            return DuplicateVideoAction.SkipDownload;
        }
        else
        {
            // 已存在「其他非最高优先级」视频 → 比较两者优先级
            var currentSort = priorityLevels.FirstOrDefault(x => x.Id == (int)currentType)?.Sort ?? int.MaxValue;
            var exitSort = priorityLevels.FirstOrDefault(x => x.Id == (int)exitVideoType)?.Sort ?? int.MaxValue;

            if (currentSort < exitSort)
            {
                // 当前类型优先级更高 → 替换旧视频
                return DuplicateVideoAction.ReplaceExisting;
            }
            else
            {
                // 当前类型优先级更低或相等 → 跳过
                return DuplicateVideoAction.SkipDownload;
            }
        }
    }
}
```

- 逐字搬移：`maxPriority` 默认值与 `OrderBy` 选取、`(VideoTypeEnum)` 强转、四层嵌套结构、
  `currentSort`/`exitSort` 的 `FirstOrDefault(...) ?? int.MaxValue`、花括号、空白原样。
- 必要替换：`VideoType` → `currentType`；`priLevs` → `priorityLevels`；
  五个叶子的 `return false` → `return DuplicateVideoAction.SkipDownload`，
  两个 fall-through（叶②④）的「`DeleteOldViedo` + 注释 +『继续执行』」替换为
  `return DuplicateVideoAction.ReplaceExisting`。
- 原代码中 `DeleteOldViedo`/`Log.Error`/`try-catch`/`exitVideo` 局部变量等副作用相关
  内容**不进** helper（它们留在薄壳）。原 `exitVideoType` 局部变量在 helper 内由
  `exitVideoType` 入参直接替代。
- 行内注释保留原 BRANCH 1 中对应行的中文注释（去掉与已删副作用相关的部分）。

### 3. `AutoDistinct` 收薄为薄壳

签名、可见性、参数顺序、XML doc（若有）不变。BRANCH 1 收薄，BRANCH 2 及所有守卫
逐字保留：

```csharp
private async Task<bool> AutoDistinct(AppConfig config, DouyinVideo exitVideo, DouyinCookie cookie)
{
    // 去重，检查视频是否已存在（按优先级下载）
    if (config.AutoDistinct)
    {
        if (exitVideo != null)
        {
            // 2. 已存在视频：先判断本地文件是否存在
            if (File.Exists(exitVideo.VideoSavePath))
            {
                List<PriorityLevelDto> priLevs = new List<PriorityLevelDto>();
                if (!string.IsNullOrWhiteSpace(config.PriorityLevel))
                {
                    priLevs = JsonConvert.DeserializeObject<List<PriorityLevelDto>>(config.PriorityLevel);
                }

                var action = SyncDecisionHelper.ResolveDuplicateVideoAction(VideoType, exitVideo.ViedoType, priLevs);
                if (action == DuplicateVideoAction.SkipDownload)
                {
                    return false;
                }

                // ReplaceExisting：删除旧的低优先级文件，继续下载（覆盖旧数据）
                try
                {
                    DeleteOldViedo(exitVideo);
                }
                catch (Exception ex)
                {
                    Log.Error($"[{cookie.UserName}][{VideoType.GetDesc()}]-删除重复的文件[{exitVideo.VideoTitle}]失败：{ex.Message}", ex);
                }
            }
            else
            {
                if (exitVideo.OnlyImgOrOnlyMp3)
                {
                    return true;//说明是图文视频，不需要再下载视频了
                }
                else
                {
                    //记录存在，但本地文件不存在，则继续下载。
                    //删除原来的记录
                    await douyinVideoService.DeleteById(exitVideo.Id);
                }
            }
        }
    }
    return true;
}
```

- I/O/DB（`config.AutoDistinct`、`exitVideo != null`、`File.Exists`、`JsonConvert`、
  `DeleteOldViedo`、`Log.Error`、`OnlyImgOrOnlyMp3`、`await DeleteById`）逐字留在 job。
- **唯一行为偏差**：`DeleteOldViedo` 现统一在 try/catch 内（原叶④ 无 guard）——见
  「现状分析」。commit message 须明确标注。
- 控制流等价（除上述偏差）：纯 pocket 是无副作用判定，搬出后 job 在同一位置得到同一
  「跳过/替换」决策，`return false` / 删旧文件 / fall-through 到 `return true` 的走向不变。
- BRANCH 1 里原本散落于五个叶子的 `Log.Debug` 注释（已注释掉的死代码）随对应叶子
  迁移而自然消失；这些是被注释的日志、非可执行代码，不影响行为。

### 4. 特征化测试 `tests/dy.net.Tests/SyncDecisionHelperTests.cs` 追加

新增一个小节 `// ---- ResolveDuplicateVideoAction ----`，紧接当前最后一节
`BuildCoverPosterPath` 之后、类关闭 `}` 之前。

**`// ---- ResolveDuplicateVideoAction ----`（8 个 `[Fact]`）**

`PriorityLevelDto.Id` 对应 `VideoTypeEnum` 的整数值（`dy_favorite=1` / `dy_collects=2`
/ `dy_follows=3`）。

| # | 用例（示意） | 输入 | 期望 |
|---|---|---|---|
| 1 | `EmptyPriorityLevels_DefaultsToFavoriteHighest_CurrentIsFavoriteSameAsExit_Skips` | `priorityLevels=[]`；`current=dy_favorite`；`exit=dy_favorite` | `SkipDownload`（叶①，默认 maxPriority Id=1） |
| 2 | `EmptyPriorityLevels_CurrentIsFavoriteExitIsCollects_Replaces` | `priorityLevels=[]`；`current=dy_favorite`；`exit=dy_collects` | `ReplaceExisting`（叶②） |
| 3 | `EmptyPriorityLevels_CurrentIsCollectsExitIsFavorite_Skips` | `priorityLevels=[]`；`current=dy_collects`；`exit=dy_favorite` | `SkipDownload`（叶③，exit 是默认最高优先级） |
| 4 | `ConfiguredLevels_CurrentIsConfiguredHighest_ExitIsLower_Replaces` | `priorityLevels=[{Id=3,Sort=1},{Id=2,Sort=2}]`；`current=dy_follows`；`exit=dy_collects` | `ReplaceExisting`（叶②，maxPriority=dy_follows） |
| 5 | `ConfiguredLevels_NeitherIsHighest_CurrentSortSmaller_Replaces` | `priorityLevels=[{Id=1,Sort=1},{Id=2,Sort=2},{Id=3,Sort=3}]`；`current=dy_collects`；`exit=dy_follows` | `ReplaceExisting`（叶④，currentSort=2 < exitSort=3） |
| 6 | `ConfiguredLevels_NeitherIsHighest_CurrentSortLarger_Skips` | `priorityLevels=[{Id=1,Sort=1},{Id=2,Sort=2},{Id=3,Sort=3}]`；`current=dy_follows`；`exit=dy_collects` | `SkipDownload`（叶⑤，currentSort=3 ≥ exitSort=2） |
| 7 | `ConfiguredLevels_NeitherIsHighest_EqualSort_Skips` | `priorityLevels=[{Id=1,Sort=1},{Id=2,Sort=5},{Id=3,Sort=5}]`；`current=dy_follows`；`exit=dy_collects` | `SkipDownload`（叶⑤，currentSort==exitSort，`<` 为假） |
| 8 | `ConfiguredLevels_CurrentTypeMissingFromList_FallsBackToMaxValueSort_Skips` | `priorityLevels=[{Id=1,Sort=1},{Id=2,Sort=2}]`；`current=dy_follows`（不在表内）；`exit=dy_collects` | `SkipDownload`（叶⑤，currentSort=int.MaxValue ≥ exitSort=2，pin `?? int.MaxValue` 回退） |

**测试构造原则**：
- 纯内存，不用 `TestDb`、不碰文件系统、不碰 DB。
- 小节内部定义助手方法（如 `Levels(params (int id, int sort)[])` 构造
  `List<PriorityLevelDto>`），与既有小节助手隔离、命名不撞（沿用前六刀做法）。
- 每个 Fact 一个不变量；断言 `Assert.Equal(expected, actual)`。
- 测试旁短注释 `// pin: current behavior, not aspirational`。
- **不写** NRE 路径测试（`priorityLevels` 为 null 时 `.Any()` 会抛——见「保留怪癖」）。
- **不测**薄壳里的 try/catch 归一化——它是 job 编排层、特征化测试不覆盖；该偏差由
  spec 与 commit message 文字记录。

### 5. 文档更新 `tests/README.md`

- 「What is pinned」`SyncDecisionHelper` 行追加 `ResolveDuplicateVideoAction`
  （优先级去重判定：空表→默认 dy_favorite 最高 / 配置表→Sort 最小者最高 /
  四层嵌套产出 SkipDownload·ReplaceExisting / 缺项回退 int.MaxValue）。
- 「What is intentionally NOT covered」`DouyinBasicSyncJob` 条目更新：`AutoDistinct`
  的**纯优先级判定**已抽出并 pinned；其 `config.AutoDistinct`/`File.Exists` 守卫、
  `JsonConvert` 反序列化、`DeleteOldViedo`/`DeleteById` I/O、BRANCH 2 仍在 job 薄壳内、
  未覆盖；`priorityLevels` 为 null 的 NRE 路径保留、不测；薄壳 `DeleteOldViedo` 的
  try/catch 归一化为本刀唯一行为偏差、不在测试覆盖内。

## 测试策略与正确性

- 行为保持型重构（除「现状分析」中明示的 try/catch 归一化）：成功判据 = 构建 0 错误
  （`dotnet build` 含 `dy.net` Web 项目）且 `dotnet test` 全绿（现 74 个 + 新增 8 个 = 82）。
- helper 是纯结构搬移，逻辑零改动；首次运行得到的即「当前行为」，锁死该值。
- job 薄壳仅把无副作用判定外移；唯一偏差（`DeleteOldViedo` 统一 try/catch）落在
  无测试覆盖的编排层，由 spec + commit message 文字记录。
- 若搬移过程中行为另有变化，必须在同一提交内更新对应 golden 值并在 commit message
  注明原因（沿用 `tests/README.md` 的 refactor-safety 规则）。
- 子类不动：`AutoDistinct` 为 `private`、不可 override，只改 `DouyinBasicSyncJob.cs`。

## 验证与收尾

- 构建/测试统一用 `DOTNET_ROLL_FORWARD=LatestMajor`（本机 SDK 10，项目 target net8.0）。
- 显式 `git add <path>` 仅暂存目标文件（不用 `git add -A`）。涉及文件：
  `model/dto/DuplicateVideoAction.cs`、`utils/SyncDecisionHelper.cs`、
  `job/DouyinBasicSyncJob.cs`、`tests/dy.net.Tests/SyncDecisionHelperTests.cs`、
  `tests/README.md`、实现计划与本 spec（`docs/superpowers/{specs,plans}/...`）。
- 以 `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'` 提交，
  沿用前六刀风格。
- 推送到 `origin decompile/dy-sync-lib`；**不合并、不开 PR**（既定约束）。
- 收尾后更新项目记忆，标注 god-class 拆分进度：七刀完成，测试 74 → 82。
