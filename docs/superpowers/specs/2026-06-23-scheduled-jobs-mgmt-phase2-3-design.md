# 定时任务管理 · 第二期(改调度) + 第三期(执行记录) 合并设计规格

- 日期：2026-06-23
- 分支：improve/dysync-refactor
- 状态：已与用户确认，待实现
- 前置：第一期（任务列表+详情，`/api/config/SyncJobs` + `/syncstatus` 任务管理页）已上线。
- 背景：在第一期任务管理页基础上，加"按任务改调度"与"按任务执行记录"。两期一次性实现。

## 1. 范围

- **第二期**：每个任务可编辑执行周期（间隔分钟 或 cron），持久化、即时重排、重启不丢。
- **第三期**：每次运行落库为执行记录，操作列「执行记录」按钮弹窗分页查看该任务历史。

两期共用第一期的任务表格；各加一个操作列按钮 + 一个 modal。

## 2. 已确认决策
1. 编辑周期支持**间隔分钟 与 cron 二选一**（cron 用 `CronExpression.IsValidExpression` 校验）。7 个任务都可编辑。
2. 执行记录用**每行「执行记录」按钮 → 分页弹窗**（按该任务类型查询）。不另开菜单。
3. 关注列表任务（`DouyinFollowsAndCollnectsSyncJob`，非 `DouyinBasicSyncJob`）**不落执行记录**——其弹窗为空（与第一期它无运行结果一致）。
4. 执行记录每类型只保留最近 **100** 条，插入时裁剪。
5. 操作列按钮：立即执行 / 改周期 / 执行记录 / 详情。
6. 记录状态只取 `completed` / `stopped`（取消触发即 stopped）——`error` 不做：per-cookie 异常已被 `ProcessSyncUserCookie` 内部吞掉并记日志，Execute 层拿不到，强行判 error 不可靠。

## 3. 第二期：按任务编辑调度

### 3.1 持久化实体 `model/entity/DouyinJobSchedule.cs`
```
[SugarTable("dy_job_schedule")]
- Type        (PK, string, 如 "dy_collects")
- ScheduleType(string, "interval" | "cron")
- Expression  (string, 间隔分钟数 或 cron 表达式)
- UpdatedAt   (DateTime)
```
仓储 `DouyinJobScheduleRepository : BaseRepository<DouyinJobSchedule>`（按现有仓储模式）。
新实体需纳入 `CodeFirst.InitTables` 的实体列表、仓储按现有 DI 模式注册。

### 3.2 校验（纯函数，可测试）
`utils/JobScheduleValidator.cs`：`ValidateAndNormalize(string scheduleType, string expression) -> (bool ok, string normalized, string error)`：
- `interval`：解析为正整数分钟（>0）→ normalized = 分钟字符串；否则 error。
- `cron`：`CronExpression.IsValidExpression(expression)` → normalized = 原 cron；否则 error。
- 其它 scheduleType → error。
（注：`CronExpression` 在 Quartz，纯函数可直接调用静态校验，仍可单测；若想完全无依赖，可把"是否合法 cron"作为入参 bool 传入——本规格选直接用 Quartz 静态方法，测试覆盖 interval 分支 + 非法/未知分支，cron 合法性交给 Quartz。）

### 3.3 调度服务改造 `DouyinQuartzJobService`
- `InitOrReStartAllJobs`：启动每个任务前，先查 `dy_job_schedule` 取该类型的自定义周期；有则用之（cron 直接传，interval 传分钟字符串——现有 `CreateScheduledTrigger` 已同时支持），无则回退现有逻辑（全局 `Cron`；关注列表默认 60）。需要注入 `DouyinJobScheduleRepository`。
- 新增 `Task<bool> RescheduleJobAsync(VideoTypeEnum type, string expression)`：复用现有 `StartJobAsync(type, expression)`（它本就 RemoveExistingJob + 按 cron/分钟重排）。仅当该任务当前已在调度器中（已启用）时重排；未启用则只入库、待启用生效。

### 3.4 接口 `ConfigController`
`POST /api/config/UpdateJobSchedule`，body `{type, scheduleType, expression}`：
- `Enum.TryParse<VideoTypeEnum>` 校验 type（须在可管理列表内、非 once）。
- `JobScheduleValidator.ValidateAndNormalize` 校验；失败 `ApiResult.Fail(error)`。
- upsert `dy_job_schedule`（按 Type 主键）。
- 调 `quartzJobService.RescheduleJobAsync(type, normalized)` 即时生效（已调度才重排）。
- 成功返回。

### 3.5 前端
- 操作列加「改周期」按钮 → modal 表单：`a-radio-group`(间隔分钟/cron) + `a-input`(值，间隔时数字、cron 时文本) + 当前值回填（来自该行 scheduleDesc/已存值）。
- 提交调 `UpdateJobSchedule`，成功 message + 刷新表格。
- `coreapi.ts` 加 `UpdateJobSchedule(param)`（post_json）。

