# 定时任务管理 · 第一期（列表 + 详情）设计规格

- 日期：2026-06-23
- 分支：improve/dysync-refactor
- 状态：已与用户确认，待实现
- 背景：把现有"同步状态"页演进为"定时任务管理"。整体分三期，本规格只覆盖**第一期**。

## 1. 背景与分期

"同步状态"页（每类型一行的进度面板）改造为**定时任务管理**。完整能力分三期，各自独立规格→计划→实现：

- **第一期（本期）**：任务列表 + 详情（只读视图 + 复用现成的手动触发/停止）。替换现有 `/syncstatus` 页。
- 第二期（后续）：编辑调度（按任务设置周期/cron + 重排）。当前是单一全局 `AppConfig.Cron`，第二期再改调度模型。
- 第三期（后续）：执行记录（新建运行历史表 + 落库 + 查询）。当前无任何运行历史持久化。

本期**不**做：改调度、执行历史持久化、单类型停止、移动端改造。

## 2. 已确认决策

1. 列表显示**全部 7 个可管理周期任务**：收藏 `dy_collects`、喜欢 `dy_favorite`、关注作品 `dy_follows`、自定义收藏夹 `dy_custom_collect`、合集 `dy_mix`、短剧 `dy_series`、关注列表元数据 `dy_followuser`。未启用/未调度的也列出并标"未启用"。（一次性任务 `dy_followuser_once` 排除。）
2. 操作：顶部保留全局「立即同步(全部)/停止(全部)」；**每行加「立即执行」**（单类型，复用 `TriggerSyncNow?type=`）。单行停止本期不做（取消令牌整批共享），停止统一用顶部"停止(全部)"。
3. 详情：点行弹**抽屉**展示该任务完整信息。
4. 菜单显示名改为「定时任务」；路由 `/syncstatus` 保持不变（不破坏链接）。
5. 移动端本期保持现状（`MobileDashboard` 继续用 `SyncStatusPanel`）。
6. 关注列表元数据任务 `dy_followuser` 未接入 `SyncRunState`，其"最近一轮结果"为空，仅显示调度信息——可接受。

## 3. 架构

### 3.1 后端：任务总览
- `DouyinQuartzJobService` 新增 `Task<List<SyncJobOverview>> GetJobsOverviewAsync()`：
  - 遍历"可管理类型"列表（`JobConfigs` 的 key 去掉 `dy_followuser_once`）。
  - 每个类型：用 `scheduler.GetTrigger(triggerKey)` 取触发器；
    - 未取到（null）→ `Scheduled=false`、`ScheduleDesc="未启用"`、时间为 null。
    - 取到 → `Scheduled=true`；`NextFireTime`/`PrevFireTime` 来自 `trigger.GetNextFireTimeUtc()/GetPreviousFireTimeUtc()`（转本地时间）；`TriggerState` 来自 `scheduler.GetTriggerState(triggerKey)`；`ScheduleDesc` 由 trigger 派生（见 3.2）。
  - 合并 `SyncRunState.GetSnapshot()` 的 per-type：若该类型当前 running → `Running=true` + 实时 downloaded/failed/currentTitle；否则取其最近一轮 downloaded/failed/endedAt（快照里该类型那条）。
  - 关注列表元数据类型在 SyncRunState 里没有 → 运行/结果字段留空。
- 新接口 `ConfigController` `GET /api/config/SyncJobs` → `ApiResult.Success(await quartzJobService.GetJobsOverviewAsync())`（沿用类级 `[Authorize]`）。

### 3.2 周期描述派生（纯逻辑，可测试）
- 抽一个纯函数（如 `SyncJobScheduleDescriber.Describe(ITrigger trigger)` 或对已取出的字段判断），规则：
  - `ISimpleTrigger`：`RepeatInterval` → "每 {分钟} 分钟"（按分钟取整）。
  - `ICronTrigger`：`CronExpressionString` → 原样返回（前缀"Cron: "）。
  - 其它/未知 → "自定义"。
