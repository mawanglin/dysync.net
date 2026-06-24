# 定时任务管理 · 第二期(改调度)+第三期(执行记录) 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development 逐任务实现。步骤用复选框（`- [ ]`）跟踪。

**目标：** 在第一期任务管理页基础上，加「按任务改调度（间隔/cron，持久化+即时重排+重启不丢）」与「按任务执行记录（每次运行落库 + 弹窗分页查看）」。

**架构：** 新增 `dy_job_schedule` 表存每任务自定义周期，`DouyinQuartzJobService` 启动时读它、并提供 `UpdateJobScheduleAsync`（校验+入库+重排）；新增 `dy_sync_run_log` 表，`DouyinBasicSyncJob.Execute` 结束时落一条记录（每类型留 100），分页接口查看。周期校验抽纯函数单测。前端操作列加「改周期」「执行记录」两个按钮各配一个 modal。

**技术栈：** .NET 8 / Quartz / SqlSugar / xUnit；Vue 3 + Ant Design Vue。

**规格：** `docs/superpowers/specs/2026-06-23-scheduled-jobs-mgmt-phase2-3-design.md`

**关键现状（已探明）：**
- 新实体放 `dy.net.model.entity` → 自动进 `CodeFirst.InitTables` 建表（无需手动注册）。
- 新类放 `dy.net.repository` / `dy.net.service` → 经 `AddServicesFromNamespace` 自动 DI 注册（无需手动）。
- `BaseRepository<T>(ISqlSugarClient db)` 暴露 `protected ISqlSugarClient Db`。
- `DouyinQuartzJobService` 现有 `StartJobAsync(VideoTypeEnum, string expression)`（RemoveExistingJob + 按 cron/分钟重排）、`CreateScheduledTrigger` 同时支持 cron 与分钟。
- 测试命令：`DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`；构建 `dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`。

---

## 文件结构
| 文件 | 动作 |
|---|---|
| `utils/JobScheduleValidator.cs` + 测试 | 创建：周期校验纯函数 |
| `model/entity/DouyinJobSchedule.cs` | 创建：自定义周期表 |
| `repository/DouyinJobScheduleRepository.cs` | 创建 |
| `model/entity/DouyinSyncRunLog.cs` | 创建：执行记录表 |
| `repository/DouyinSyncRunLogRepository.cs` | 创建 |
| `service/DouyinSyncRunLogService.cs` | 创建 |
| `service/DouyinQuartzJobService.cs` | 改：注入 schedule 仓储；InitOrReStart 读自定义周期；RescheduleJobAsync + UpdateJobScheduleAsync |
| `job/DouyinBasicSyncJob.cs` + 6 子类 | 改：注入 runlog 服务 + Execute 落库 + 子类透传 |
| `Controllers/ConfigController.cs` | 改：UpdateJobSchedule + SyncRunLogs（注入 runlog 服务） |
| `app/src/store/coreapi.ts` | 改：UpdateJobSchedule、SyncRunLogs |
| `app/src/pages/syncstatus/index.vue` | 改：操作列 2 按钮 + 2 modal |
| `tests/README.md` | 改 |

---

## 任务 1：周期校验纯函数（TDD）

**文件：** 创建 `utils/JobScheduleValidator.cs`、`tests/dy.net.Tests/JobScheduleValidatorTests.cs`

- [ ] **步骤 1：创建 `utils/JobScheduleValidator.cs`**
```csharp
using Quartz;

namespace dy.net.utils
{
    /// <summary>校验并归一化任务周期输入。interval→正整数分钟字符串；cron→Quartz 校验。</summary>
    public static class JobScheduleValidator
    {
        public static (bool ok, string normalized, string error) ValidateAndNormalize(string scheduleType, string expression)
        {
            if (scheduleType == "interval")
            {
                if (int.TryParse(expression?.Trim(), out var m) && m > 0)
                    return (true, m.ToString(), null);
                return (false, null, "间隔分钟必须是正整数");
            }
            if (scheduleType == "cron")
            {
                var expr = expression?.Trim();
                if (!string.IsNullOrWhiteSpace(expr) && CronExpression.IsValidExpression(expr))
                    return (true, expr, null);
                return (false, null, "cron 表达式不合法");
            }
            return (false, null, "未知的周期类型");
        }
    }
}
```

