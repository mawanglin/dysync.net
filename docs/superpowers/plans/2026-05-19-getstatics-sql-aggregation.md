# GetStatics/GetChartData SQL 聚合重写 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `DouyinVideoService.GetStatics` / `GetChartData` 从「`GetAllAsync()` 全表载入内存再 LINQ 聚合」改为数据库侧 SQL 聚合，消除随数据量线性增长的内存/传输开销，且严格保持现有可观测行为不变。

**Architecture:** 在 `DouyinVideoRepository` 新增一组只读聚合方法（`Db.Queryable<DouyinVideo>()` 服务端 `GroupBy`/`Sum`/`Count`），`DouyinVideoService` 改为调用它们组装同样的 `VideoStaticsDto` / `List<VideoChartItemDto>`。重构前先把当前**尚未被钉死**的 `Categories`/`Authors` 列表与多日分组行为补进 `VideoStatsCharacterizationTests`，使整个可观测面都在 golden-master 保护下，再做改写。

**Tech Stack:** .NET 8 / SDK 10（`DOTNET_ROLL_FORWARD=LatestMajor`）、SqlSugarCore（SQLite）、xUnit。

**安全规则（来自 `tests/README.md`）：** 重构仅当 `dotnet test` 保持全绿才算安全；若行为确属合理变化，必须在同一提交内更新对应 golden 值并在提交信息中给出一行理由。本计划目标是**零行为变化**——所有 golden 值在 Task 1 锁定后，Task 3/4 不得修改。

---

### Task 1: 扩充特征化测试，钉死 Categories / Authors / 多日 ChartData

**Files:**
- Modify: `tests/dy.net.Tests/VideoStatsCharacterizationTests.cs`

当前测试只断言标量与体积字段。重构要把分组下推到 SQL，必须先锁定 `Categories`、`Authors` 列表及 `GetChartData` 多日/排序行为，否则这些行为没有安全网。

- [ ] **Step 1: 在 `GetStatics_locks_current_aggregation` 末尾追加对 `Categories` 与 `Authors` 的快照断言（先用占位常量）**

在该测试方法已有断言之后追加：

```csharp
            // Categories：按 Tag1 分组，空 Tag1 显示为「其他」，按 Count 降序
            Assert.Equal(2, dto.Categories.Count);
            Assert.Equal("<<CAT0_NAME>>", dto.Categories[0].Name);
            Assert.Equal(0L, dto.Categories[0].Count); // 占位，待 Step 3 锁定
            Assert.Equal("<<CAT1_NAME>>", dto.Categories[1].Name);
            Assert.Equal(0L, dto.Categories[1].Count); // 占位

            // Authors：按 Author 分组，Count 降序，Icon/UperId 取分组内最后一行
            Assert.Equal(2, dto.Authors.Count);
            Assert.Equal("<<AUTH0_NAME>>", dto.Authors[0].Name);
            Assert.Equal(0L, dto.Authors[0].Count); // 占位
            Assert.Equal("<<AUTH0_UPERID>>", dto.Authors[0].UperId);
            Assert.Equal("<<AUTH1_NAME>>", dto.Authors[1].Name);
            Assert.Equal(0L, dto.Authors[1].Count); // 占位
```

- [ ] **Step 2: 新增多日 `GetChartData` 特征化测试方法（占位常量）**

在类内追加：

```csharp
        [Fact]
        public async System.Threading.Tasks.Task GetChartData_multi_day_locks_grouping_and_order()
        {
            using var t = new TestDb();
            var d1 = System.DateTime.Now.AddDays(-1);
            var d3 = System.DateTime.Now.AddDays(-3);
            await t.Db.Insertable(new System.Collections.Generic.List<DouyinVideo>
            {
                V("11","authorA","作者甲",1*GB,VideoTypeEnum.dy_collects,"搞笑",0,"h1",d1),
                V("12","authorA","作者甲",1*GB,VideoTypeEnum.dy_favorite,"搞笑",0,"",  d3),
                V("13","authorB","作者乙",1*GB,VideoTypeEnum.dy_follows, "",   1,"h3",d3),
            }).ExecuteCommandAsync();

            var chart = await MakeService(t).GetChartData(7);

            Assert.Equal(2, chart.Count);
            // 锁定当前分组顺序与各组计数（Step 3 填入观测值）
            Assert.Equal("<<MD_DATES>>", string.Join(",", chart.Select(c => c.Date)));
            Assert.Equal("<<MD_COLLECT>>", string.Join(",", chart.Select(c => c.Collect)));
            Assert.Equal("<<MD_FAVORITE>>", string.Join(",", chart.Select(c => c.Favorite)));
            Assert.Equal("<<MD_FOLLOW>>", string.Join(",", chart.Select(c => c.Follow)));
            Assert.Equal("<<MD_GRAPHIC>>", string.Join(",", chart.Select(c => c.Graphic)));
        }
```