- 为避免直接依赖 Quartz 类型难以单测，纯函数签名以**已提取的原始值**为入参：`Describe(bool isCron, string cronExpr, int? simpleIntervalMinutes)`，返回描述字符串。Service 里先从 trigger 提取这些原始值再调用。这样纯函数可特征化测试，Quartz I/O 留在 service。

### 3.3 DTO
`SyncJobOverview`（带 `[JsonPropertyName]` camelCase）：
- `type`(string)、`name`(string)、`scheduled`(bool)、`scheduleDesc`(string)、
  `nextFireTime`(DateTime?)、`prevFireTime`(DateTime?)、`triggerState`(string)、
  `running`(bool)、`downloaded`(int)、`failed`(int)、`currentTitle`(string)、`endedAt`(DateTime?)。

### 3.4 前端：定时任务管理页
- `app/src/pages/syncstatus/index.vue` 改为任务管理页（不再仅包 `SyncStatusPanel`）：
  - 顶部：全局「立即同步(全部)」「停止(全部)」按钮（互斥逻辑同现状：运行中禁用立即同步、空闲禁用停止）。
  - `a-table`：列 类型 | 周期 | 下次执行 | 状态 | 最近结果 | 操作。
    - 状态：运行中（tag processing）/ 已调度（default）/ 未启用（灰）。
    - 最近结果：`下载 N · 失败 M`（运行中显示当前视频；已完成显示完成时间）。
    - 操作：「立即执行」按钮（调 `TriggerSyncNow?type=该类型`），运行中时禁用。
  - 每 3 秒轮询**两个**单一职责接口：`SyncJobs`（驱动表格各行）+ `SyncStatus`（取全局 `running` 驱动顶部按钮互斥、取 `recentLogs` 驱动底部日志）。两个都是轻量 GET，不合并、各司其职。
  - 详情抽屉：点行打开，显示周期表达式、下次/上次执行、触发器状态、最近一轮结果。
  - 底部"最近同步日志"：用 `SyncStatus.recentLogs`（全局日志）。
- `coreapi.ts` 新增 `SyncJobs()`（GET /api/config/SyncJobs）。`SyncStatus`/`StopSyncNow`/`TriggerSyncNow` 已有。
- 路由 `routes.ts`：把 `/syncstatus` 的 `name` 由「同步状态」改为「定时任务」（图标可沿用 `PlayCircleOutlined` 或换 `ScheduleOutlined`）。

### 3.5 组件复用
- `SyncStatusPanel.vue` 仍被 `MobileDashboard` 使用，**保持不动**（本期不碰移动端）。桌面 `/syncstatus` 页不再用它，改用新表格。

## 4. 测试
- 后端：周期描述纯函数 `Describe(isCron, cronExpr, simpleIntervalMinutes)` 的特征化测试（simple→"每N分钟"、cron→"Cron: ..."、未知→"自定义"）。
- Quartz I/O 组装逻辑不单测（与现有 I/O 不单测约定一致）。
- 前端：CI 的 vite build 验证编译。
- `tests/README.md` 增加该 helper 覆盖条目。

## 5. 取舍（YAGNI）
- 本期只读 + 单类型触发；改周期、执行历史、单类型停止均留后续期。
- 时间统一转服务器本地时间返回字符串/DateTime，前端 `toLocaleString` 展示。
- 详情抽屉而非独立子页。
- 菜单名改「定时任务」，路由不变。

## 6. 影响文件清单（预计）
- 新增：`utils/SyncJobScheduleDescriber.cs`（纯描述函数）、`tests/dy.net.Tests/SyncJobScheduleDescriberTests.cs`
- 修改：`service/DouyinQuartzJobService.cs`（GetJobsOverviewAsync + SyncJobOverview DTO，或 DTO 放独立文件）、
  `Controllers/ConfigController.cs`（SyncJobs 接口）、
  `app/src/store/coreapi.ts`（SyncJobs）、`app/src/pages/syncstatus/index.vue`（任务管理页）、
  `app/src/router/routes.ts`（菜单名）、`tests/README.md`
- 不动：`SyncStatusPanel.vue`、`MobileDashboard.vue`、`RecordTable.vue`、`SyncRunState.cs`