- [ ] **步骤 2：创建测试 `tests/dy.net.Tests/JobScheduleValidatorTests.cs`**
```csharp
using dy.net.utils;

namespace dy.net.Tests
{
    public class JobScheduleValidatorTests
    {
        [Theory]
        [InlineData("interval", "30", true, "30")]
        [InlineData("interval", " 45 ", true, "45")]
        [InlineData("interval", "0", false, null)]
        [InlineData("interval", "-5", false, null)]
        [InlineData("interval", "abc", false, null)]
        [InlineData("cron", "0 0/30 * * * ?", true, "0 0/30 * * * ?")]
        [InlineData("cron", "not a cron", false, null)]
        [InlineData("weekly", "x", false, null)]
        public void ValidateAndNormalize_locks_behavior(string type, string expr, bool ok, string normalized)
        {
            var (resultOk, resultNorm, error) = JobScheduleValidator.ValidateAndNormalize(type, expr);
            Assert.Equal(ok, resultOk);
            if (ok) { Assert.Equal(normalized, resultNorm); Assert.Null(error); }
            else { Assert.Null(resultNorm); Assert.False(string.IsNullOrEmpty(error)); }
        }
    }
}
```

- [ ] **步骤 3：构建 + 测试**
构建 0 错误；`DOTNET_ROLL_FORWARD=LatestMajor dotnet test ...` 全绿（新增 8 用例）。

- [ ] **步骤 4：Commit**
```bash
git add utils/JobScheduleValidator.cs tests/dy.net.Tests/JobScheduleValidatorTests.cs
git commit -m "feat(sched): 周期校验纯函数 JobScheduleValidator + 测试"
```

---

## 任务 2：第二期后端（自定义周期持久化 + 重排 + 接口）

**文件：** 创建 `model/entity/DouyinJobSchedule.cs`、`repository/DouyinJobScheduleRepository.cs`；改 `service/DouyinQuartzJobService.cs`、`Controllers/ConfigController.cs`

- [ ] **步骤 1：实体 `model/entity/DouyinJobSchedule.cs`**
```csharp
using System;
using SqlSugar;

namespace dy.net.model.entity
{
    [SugarTable("dy_job_schedule")]
    public class DouyinJobSchedule
    {
        [SugarColumn(IsPrimaryKey = true, Length = 50)]
        public string Type { get; set; }              // VideoTypeEnum.ToString()
        [SugarColumn(Length = 20)]
        public string ScheduleType { get; set; }      // "interval" | "cron"
        [SugarColumn(Length = 200)]
        public string Expression { get; set; }        // 分钟数 或 cron
        public DateTime UpdatedAt { get; set; }
    }
}
```

- [ ] **步骤 2：仓储 `repository/DouyinJobScheduleRepository.cs`**
```csharp
using dy.net.model.entity;
using SqlSugar;

namespace dy.net.repository
{
    public class DouyinJobScheduleRepository : BaseRepository<DouyinJobSchedule>
    {
        public DouyinJobScheduleRepository(ISqlSugarClient db) : base(db) { }

        public Task<List<DouyinJobSchedule>> GetAllAsync()
            => Db.Queryable<DouyinJobSchedule>().ToListAsync();

        public async Task UpsertAsync(string type, string scheduleType, string expression, DateTime now)
        {
            await Db.Storageable(new DouyinJobSchedule
            {
                Type = type,
                ScheduleType = scheduleType,
                Expression = expression,
                UpdatedAt = now
            }).ExecuteCommandAsync();   // 按主键 Type upsert
        }
    }
}
```

- [ ] **步骤 3：`DouyinQuartzJobService` 注入仓储 + 改 InitOrReStart + 新方法**