- [ ] **Step 3: 运行测试，捕获实际值并替换全部 `<<...>>` 占位**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter VideoStatsCharacterizationTests`
Expected: FAIL，输出每个断言的 `Actual:`。把每个 `<<...>>` 占位（及对应的 `0L` 占位 Count）替换为输出中的精确实际值，捕获当前行为。

- [ ] **Step 4: 重新运行，确认全绿**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj --filter VideoStatsCharacterizationTests`
Expected: PASS（3 个测试方法全通过）。当前 Categories/Authors/多日 ChartData 行为已全部钉死。

- [ ] **Step 5: 提交**

```bash
git add tests/dy.net.Tests/VideoStatsCharacterizationTests.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "test: widen GetStatics/GetChartData golden master (Categories/Authors/multi-day)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: 在 DouyinVideoRepository 新增 SQL 聚合方法

**Files:**
- Modify: `repository/DouyinVideoRepository.cs`（在类内、`GetByIds` 之前追加方法；保持现有 `using`）

仅做服务端聚合查询，不改动任何调用方；本任务结束时行为面不变（方法尚未被调用），用全量测试确认未破坏编译/现有行为。

- [ ] **Step 1: 追加标量计数/求和聚合方法**

在 `DouyinVideoRepository` 类内追加（`VideoTypeEnum`、`DouyinVideo` 已在命名空间可见）：

```csharp
        /// <summary>GetStatics 标量聚合：一次查询取回全部计数与按类型的字节求和。</summary>
        public async Task<VideoStaticsScalar> GetStaticsScalarAsync()
        {
            var q = Db.Queryable<DouyinVideo>();
            return new VideoStaticsScalar
            {
                VideoCount      = await q.CountAsync(),
                AuthorCount     = await Db.Queryable<DouyinVideo>().Select(x => x.AuthorId).Distinct().CountAsync(),
                CategoryCount   = await Db.Queryable<DouyinVideo>().Select(x => x.Tag1).Distinct().CountAsync(),
                FavoriteCount   = await Db.Queryable<DouyinVideo>().Where(x => x.ViedoType == VideoTypeEnum.dy_favorite).CountAsync(),
                CollectCount    = await Db.Queryable<DouyinVideo>().Where(x => x.ViedoType == VideoTypeEnum.dy_collects || x.ViedoType == VideoTypeEnum.dy_custom_collect).CountAsync(),
                FollowCount     = await Db.Queryable<DouyinVideo>().Where(x => x.ViedoType == VideoTypeEnum.dy_follows).CountAsync(),
                MixCount        = await Db.Queryable<DouyinVideo>().Where(x => x.ViedoType == VideoTypeEnum.dy_mix).CountAsync(),
                SeriesCount     = await Db.Queryable<DouyinVideo>().Where(x => x.ViedoType == VideoTypeEnum.dy_series).CountAsync(),
                GraphicVideoCount = await Db.Queryable<DouyinVideo>().Where(x => x.IsMergeVideo == 1).CountAsync(),
                TotalSize       = await Db.Queryable<DouyinVideo>().SumAsync(x => x.FileSize),
                FavoriteSize    = await Db.Queryable<DouyinVideo>().Where(x => x.ViedoType == VideoTypeEnum.dy_favorite).SumAsync(x => x.FileSize),
                CollectSize     = await Db.Queryable<DouyinVideo>().Where(x => x.ViedoType == VideoTypeEnum.dy_collects || x.ViedoType == VideoTypeEnum.dy_custom_collect).SumAsync(x => x.FileSize),
                FollowSize      = await Db.Queryable<DouyinVideo>().Where(x => x.ViedoType == VideoTypeEnum.dy_follows).SumAsync(x => x.FileSize),
                MixSize         = await Db.Queryable<DouyinVideo>().Where(x => x.ViedoType == VideoTypeEnum.dy_mix).SumAsync(x => x.FileSize),
                SeriesSize      = await Db.Queryable<DouyinVideo>().Where(x => x.ViedoType == VideoTypeEnum.dy_series).SumAsync(x => x.FileSize),
                GraphicSize     = await Db.Queryable<DouyinVideo>().Where(x => x.IsMergeVideo == 1).SumAsync(x => x.FileSize),
            };
        }

        /// <summary>Categories：仅取 Tag1 投影，分组在内存完成（与原 LINQ 语义完全一致），但不再载入大字段。</summary>
        public async Task<List<string>> GetTag1ProjectionAsync()
            => await Db.Queryable<DouyinVideo>().Select(x => x.Tag1).ToListAsync();

        /// <summary>Authors：仅取分组所需 4 列投影，保持插入顺序（与原 GetAllAsync().GroupBy 一致）。</summary>
        public async Task<List<AuthorProjection>> GetAuthorProjectionAsync()
            => await Db.Queryable<DouyinVideo>()
                       .Select(x => new AuthorProjection { Author = x.Author, AuthorAvatarUrl = x.AuthorAvatarUrl, AuthorId = x.AuthorId, DyUserId = x.DyUserId })
                       .ToListAsync();

        /// <summary>GetChartData：仅取 SyncTime/类型/FileHash 投影，时间过滤下推到 SQL。</summary>
        public async Task<List<ChartProjection>> GetChartProjectionAsync(System.DateTime after)
            => await Db.Queryable<DouyinVideo>()
                       .Where(x => x.SyncTime > after)
                       .Select(x => new ChartProjection { SyncTime = x.SyncTime, ViedoType = x.ViedoType, FileHash = x.FileHash })
                       .ToListAsync();
