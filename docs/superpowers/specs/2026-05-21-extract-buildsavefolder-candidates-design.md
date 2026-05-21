# 设计：抽取 CreateSaveFolder 基类纯路径逻辑 + 特征化测试

日期：2026-05-21
分支：`decompile/dy-sync-lib`
关联：`docs/code-review-2026-05-18.md`（WARNING：~1420 行 god-class `job/DouyinBasicSyncJob.cs`）；
前序刀：
- `docs/superpowers/specs/2026-05-19-extract-syncjob-pure-logic-design.md`
- `docs/superpowers/specs/2026-05-19-extract-createvideoentity-mapping-design.md`
- `docs/superpowers/specs/2026-05-20-extract-pick-best-video-bitrate-design.md`
- `docs/superpowers/specs/2026-05-20-extract-buildvideofilename-design.md`

特征化安全网纪律见 `tests/README.md`。

## 背景与目标

`CreateSaveFolder`（`job/DouyinBasicSyncJob.cs:203-219`）是 `protected virtual string`
基类方法：把 `Aweme.Desc`/`AwemeId` 经 `DouyinFileNameHelper.SanitizeLinuxFileName`
清洗成子目录名，拼到 `cookie.SavePath` 下；若目录不存在则 `Directory.CreateDirectory`
并返回该路径；若目录已存在（视频标题撞名），返回带 `_{AwemeId}` 后缀的"消歧路径"。

该方法**混了纯逻辑与 I/O**：路径**构造**（Sanitize + Path.Combine）是纯的，
而 `Directory.Exists` / `Directory.CreateDirectory` 是文件系统副作用。本刀只抽
**纯路径构造**部分，I/O 编排留在 job 内。

延续「最低风险、行为保持、先有特征化测试」纪律：把两条候选路径的构造逐字搬到
`SyncDecisionHelper.BuildVideoSaveFolderCandidates`，返回 `(primary, collisionResolved)`
元组；job 内 `CreateSaveFolder` 保留 `protected virtual` 薄壳，只做 `Directory.Exists`
判断 + `CreateDirectory` + 选返回哪条路径。

**显式不做**：
- **不动 6 个子类 override**（见下"现状分析"）；
- **不动 3 个调用点**（`ProcessSingleVideo:836`、`ProcessDynamicVideo:895`、
  `ProcessImageSetAndMergeToVideo:1041`）；
- **不引入 `IFileSystem` 抽象**——`Directory.*` 仍直接调，I/O 不进 helper、不进测试；
- **不修复**"消歧分支返回的路径未被 `CreateDirectory`"这一既有行为（基类 else 分支
  只算路径不建目录——逐字保留，不"顺手修"）；
- 不补 `item` / `cookie` 的 null 守卫，不重写命名风格。

## 现状分析

### 基类方法体（`job/DouyinBasicSyncJob.cs:203-219`）

```csharp
protected virtual string CreateSaveFolder(DouyinCookie cookie, Aweme item, AppConfig config, DouyinFollowed followed, DouyinCollectCate cate)
{
    var subFolder = DouyinFileNameHelper.SanitizeLinuxFileName(item.Desc, item.AwemeId, true);
    var folder = Path.Combine(cookie.SavePath, subFolder);
    if (!Directory.Exists(folder))
    {
        Directory.CreateDirectory(folder);
    }
    else
    {
        //说明文件夹存在，检查里面有没有文件，如果已经有视频文件了，说明视频标题相同，那么应该重新创建文件夹,+id

        folder = Path.Combine(cookie.SavePath, subFolder + "_" + item.AwemeId);
    }
    return folder;
}
```

| 部分 | 纯度 |
|------|------|
| `SanitizeLinuxFileName(item.Desc, item.AwemeId, true)` | 纯（无 I/O、无字段写入——已 grep `utils/DouyinFileNameHelper.cs` 确认仅字符串/正则运算） |
| `Path.Combine(cookie.SavePath, subFolder)` —— primary 候选 | 纯 |
| `Path.Combine(cookie.SavePath, subFolder + "_" + item.AwemeId)` —— collisionResolved 候选 | 纯 |
| `Directory.Exists` / `Directory.CreateDirectory` | **I/O 副作用，留在 job** |