(a) 构造函数加依赖。把：
```csharp
        public DouyinQuartzJobService(ISchedulerFactory schedulerFactory,DouyinCookieService douyinCookieService)
        {
            _schedulerFactory = schedulerFactory ?? throw new ArgumentNullException(nameof(schedulerFactory));
            this.douyinCookieService = douyinCookieService;
        }
```
改为（加字段 + 参数）：
```csharp
        private readonly DouyinJobScheduleRepository _scheduleRepository;

        public DouyinQuartzJobService(ISchedulerFactory schedulerFactory, DouyinCookieService douyinCookieService, DouyinJobScheduleRepository scheduleRepository)
        {
            _schedulerFactory = schedulerFactory ?? throw new ArgumentNullException(nameof(schedulerFactory));
            this.douyinCookieService = douyinCookieService;
            _scheduleRepository = scheduleRepository;
        }
```
（文件已 `using dy.net.utils;`、`using dy.net.model.dto;`；需加 `using dy.net.repository;` 与 `using dy.net.model.entity;`——若缺则补。）

(b) `InitOrReStartAllJobs` 的任务循环改为读自定义周期。在 `int successfullyStartedJobs = 0;` 之前插入：
```csharp
                // 读取每任务自定义周期（覆盖全局）
                var customSchedules = (await _scheduleRepository.GetAllAsync())
                    .ToDictionary(s => s.Type, s => s.Expression);
```
然后把 followuser 分支与普通分支的"启动表达式"改为优先用自定义。具体：
- followuser 分支：
```csharp
                    if (jobKey == VideoTypeEnum.dy_followuser)
                    {
                        var expr = customSchedules.TryGetValue(jobKey.ToString(), out var cs) ? cs : "60";
                        bool startSuccess = await StartSingleJobAsync(jobKey, expr);
                        if (startSuccess) successfullyStartedJobs++;
                        continue;
                    }
```
- 普通启用分支里 `StartSingleJobAsync(jobKey, taskIntervalExpression)` 改为：
```csharp
                    if (isTaskEnabled)
                    {
                        var expr = customSchedules.TryGetValue(jobKey.ToString(), out var cs) ? cs : taskIntervalExpression;
                        bool startSuccess = await StartSingleJobAsync(jobKey, expr);
                        if (startSuccess) successfullyStartedJobs++;
                    }
```

(c) 类内新增两个方法：
```csharp
        /// <summary>仅当该任务当前已调度时即时重排；未调度则不动（待启用时由 InitOrReStart 读自定义周期生效）。</summary>
        public async Task<bool> RescheduleJobAsync(VideoTypeEnum type, string expression)
        {
            if (!JobConfigs.TryGetValue(type, out var cfg)) return false;
            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = new JobKey(cfg.JobKey, DefaultJobGroup);
            if (!await scheduler.CheckExists(jobKey)) return false;
            return await StartJobAsync(type, expression);
        }

        /// <summary>校验 + 持久化 + 即时重排某任务周期。</summary>
        public async Task<(bool ok, string error)> UpdateJobScheduleAsync(VideoTypeEnum type, string scheduleType, string expression)
        {
            if (type == VideoTypeEnum.dy_followuser_once)
                return (false, "该任务不可配置");
            var (ok, normalized, error) = JobScheduleValidator.ValidateAndNormalize(scheduleType, expression);
            if (!ok) return (false, error);
            await _scheduleRepository.UpsertAsync(type.ToString(), scheduleType, normalized, DateTime.Now);
            await RescheduleJobAsync(type, normalized);   // 已调度才即时重排；未调度仅入库
            return (true, null);
        }
```