```

> 说明：`Categories`/`Authors`/`ChartData` 的分组与「最后一行」「其他」「降序」语义保留在 service 的内存 LINQ 中（与原实现逐字一致），仅把**全表大字段载入**换成**窄投影**；标量计数/求和则完全下推 SQL。这样既消除主要内存/IO 开销，又保证未被 SQL 语义改写的部分行为零变化。

- [ ] **Step 2: 新增投影/标量 DTO 文件**

Create: `model/dto/VideoStaticsScalar.cs`

```csharp
namespace dy.net.model.dto
{
    /// <summary>GetStatics 的服务端标量聚合结果。</summary>
    public class VideoStaticsScalar
    {
        public int VideoCount { get; set; }
        public int AuthorCount { get; set; }
        public int CategoryCount { get; set; }
        public int FavoriteCount { get; set; }
        public int CollectCount { get; set; }
        public int FollowCount { get; set; }
        public int MixCount { get; set; }
        public int SeriesCount { get; set; }
        public int GraphicVideoCount { get; set; }
        public long TotalSize { get; set; }
        public long FavoriteSize { get; set; }
        public long CollectSize { get; set; }
        public long FollowSize { get; set; }
        public long MixSize { get; set; }
        public long SeriesSize { get; set; }
        public long GraphicSize { get; set; }
    }

    public class AuthorProjection
    {
        public string Author { get; set; }
        public string AuthorAvatarUrl { get; set; }
        public string AuthorId { get; set; }
        public string DyUserId { get; set; }
    }

    public class ChartProjection
    {
        public System.DateTime SyncTime { get; set; }
        public VideoTypeEnum ViedoType { get; set; }
        public string FileHash { get; set; }
    }
}
```

- [ ] **Step 3: 在 `DouyinVideoRepository.cs` 顶部确认引用 `dy.net.model.dto`**

若文件顶部 `using` 未含 `using dy.net.model.dto;` 则添加（`VideoTypeEnum` 等已用，通常已存在；不存在时补上）。

- [ ] **Step 4: 构建主项目，确认 0 错误**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj -c Debug`
Expected: `生成成功` / 0 Error（警告不计）。

- [ ] **Step 5: 跑全量测试，确认现有行为未破坏**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: PASS（仍为 Task 1 后的全部测试数全绿；新方法尚未被调用，行为不变）。

- [ ] **Step 6: 提交**