## 4. 第三期：执行记录

### 4.1 持久化实体 `model/entity/DouyinSyncRunLog.cs`
```
[SugarTable("dy_sync_run_log")]
- Id         (PK, string，雪花/Guid)
- Type       (string)
- Name       (string)
- StartedAt  (DateTime)
- EndedAt    (DateTime)
- Downloaded (int)
- Failed     (int)
- Status     (string, "completed" | "stopped")
- CreatedAt  (DateTime)
```
仓储 `DouyinSyncRunLogRepository : BaseRepository<DouyinSyncRunLog>`：
- `AddAndPruneAsync(DouyinSyncRunLog log, int keepPerType)`：插入后，按 Type 删除超出最近 keepPerType 条的旧记录。
- `Task<(List<DouyinSyncRunLog> list, int total)> GetPagedAsync(string type, int page, int size)`：按 Type 倒序分页。
新实体纳入 InitTables、仓储 DI 注册。

### 4.2 落库钩子 `DouyinBasicSyncJob`
- 构造注入 `DouyinSyncRunLogService`（薄封装仓储），透传到 6 个子类（同 SyncRunState 注入模式）。
- `Execute`：在 RegisterStart 前记 `var startedAt = DateTime.Now;`。finally 里（RegisterFinish 之后）：
  - 取 `syncRunState.GetSnapshot(now)` 中本类型条目的 downloaded/failed（RegisterFinish 后该类型仍在快照、Running=false）。
  - `status = syncRunState.Token.IsCancellationRequested ? "stopped" : "completed"`。
  - `await runLogService.RecordAsync(VideoType, VideoType.GetDesc(), startedAt, now, downloaded, failed, status)`（内部 AddAndPrune，keepPerType=100）。
  - 落库失败不影响主流程（try/catch 包裹，仅记日志）。

### 4.3 接口 `ConfigController`
`GET /api/config/SyncRunLogs?type=xxx&page=1&size=10` → `ApiResult.Success(new { list, total })`，list 每项含 type/name/startedAt/endedAt/downloaded/failed/status（camelCase，DTO 或直接返回实体均可——统一用一个 `SyncRunLogDto` 带 JsonPropertyName 以保证 camelCase）。

### 4.4 前端
- 操作列加「执行记录」按钮 → modal：`a-table` 分页（开始/结束/耗时(由前端按 ended-started 计算)/下载/失败/结果）；翻页调 `SyncRunLogs(type,page,size)`。
- `coreapi.ts` 加 `SyncRunLogs(type, page, size)`。

## 5. 测试
- 后端：`JobScheduleValidator.ValidateAndNormalize` 特征化测试（interval 正/负/非数字、未知 scheduleType；cron 合法/非法各一）。
- 仓储 `AddAndPruneAsync`/`GetPagedAsync` 与 Quartz 重排属 I/O，不单测（与现有约定一致）。
- 前端：CI vite build 验证。
- `tests/README.md` 增加 validator 覆盖。

## 6. 取舍（YAGNI）
- 状态仅 completed/stopped。
- 每类型留 100 条。
- 关注列表不落执行记录。
- 改周期对"未启用"任务只入库、不强行启动（保持未启用语义）。
- 执行记录弹窗按任务，不做全量跨任务菜单。

## 7. 影响文件清单（预计）
- 新增：`model/entity/DouyinJobSchedule.cs`、`model/entity/DouyinSyncRunLog.cs`、
  `repository/DouyinJobScheduleRepository.cs`、`repository/DouyinSyncRunLogRepository.cs`、
  `service/DouyinSyncRunLogService.cs`、`utils/JobScheduleValidator.cs`、
  `tests/dy.net.Tests/JobScheduleValidatorTests.cs`、（可选 DTO）`model/dto/SyncRunLogDto.cs`
- 修改：`service/DouyinQuartzJobService.cs`（InitOrReStartAllJobs 读自定义周期 + RescheduleJobAsync + 注入 schedule 仓储）、
  `job/DouyinBasicSyncJob.cs`（注入 runLogService + Execute 落库）、6 个子类构造透传、
  `Controllers/ConfigController.cs`（UpdateJobSchedule + SyncRunLogs）、
  `extension/ServiceExtension.cs`（InitTables 实体列表 + 新仓储/服务 DI，如需）、
  `app/src/store/coreapi.ts`、`app/src/pages/syncstatus/index.vue`（两个按钮+两个 modal）、
  `tests/README.md`
- 不动：`SyncRunState.cs`、`SyncStatusPanel.vue`、`MobileDashboard.vue`、`RecordTable.vue`