- [ ] **步骤 4：`ConfigController` 新增 `UpdateJobSchedule` 接口**
在 `SyncJobs` 方法之后新增：
```csharp
        /// <summary>修改某任务的执行周期（间隔分钟或 cron），即时生效且重启不丢。</summary>
        [HttpPost("UpdateJobSchedule")]
        public async Task<IActionResult> UpdateJobSchedule([FromBody] UpdateJobScheduleDto dto)
        {
            if (dto == null || !Enum.TryParse<VideoTypeEnum>(dto.Type, true, out var vt))
                return ApiResult.Fail("参数无效");
            var (ok, error) = await quartzJobService.UpdateJobScheduleAsync(vt, dto.ScheduleType, dto.Expression);
            return ok ? ApiResult.Success(new { updated = true }, "周期已更新") : ApiResult.Fail(error);
        }
```
并创建入参 DTO `model/dto/UpdateJobScheduleDto.cs`：
```csharp
namespace dy.net.model.dto
{
    public class UpdateJobScheduleDto
    {
        public string Type { get; set; }
        public string ScheduleType { get; set; }   // "interval" | "cron"
        public string Expression { get; set; }
    }
}
```
（`ConfigController` 已注入 `quartzJobService`；`model/dto` 已 using。）

- [ ] **步骤 5：构建验证**
`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`（0 错误）。

- [ ] **步骤 6：Commit**
```bash
git add model/entity/DouyinJobSchedule.cs repository/DouyinJobScheduleRepository.cs model/dto/UpdateJobScheduleDto.cs service/DouyinQuartzJobService.cs Controllers/ConfigController.cs
git commit -m "feat(sched): 第二期-按任务自定义周期（持久化+即时重排+UpdateJobSchedule 接口）"
```

---

## 任务 3：第三期后端（执行记录落库 + 查询）

**文件：** 创建 `model/entity/DouyinSyncRunLog.cs`、`repository/DouyinSyncRunLogRepository.cs`、`service/DouyinSyncRunLogService.cs`；改 `job/DouyinBasicSyncJob.cs` + 6 子类、`Controllers/ConfigController.cs`

- [ ] **步骤 1：实体 `model/entity/DouyinSyncRunLog.cs`**
```csharp
using System;
using System.Text.Json.Serialization;
using SqlSugar;

namespace dy.net.model.entity
{
    // 直接带 [JsonPropertyName] 确保接口返回 camelCase（与项目其它响应 DTO 约定一致，不依赖全局策略）
    [SugarTable("dy_sync_run_log")]
    public class DouyinSyncRunLog
    {
        [SugarColumn(IsPrimaryKey = true, Length = 50)]
        [JsonPropertyName("id")] public string Id { get; set; }
        [SugarColumn(Length = 50)]
        [JsonPropertyName("type")] public string Type { get; set; }
        [SugarColumn(Length = 50)]
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("startedAt")] public DateTime StartedAt { get; set; }
        [JsonPropertyName("endedAt")] public DateTime EndedAt { get; set; }
        [JsonPropertyName("downloaded")] public int Downloaded { get; set; }
        [JsonPropertyName("failed")] public int Failed { get; set; }
        [SugarColumn(Length = 20)]
        [JsonPropertyName("status")] public string Status { get; set; }   // "completed" | "stopped"
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }
}
```

- [ ] **步骤 2：仓储 `repository/DouyinSyncRunLogRepository.cs`**
```csharp
using dy.net.model.entity;
using SqlSugar;

namespace dy.net.repository
{
    public class DouyinSyncRunLogRepository : BaseRepository<DouyinSyncRunLog>
    {
        public DouyinSyncRunLogRepository(ISqlSugarClient db) : base(db) { }

        /// <summary>插入一条；并裁剪该类型超出最近 keepPerType 条的旧记录。</summary>
        public async Task AddAndPruneAsync(DouyinSyncRunLog log, int keepPerType)
        {
            await Db.Insertable(log).ExecuteCommandAsync();
            var oldIds = await Db.Queryable<DouyinSyncRunLog>()
                .Where(x => x.Type == log.Type)
                .OrderBy(x => x.StartedAt, OrderByType.Desc)
                .Skip(keepPerType)
                .Select(x => x.Id)
                .ToListAsync();
            if (oldIds.Count > 0)
                await Db.Deleteable<DouyinSyncRunLog>().In(oldIds).ExecuteCommandAsync();
        }

        public async Task<(List<DouyinSyncRunLog> list, int total)> GetPagedAsync(string type, int page, int size)
        {
            RefAsync<int> total = 0;
            var list = await Db.Queryable<DouyinSyncRunLog>()
                .Where(x => x.Type == type)
                .OrderBy(x => x.StartedAt, OrderByType.Desc)
                .ToPageListAsync(page, size, total);
            return (list, total);
        }
    }
}
```