**形式参数情况**：`config`、`followed`、`cate` 在基类体中**未被引用**，仅为
virtual 契约的一部分以兼容 override。本刀**不改 virtual 签名**，故这三个参数原样保留。

**既有行为「不修」清单**（行为保持纪律——逐字保留，不改）：
- else 分支只算 `collisionResolved` 路径、**不** `CreateDirectory`，直接返回——
  即"撞名时返回一个可能不存在的目录"。本刀保留此行为。
- `subFolder + "_" + item.AwemeId` 是把后缀拼在 sanitize **之后**——若 `subFolder`
  已被截断到 100 字节上限，加后缀会超限。保留原样。

### 6 个子类 override（全部零改动）

| 子类 | 是否调 `base.CreateSaveFolder` | 说明 |
|------|------|------|
| `DouyinCollectCustomSyncJob:17` | `cate == null` 时调 base，否则走 cate.SaveFolder 路径 | 受 base→helper 间接影响，**行为等价** |
| `DouyinMixSyncJob:28` | `cate == null` 时调 base，否则走 `cookie.MixPath`/分类路径 | 同上 |
| `DouyinSeriesSyncJob:27` | `cate == null` 时调 base，否则走 `cookie.SeriesPath`/分类路径 | 同上 |
| `DouyinCollectSyncJob:41` | **不调 base**，完全独立（按作者分目录 + `GetDouyinUpSavePath`） | 与本刀完全隔离 |
| `DouyinFavoritSyncJob:46` | **不调 base**，完全独立（`cookie.FavSavePath` + 作者文件夹） | 与本刀完全隔离 |
| `DouyinFollowedSyncJob:46` | **不调 base**，完全独立（`cookie.UpSavePath` + `GetUperLastViedoFileName`） | 与本刀完全隔离 |

3 个调用 base 的 override 仍走 `base.CreateSaveFolder(...)` → 新薄壳 → helper，
路径构造逐字不变、I/O 编排逐字不变，故 **base 体的行为等价即保证这 3 个 override
的 `cate==null` 分支行为等价**。3 个独立 override 与本刀无交集。**6 个 override
文件本刀一行不改**，由 grep + diff 双重确认。

### 3 个调用点（全部不动）

- `ProcessSingleVideo:836` — `var saveFolder = CreateSaveFolder(cookie, item, config, followed, cate);`
- `ProcessDynamicVideo:895` — `var saveFolder = CreateSaveFolder(cookie, item, config, followed, cate);`
- `ProcessImageSetAndMergeToVideo:1041` — `fileNamefolder = CreateSaveFolder(cookie, item, config, followed, cate);`

三处**不动**，仍走 virtual 派发。

## 架构

### 1. `utils/SyncDecisionHelper.cs` 新增 `BuildVideoSaveFolderCandidates`

复用已有 helper（`namespace dy.net.utils`，`public static class SyncDecisionHelper`）。
追加方法到类末尾，紧接当前最后一个方法 `BuildVideoFileName` 之后、类关闭 `}` 之前。
**不动**既有 7 个方法。

`Path` / `Directory` 属 `System.IO`——`dy.net.csproj` 已 `<ImplicitUsings>enable</ImplicitUsings>`
（net8.0），`System.IO` 在全局 using 内，**无需新增 `using`**。`DouyinFileNameHelper`
与 `SyncDecisionHelper` 同处 `dy.net.utils` 命名空间，亦无需 using。