```bash
git add repository/DouyinVideoRepository.cs model/dto/VideoStaticsScalar.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "perf(repo): add SQL-side aggregation + narrow projections for video stats

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: 重写 `GetStatics` 使用 SQL 聚合 + 窄投影

**Files:**
- Modify: `service/DouyinVideoService.cs:106-173`（`GetStatics` 方法体）

逐字保留 `<0.01` 替换、`其他` 改名、Categories/Authors 的 LINQ 分组与降序及「最后一行」取值，仅把数据来源从 `GetAllAsync()` 换成 Task 2 的聚合/投影。

- [ ] **Step 1: 用以下实现替换整个 `GetStatics` 方法体**

```csharp
        public async Task<VideoStaticsDto> GetStatics()
        {
            var s = await this._dyCollectVideoRepository.GetStaticsScalarAsync();
            if (s.VideoCount == 0)
                return new VideoStaticsDto();

            var tag1List = await this._dyCollectVideoRepository.GetTag1ProjectionAsync();
            var Categories = tag1List.GroupBy(x => x).Select(x => new VideoStaticsItemDto { Name = x.Key, Count = x.LongCount() }).OrderByDescending(p => p.Count).ToList();
            Categories.Where(x => string.IsNullOrWhiteSpace(x.Name)).ToList().ForEach(x => x.Name = "其他");

            var data = new VideoStaticsDto
            {
                AuthorCount = s.AuthorCount,
                CategoryCount = s.CategoryCount,
                VideoCount = s.VideoCount,
                Categories = Categories,
                FavoriteCount = s.FavoriteCount,
                CollectCount = s.CollectCount,
                FollowCount = s.FollowCount,
                GraphicVideoCount = s.GraphicVideoCount,
                MixCount = s.MixCount,
                SeriesCount = s.SeriesCount,
                VideoSizeTotal = DouyinFileUtils.ConvertBytesToGb(s.TotalSize),
                VideoFavoriteSize = DouyinFileUtils.ConvertBytesToGb(s.FavoriteSize),
                VideoCollectSize = DouyinFileUtils.ConvertBytesToGb(s.CollectSize),
                VideoFollowSize = DouyinFileUtils.ConvertBytesToGb(s.FollowSize),
                VideoMixSize = DouyinFileUtils.ConvertBytesToGb(s.MixSize),
                VideoSeriesSize = DouyinFileUtils.ConvertBytesToGb(s.SeriesSize),
                GraphicVideoSize = DouyinFileUtils.ConvertBytesToGb(s.GraphicSize),
            };
            if (data.GraphicVideoSize == "0.00")
            {
                if (s.GraphicSize > 0)
                {
                    data.GraphicVideoSize = "<0.01";
                }
            }
            if (data.VideoFavoriteSize == "0.00")
            {
                data.VideoFavoriteSize = "<0.01";
            }
            if (data.VideoCollectSize == "0.00")
            {
                data.VideoCollectSize = "<0.01";
            }
            if (data.VideoFollowSize == "0.00")
            {
                data.VideoFollowSize = "<0.01";
            }
            if (data.VideoMixSize == "0.00")
            {
                data.VideoMixSize = "<0.01";
            }
            if (data.VideoSeriesSize == "0.00")
            {
                data.VideoSeriesSize = "<0.01";
            }
            var authorRows = await this._dyCollectVideoRepository.GetAuthorProjectionAsync();
            data.Authors = authorRows.GroupBy(x => x.Author).Select(x => new VideoStaticsItemDto
            {
                Name = x.Key,
                Count = x.LongCount(),
                Icon = x.LastOrDefault()?.AuthorAvatarUrl,
                UperId = x.LastOrDefault()?.AuthorId ?? x.LastOrDefault()?.DyUserId
            }).OrderByDescending(d => d.Count).ToList();
            return data;
        }
```

> 注：原 `CategoryCount = list.Select(x => x.Tag1).Distinct().Count()` 计入空字符串/NULL 为一个分类；Task 2 的 `GetStaticsScalarAsync` 用同一 `Distinct().CountAsync()` 语义，行为一致——由 Task 1 锁定的 `CategoryCount` golden 验证。

- [ ] **Step 2: 构建，确认 0 错误**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj -c Debug`
Expected: 0 Error。

- [ ] **Step 3: 跑全量测试，确认全绿（行为零变化）**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: PASS，全部测试（含 Task 1 扩充的 Categories/Authors 断言）保持绿。**若任一 golden 失败：说明重写改变了行为——回退本步实现并排查，禁止修改 golden。**

- [ ] **Step 4: 提交**