- [ ] **步骤 3：服务 `service/DouyinSyncRunLogService.cs`**
```csharp
using ClockSnowFlake;
using dy.net.model.dto;
using dy.net.model.entity;
using dy.net.repository;

namespace dy.net.service
{
    public class DouyinSyncRunLogService
    {
        private const int KeepPerType = 100;
        private readonly DouyinSyncRunLogRepository _repo;
        public DouyinSyncRunLogService(DouyinSyncRunLogRepository repo) { _repo = repo; }

        public Task RecordAsync(VideoTypeEnum type, string name, DateTime startedAt, DateTime endedAt, int downloaded, int failed, string status)
            => _repo.AddAndPruneAsync(new DouyinSyncRunLog
            {
                Id = IdGener.GetLong().ToString(),
                Type = type.ToString(),
                Name = name,
                StartedAt = startedAt,
                EndedAt = endedAt,
                Downloaded = downloaded,
                Failed = failed,
                Status = status,
                CreatedAt = DateTime.Now
            }, KeepPerType);

        public Task<(List<DouyinSyncRunLog> list, int total)> GetPagedAsync(string type, int page, int size)
            => _repo.GetPagedAsync(type, page, size);
    }
}
```

- [ ] **步骤 4：基类 `DouyinBasicSyncJob` 注入 + Execute 落库**

(a) 字段（在 `protected readonly SyncRunState syncRunState;` 之后）加：
```csharp
        /// <summary>执行记录落库服务</summary>
        protected readonly DouyinSyncRunLogService syncRunLogService;
```
(b) 构造函数：在最后一个参数 `SyncRunState syncRunState` 之后追加 `DouyinSyncRunLogService syncRunLogService`，并在体内赋值 `this.syncRunLogService = syncRunLogService;`。
（`DouyinBasicSyncJob.cs` 已 `using dy.net.service;`。）
(c) `Execute` 改为记录开始时间并在 finally 落库。把当前：
```csharp
            // 遍历每个有效的Cookie，执行同步
            syncRunState.RegisterStart(VideoType, cookies.FirstOrDefault()?.UserName ?? "", DateTime.Now);
            try
            {
                foreach (var cookie in cookies)
                {
                    syncRunState.SetCurrentCookie(VideoType, cookie.UserName);
                    await ProcessSyncUserCookie(cookie, config);
                }
            }
            finally
            {
                syncRunState.RegisterFinish(VideoType, DateTime.Now);
            }
        }
```
改为：
```csharp
            // 遍历每个有效的Cookie，执行同步
            var runStartedAt = DateTime.Now;
            syncRunState.RegisterStart(VideoType, cookies.FirstOrDefault()?.UserName ?? "", runStartedAt);
            try
            {
                foreach (var cookie in cookies)
                {
                    syncRunState.SetCurrentCookie(VideoType, cookie.UserName);
                    await ProcessSyncUserCookie(cookie, config);
                }
            }
            finally
            {
                var endedAt = DateTime.Now;
                bool stopped = syncRunState.Token.IsCancellationRequested;
                syncRunState.RegisterFinish(VideoType, endedAt);
                // 落执行记录（失败不影响主流程）
                try
                {
                    var snap = syncRunState.GetSnapshot(endedAt);
                    var t = snap.Types.FirstOrDefault(x => x.Type == VideoType.ToString());
                    await syncRunLogService.RecordAsync(
                        VideoType, VideoType.GetDesc(), runStartedAt, endedAt,
                        t?.Downloaded ?? 0, t?.Failed ?? 0,
                        stopped ? "stopped" : "completed");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"[{VideoType.GetDesc()}]记录执行历史失败");
                }
            }
        }
```