```csharp
/// <summary>
/// 从 DouyinBasicSyncJob.CreateSaveFolder 抽出的纯路径构造逻辑（无 I/O）。
/// 行为逐字保留：把 item.Desc/AwemeId 经 SanitizeLinuxFileName 清洗为子目录名，
/// 返回两条候选路径——primary（cookie.SavePath/子目录）与 collisionResolved
/// （撞名时的 cookie.SavePath/子目录_AwemeId）。
/// 目录存在性判断与 Directory.CreateDirectory 的 I/O 编排留在 job 内。
/// 原方法 config/followed/cate 参数在 base body 中未引用，故 helper 签名不带这三项。
/// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
/// </summary>
public static (string primary, string collisionResolved) BuildVideoSaveFolderCandidates(DouyinCookie cookie, Aweme item)
{
    var subFolder = DouyinFileNameHelper.SanitizeLinuxFileName(item.Desc, item.AwemeId, true);
    return (
        Path.Combine(cookie.SavePath, subFolder),
        Path.Combine(cookie.SavePath, subFolder + "_" + item.AwemeId)
    );
}
```

- `subFolder` 计算与两次 `Path.Combine` 逐字搬入；表达式、`true` 实参、`"_"`
  字面量逐字保留。
- **关于"提前求值"**：原方法只在 else 分支算 `collisionResolved`，helper 改为
  **两条候选一并求值**。`SanitizeLinuxFileName` 与 `Path.Combine` 均为纯函数
  （已逐行 grep 确认无 I/O、无静态状态写入、确定性），提前求值**不产生任何
  可观察差异**——返回值、异常、副作用全等价。此为本刀唯一的结构性偏差，
  在此显式记录。

### 2. `DouyinBasicSyncJob.CreateSaveFolder` 改为薄壳（保留 I/O 编排）

签名/可见性/参数顺序/XML doc 注释保持不变。方法体替换为：

```csharp
protected virtual string CreateSaveFolder(DouyinCookie cookie, Aweme item, AppConfig config, DouyinFollowed followed, DouyinCollectCate cate)
{
    var (primary, collisionResolved) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(cookie, item);
    if (!Directory.Exists(primary))
    {
        Directory.CreateDirectory(primary);
        return primary;
    }
    //说明文件夹存在，检查里面有没有文件，如果已经有视频文件了，说明视频标题相同，那么应该重新创建文件夹,+id
    return collisionResolved;
}
```

- I/O（`Directory.Exists` / `Directory.CreateDirectory`）**留在 job**，逐字保留。
- 控制流等价：原 `if(!Exists){Create;}else{folder=...;}return folder;`
  改写为 `if(!Exists){Create;return primary;} return collisionResolved;`——
  两种写法对同一输入产出同一返回值、同一 `CreateDirectory` 调用，行为等价。
- 撞名注释逐字保留。
- `config`/`followed`/`cate` 参数仍在 virtual 签名上（virtual 契约不变）；
  base impl 不引用它们，与原方法一致。
- 6 个子类 override **零改动**。

### 3. 特征化测试 `tests/dy.net.Tests/SyncDecisionHelperTests.cs` 追加

新增 `// ---- BuildVideoSaveFolderCandidates ----` 小节，包含 5 个 `[Fact]`。
helper 无 I/O，测试**纯内存**、不碰文件系统、不用 `TestDb`。

| # | 用例（示意） | 输入 | 期望 |
|---|---|---|---|
| 1 | `Primary_CombinesSavePathWithSanitizedDesc` | `cookie.SavePath="/data"`；`item.Desc="我的视频"`；`item.AwemeId="123"` | `primary == Path.Combine("/data", "我的视频")` |
| 2 | `CollisionResolved_AppendsUnderscoreAwemeId` | 同上 | `collisionResolved == Path.Combine("/data", "我的视频" + "_" + "123")` |
| 3 | `BlankDesc_FallsBackToAwemeIdAsSubFolder` | `item.Desc=""`（或空白）；`item.AwemeId="123"` | `primary == Path.Combine("/data", "123")`；`collisionResolved == Path.Combine("/data", "123_123")`（验证 Sanitize 空值兜底链路） |
| 4 | `IllegalChars_SanitizedIntoSubFolder` | `item.Desc="a/b:c"`；`item.AwemeId="123"` | `primary` / `collisionResolved` 用 `SanitizeLinuxFileName("a/b:c","123",true)` 的实际产出锁定（首跑取值，pin 现状） |
| 5 | `BothCandidates_ShareSameSanitizedSubFolder` | 任意 `Desc`/`AwemeId` | `collisionResolved` 恰为 `primary + "_" + item.AwemeId` 的等价拼接——pin "两候选共享同一 subFolder 词根、仅后缀不同"这一不变量 |