```bash
git add service/DouyinVideoService.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "perf(service): GetStatics uses SQL aggregation, no full-table in-memory load

Behavior pinned unchanged by widened characterization tests (16+ green).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: 重写 `GetChartData` 使用窄投影 + SQL 时间过滤

**Files:**
- Modify: `service/DouyinVideoService.cs:393-411`（`GetChartData` 方法体）

- [ ] **Step 1: 用以下实现替换整个 `GetChartData` 方法体**

```csharp
        public async Task<List<VideoChartItemDto>> GetChartData(int day = 7)
        {
            var date = DateTime.Now.AddDays(-day);
            var list = await _dyCollectVideoRepository.GetChartProjectionAsync(date);

            var resultData = list.GroupBy(x => x.SyncTime.ToString("yyyyMMdd")).Select(g => new VideoChartItemDto
            {
                Date = g.Key,
                Collect = g.Count(x => x.ViedoType == VideoTypeEnum.dy_collects || x.ViedoType == VideoTypeEnum.dy_custom_collect),
                Favorite = g.Count(x => x.ViedoType == VideoTypeEnum.dy_favorite),
                Follow = g.Count(x => x.ViedoType == VideoTypeEnum.dy_follows),
                Graphic = g.Count(x => string.IsNullOrEmpty(x.FileHash)),
                Mix = g.Count(x => x.ViedoType == VideoTypeEnum.dy_mix),
                Series = g.Count(x => x.ViedoType == VideoTypeEnum.dy_series),
            })
              .ToList();
            return resultData;
        }
```

> 分组键与各类型计数逐字保留；唯一变化是数据来源从「全实体 `GetListAsync`」换成「3 列投影」，过滤条件 `SyncTime > date` 同样下推 SQL（原本也是 SQL 过滤）。行为由 Task 1 的单日 + 多日 ChartData golden 验证。

- [ ] **Step 2: 构建，确认 0 错误**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet build dy.net.csproj -c Debug`
Expected: 0 Error。

- [ ] **Step 3: 跑全量测试，确认全绿**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: PASS，全部绿（含单日与多日 ChartData 断言）。失败则回退本步，禁止改 golden。

- [ ] **Step 4: 提交**

```bash
git add service/DouyinVideoService.cs
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "perf(service): GetChartData uses narrow projection + SQL time filter

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: 更新测试清单文档并推送

**Files:**
- Modify: `tests/README.md`（"What is pinned" 表格补充 Categories/Authors/多日 ChartData）
- Modify: 记忆 `project-dysync-security-hardening.md`（标注此 WARNING 项已完成）

- [ ] **Step 1: 在 `tests/README.md` 的 pinned 表 `GetStatics`/`GetChartData` 行补充说明现已覆盖 `Categories` 列表、`Authors` 列表（含 Icon/UperId 最后一行语义）、多日分组与顺序。**

- [ ] **Step 2: 跑全量测试最终确认**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj`
Expected: PASS 全绿。

- [ ] **Step 3: 提交并推送分支**

```bash
git add tests/README.md
git -c user.name='Claude Code' -c user.email='mjgenab@gmail.com' commit -m "docs: note widened GetStatics/GetChartData coverage post-SQL-refactor

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
git push origin decompile/dy-sync-lib
```

- [ ] **Step 4: 更新项目记忆**，记录「全表内存聚合 → SQL 聚合」WARNING 项已在测试保护下完成并推送（不合并、不开 PR）。

---

## Self-Review

- **Spec 覆盖：** WARNING 项「full-table in-memory loads (GetStatics/GetChartData)」由 Task 3/4 实现；前置安全网缺口（Categories/Authors/多日未钉死）由 Task 1 补齐；仓储聚合能力由 Task 2 提供；文档/记忆由 Task 5 收尾。其余 WARNING 项（god-class 拆分、async 清理、前端拦截器）不在本计划范围，是后续独立计划。
- **占位符扫描：** 仅 Task 1 的 `<<...>>` 与 `0L` 为 golden-master 方法学的「先跑后锁」显式捕获步骤（Step 3 明确替换），非计划空洞；其余步骤均含完整代码与确切命令。
- **类型一致性：** `VideoStaticsScalar`/`AuthorProjection`/`ChartProjection`（Task 2 定义）字段名与 Task 3/4 用法一致；仓储方法名 `GetStaticsScalarAsync`/`GetTag1ProjectionAsync`/`GetAuthorProjectionAsync`/`GetChartProjectionAsync` 在 Task 2 定义、Task 3/4 调用一致；`VideoStaticsDto`/`VideoStaticsItemDto`/`VideoChartItemDto`/`VideoTypeEnum`/`DouyinFileUtils.ConvertBytesToGb` 均沿用现有定义，未引入未定义符号。
- **风险：** `Db.Queryable<DouyinVideo>().Select(x=>x.AuthorId).Distinct().CountAsync()` 在 SQLite 下 NULL 去重语义需与原 LINQ `Distinct().Count()` 一致——由 Task 1 锁定的 `AuthorCount`/`CategoryCount` golden 在 Task 3 Step 3 自动验证；不一致会红灯，触发回退而非改 golden。