- [ ] **步骤 5：6 个子类构造函数透传 `DouyinSyncRunLogService`**
对 `DouYinCollectSyncJob`、`DouYinFavoritSyncJob`、`DouyinFollowedSyncJob`、`DouyinCollectCustomSyncJob`、`DouyinMixSyncJob`、`DouyinSeriesSyncJob`：构造函数参数末尾（当前最后是 `SyncRunState syncRunState`）追加 `, DouyinSyncRunLogService syncRunLogService`，`base(...)` 末尾追加 `, syncRunLogService`。各文件已 `using dy.net.service;`。

示例（DouYinCollectSyncJob，其余同构）：
```csharp
        public DouyinCollectSyncJob(DouyinCookieService douyinCookieService, DouyinHttpClientService douyinHttpClientService, DouyinVideoService douyinVideoService, DouyinCommonService douyinCommonService, DouyinFollowService douyinFollowService, DouyinMergeVideoService douyinMergeVideoService, DouyinCollectCateService douyinCollectCateService, SyncRunState syncRunState, DouyinSyncRunLogService syncRunLogService) : base(douyinCookieService, douyinHttpClientService, douyinVideoService, douyinCommonService, douyinFollowService, douyinMergeVideoService, douyinCollectCateService, syncRunState, syncRunLogService)
        {
        }
```
（逐个文件按各自类名做相同变换；6 个都要改，否则编译失败。）

- [ ] **步骤 6：`ConfigController` 注入 runlog 服务 + `SyncRunLogs` 接口**
(a) 构造函数参数末尾追加 `DouyinSyncRunLogService syncRunLogService`，加字段 `private readonly DouyinSyncRunLogService syncRunLogService;` 并赋值。
(b) 新增接口（在 `UpdateJobSchedule` 之后）：
```csharp
        /// <summary>某任务的执行记录分页查询。</summary>
        [HttpGet("SyncRunLogs")]
        public async Task<IActionResult> SyncRunLogs(string type, int page = 1, int size = 10)
        {
            if (string.IsNullOrWhiteSpace(type)) return ApiResult.Fail("type 必填");
            var (list, total) = await syncRunLogService.GetPagedAsync(type, page < 1 ? 1 : page, size < 1 ? 10 : size);
            return ApiResult.Success(new { list, total });
        }
```
（`DouyinSyncRunLog` 实体已在步骤 1 直接带 `[JsonPropertyName]` camelCase，接口直接返回实体即可保证 `id/type/name/startedAt/endedAt/downloaded/failed/status/createdAt` 大小写正确。）

- [ ] **步骤 7：构建 + 测试**
`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`（0 错误）；`DOTNET_ROLL_FORWARD=LatestMajor dotnet test ...`（全绿）。

- [ ] **步骤 8：Commit**
```bash
git add model/entity/DouyinSyncRunLog.cs repository/DouyinSyncRunLogRepository.cs service/DouyinSyncRunLogService.cs job/DouyinBasicSyncJob.cs job/DouYinCollectSyncJob.cs job/DouYinFavoritSyncJob.cs job/DouyinFollowedSyncJob.cs job/DouyinCollectCustomSyncJob.cs job/DouyinMixSyncJob.cs job/DouyinSeriesSyncJob.cs Controllers/ConfigController.cs
git commit -m "feat(sched): 第三期-执行记录落库（每类型留100）+ SyncRunLogs 分页接口"
```

---

## 任务 4：前端（改周期 + 执行记录 两个 modal）

**文件：** 改 `app/src/store/coreapi.ts`、`app/src/pages/syncstatus/index.vue`

- [ ] **步骤 1：`coreapi.ts` 新增两方法并导出**
在 `SyncJobs` 之后新增：
```typescript
  // 修改任务周期
  async function UpdateJobSchedule(param: object) {
    return http.request<any, Response<any>>('/api/config/UpdateJobSchedule', 'post_json', param).then(r => {
      return r;
    }).finally(() => {

    });
  }
  // 某任务执行记录分页
  async function SyncRunLogs(type: string, page: number, size: number) {
    return http.request<any, Response<any>>(`/api/config/SyncRunLogs?type=${encodeURIComponent(type)}&page=${page}&size=${size}`, 'get').then(r => {
      return r;
    }).finally(() => {

    });
  }
```
导出块 `SyncJobs,` 之后加 `UpdateJobSchedule,` 和 `SyncRunLogs,`。