**测试构造原则**：
- 期望值用 `Path.Combine` + `DouyinFileNameHelper.SanitizeLinuxFileName` 现场计算
  （而非硬编码字符串），以避免把跨平台分隔符/Sanitize 细节写死；本刀 pin 的是
  "helper 与这两个纯函数的组合关系"，不是某个平台上的字面路径。
- 助手方法在小节内部定义（`Aweme(desc, awemeId)` / `Cookie(savePath)` 等），
  与 `BuildVideoFileName`/`PickBestVideoBitRate` 等小节的助手隔离。
- 每个 Fact 一个不变量；断言用 `Assert.Equal(expected, actual)`。
- 测试旁短注释 `// pin: current behavior, not aspirational`（沿用前刀风格）。

**不写**：`Directory.Exists`/`CreateDirectory` 的 I/O 路径——副作用留在 job 薄壳，
不在 helper 测试范围；薄壳的 I/O 编排由"控制流等价"论证 + 构建/既有测试守护。

### 4. 文档更新 `tests/README.md`

- 「What is pinned」`SyncDecisionHelper` 行追加 `BuildVideoSaveFolderCandidates`
  （primary = SavePath/sanitized-subFolder；collisionResolved = 同根 + `_{AwemeId}`；
   空 Desc 走 AwemeId 兜底；非法字符经 Sanitize）。
- 「What is intentionally NOT covered」`DouyinBasicSyncJob` 条目更新：
  `CreateSaveFolder` 的**纯路径构造**已抽出并 pinned；其 `Directory.Exists`/
  `CreateDirectory` **I/O 编排**仍在 job 薄壳内、未覆盖（行为保持的接缝边界）；
  6 个子类 `CreateSaveFolder` override 仍是子类业务实现，未覆盖；
  剩余 `AutoDistinct`、编排/HTTP/FS/DB 仍待后续接缝抽取。

## 测试策略与正确性

- 行为保持型重构：成功判据 = 构建 0 错误（`dotnet build` 含 `dy.net` Web 项目）且
  `dotnet test` 全绿（现 60 个 + 新增 5 个 `BuildVideoSaveFolderCandidates` 用例）。
- helper 是纯结构搬移，逻辑零改动；唯一偏差（提前求值两条候选）已论证为
  可观察行为等价。首次运行得到的即「当前行为」，锁死并在测试旁注明 pin 现状。
- job 薄壳的 I/O 编排由"控制流等价"论证守护：原 `if/else + 单 return` 与新
  `if{...return} + return` 对同一输入产出同一返回值与同一 `CreateDirectory` 调用。
- 若搬移过程中行为确有变化，必须在同一提交内更新对应 golden 值并在 commit message
  注明原因（沿用 `tests/README.md` 的 refactor-safety 规则）。
- 子类不动：6 个 `CreateSaveFolder` override 必须零改动，由 grep + diff 双重确认。

## 验证与收尾

- 构建/测试统一用 `DOTNET_ROLL_FORWARD=LatestMajor`（本机 SDK 10，项目 target net8.0）。
- 显式 `git add <path>` 仅暂存目标文件（不用 `git add -A`，避免 CRLF 抖动）。
  涉及文件：`utils/SyncDecisionHelper.cs`、`job/DouyinBasicSyncJob.cs`、
  `tests/dy.net.Tests/SyncDecisionHelperTests.cs`、`tests/README.md`、
  实现计划与本 spec（`docs/superpowers/{specs,plans}/...`）。
- 以 `git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com'` 提交，
  沿用前四刀的提交作者风格。
- 推送到 `origin decompile/dy-sync-lib`；**不合并、不开 PR**（既定约束）。
- 收尾后更新项目记忆，标注本 WARNING 子项进展（god-class 拆分进度：五刀完成，
  测试 60 → 65）。