- [ ] **步骤 2：`syncstatus/index.vue` 操作列加两个按钮**
在操作列（`column.key === 'op'`）里，现有「立即执行」「详情」之间或之后插入：
```html
            <a-button type="link" size="small" @click="openSchedule(record)">改周期</a-button>
            <a-button type="link" size="small" @click="openLogs(record)">执行记录</a-button>
```

- [ ] **步骤 3：`syncstatus/index.vue` 加「改周期」modal**
在 `<a-drawer>` 详情抽屉之后插入：
```html
    <a-modal :visible="scheduleOpen" :title="scheduleTarget ? scheduleTarget.name + ' · 改周期' : '改周期'" @ok="submitSchedule" @cancel="scheduleOpen = false" :confirm-loading="scheduleSubmitting">
      <a-form layout="vertical">
        <a-form-item label="周期类型">
          <a-radio-group v-model:value="scheduleForm.scheduleType">
            <a-radio value="interval">间隔分钟</a-radio>
            <a-radio value="cron">Cron 表达式</a-radio>
          </a-radio-group>
        </a-form-item>
        <a-form-item :label="scheduleForm.scheduleType === 'cron' ? 'Cron 表达式' : '间隔分钟'">
          <a-input v-model:value="scheduleForm.expression"
                   :placeholder="scheduleForm.scheduleType === 'cron' ? '如 0 0/30 * * * ?' : '如 30'" />
        </a-form-item>
      </a-form>
    </a-modal>
```

- [ ] **步骤 4：`syncstatus/index.vue` 加「执行记录」modal**
继续插入：
```html
    <a-modal :visible="logsOpen" :title="logsTarget ? logsTarget.name + ' · 执行记录' : '执行记录'" :footer="null" @cancel="logsOpen = false" width="640">
      <a-table :data-source="logs" :columns="logColumns" row-key="id" size="small"
               :pagination="{ current: logsPage, pageSize: logsSize, total: logsTotal, onChange: onLogsPage }">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'duration'">{{ durationSec(record) }} 秒</template>
          <template v-else-if="column.key === 'status'">
            <a-tag :color="record.status === 'stopped' ? 'warning' : 'success'">{{ record.status === 'stopped' ? '被停止' : '完成' }}</a-tag>
          </template>
          <template v-else-if="column.key === 'started'">{{ formatTime(record.startedAt) }}</template>
          <template v-else-if="column.key === 'ended'">{{ formatTime(record.endedAt) }}</template>
        </template>
      </a-table>
    </a-modal>
```

- [ ] **步骤 5：`syncstatus/index.vue` script 加状态与方法**
在 `<script setup>` 内（`detail`/`detailOpen` 附近）新增：
```typescript
// 改周期
const scheduleOpen = ref(false);
const scheduleTarget = ref<any>(null);
const scheduleSubmitting = ref(false);
const scheduleForm = ref<any>({ scheduleType: 'interval', expression: '' });
const openSchedule = (record: any) => {
  scheduleTarget.value = record;
  scheduleForm.value = { scheduleType: 'interval', expression: '' };
  scheduleOpen.value = true;
};
const submitSchedule = () => {
  if (scheduleSubmitting.value) return;
  scheduleSubmitting.value = true;
  useApiStore().UpdateJobSchedule({
    type: scheduleTarget.value.type,
    scheduleType: scheduleForm.value.scheduleType,
    expression: scheduleForm.value.expression,
  })
    .then((res) => {
      if (res.code === 0) { message.success(res.message || '周期已更新'); scheduleOpen.value = false; refresh(); }
      else message.error(res.message || '更新失败');
    })
    .catch(() => message.error('更新失败，请检查网络'))
    .finally(() => { scheduleSubmitting.value = false; });
};

// 执行记录
const logsOpen = ref(false);
const logsTarget = ref<any>(null);
const logs = ref<any[]>([]);
const logsPage = ref(1);
const logsSize = ref(10);
const logsTotal = ref(0);
const logColumns = [
  { title: '开始', key: 'started' },
  { title: '结束', key: 'ended' },
  { title: '耗时', key: 'duration' },
  { title: '下载', dataIndex: 'downloaded', key: 'downloaded' },
  { title: '失败', dataIndex: 'failed', key: 'failed' },
  { title: '结果', key: 'status' },
];
const durationSec = (record: any) => {
  try { return Math.max(0, Math.round((new Date(record.endedAt).getTime() - new Date(record.startedAt).getTime()) / 1000)); }
  catch { return 0; }
};
const loadLogs = () => {
  useApiStore().SyncRunLogs(logsTarget.value.type, logsPage.value, logsSize.value)
    .then((res) => {
      if (res.code === 0 && res.data) { logs.value = res.data.list || []; logsTotal.value = res.data.total || 0; }
    })
    .catch(() => {});
};
const openLogs = (record: any) => {
  logsTarget.value = record;
  logsPage.value = 1;
  logsOpen.value = true;
  loadLogs();
};
const onLogsPage = (p: number) => { logsPage.value = p; loadLogs(); };
```

- [ ] **步骤 6：构建（如本机 pnpm 可用）/ 自检**
`cd app && pnpm exec vite build`（别动 lockfile/workspace）；不可用则逐项自检语法/字段/导入。

- [ ] **步骤 7：Commit（仅这 2 个文件）**
```bash
git add app/src/store/coreapi.ts app/src/pages/syncstatus/index.vue
git commit -m "feat(web): 定时任务页加改周期 + 执行记录两个 modal"
```

---

## 任务 5：文档 + 验证 + 推送

**文件：** 改 `tests/README.md`

- [ ] **步骤 1：README 加 validator 覆盖**
覆盖表新增：
```markdown
| `JobScheduleValidator` | `JobScheduleValidatorTests` | 周期校验：interval 正整数(含空格 trim)→归一化·0/负/非数字→失败·cron 经 Quartz 校验合法/非法·未知类型→失败 |
```

- [ ] **步骤 2：全量构建 + 测试**
`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`（0 错误）；测试全绿。

- [ ] **步骤 3：Commit + 推送**
```bash
git add tests/README.md
git commit -m "docs(tests): JobScheduleValidator 覆盖"
git push
```

---

## 实现后人工验证（部署新镜像后）
- 「定时任务」页操作列出现「改周期」「执行记录」。
- 改周期：选间隔填 5、确定 → 该行"周期"变"每 5 分钟"、"下次执行"按新周期刷新；选 cron 填 `0 0/2 * * * ?` → 变"Cron: ..."。重启容器后仍是自定义周期（不被全局覆盖）。
- 非法输入（间隔填 0 / cron 填乱串）→ 报错不生效。
- 跑过几轮后点「执行记录」→ 弹窗分页显示该任务历史（开始/结束/耗时/下载/失败/结果）；超过 100 条自动只留最近 100。
- 关注列表任务的执行记录为空（设计如此）。

## 自检
- 规格点（自定义周期表+校验+重排+重启读取+改周期接口/UI；执行记录表+落库+裁剪100+分页接口/UI）均有任务覆盖 ✅
- 无占位符；代码步骤含完整代码 ✅
- 字段一致：`DouyinSyncRunLog` 加 `[JsonPropertyName]` camelCase 与前端 `id/type/name/startedAt/endedAt/downloaded/failed/status` 对应；`UpdateJobScheduleDto`(type/scheduleType/expression) 与前端提交体一致 ✅
- 构造签名变更（基类 + 6 子类追加 `DouyinSyncRunLogService`）已全覆盖 ✅
- 新实体/仓储/服务自动注册（实体扫描 model.entity；仓储/服务扫描 repository/service 命名空间），无需手动 DI ✅
