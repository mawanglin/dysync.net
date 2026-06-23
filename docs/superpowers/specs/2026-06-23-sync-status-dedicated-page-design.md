# 独立「同步状态」菜单页 + 移动端对齐 —— 设计规格

- 日期：2026-06-23
- 分支：improve/dysync-refactor
- 状态：已与用户确认，待实现
- 前置：依赖已上线的 `service/SyncRunState.cs` 与 `/api/config/{TriggerSyncNow,StopSyncNow,SyncStatus}`
  （见 `2026-06-23-sync-stop-and-progress-design.md`）

## 1. 背景与目标

现有进度面板内联在「同步记录」页，且 `v-if="running"` 只在跑的时候出现，可发现性差。
本次新增一个**独立的左侧菜单页「同步状态」**作为同步监控/控制中心，并在移动端单页同步对齐，
让"正在执行的任务"随时一眼可达，空闲时也展示上一轮同步摘要。

## 2. 已确认决策

1. 新页面 = **自包含控制中心**（立即同步 + 停止 + 详细进度 + 日志 + 空闲态摘要）。
2. 「同步记录」页：**移除**内联进度面板，**保留**立即同步/停止按钮及其轮询（按钮互斥仍需要）。
3. 移动端：在 `MobileDashboard.vue` 也嵌入同一套实时状态（含停止）。
4. 空闲态显示**上一轮同步的每类型明细**（不是只存总数）——即运行视图的"冻结版"。

## 3. 架构

### 3.1 复用组件 `app/src/components/SyncStatusPanel.vue`（核心，单一职责）
三处复用（桌面新页 / 移动端 / 未来需要处），自包含：
- **数据**：自身每 2.5s 轮询 `/api/config/SyncStatus`；`onMounted` 起轮询、`onBeforeUnmount` 清。
- **运行中**：顶部「同步进行中 · 已运行 N 秒」；每类型一行（类型名 · 本轮已下载 N 条 · 失败 M · 账号 · 当前视频标题）；底部最近日志滚动列表。
- **空闲**：「当前空闲」+ 若有上轮摘要则显示「上次同步 · 结束于 X」及每类型一行（类型名 · 下载 N · 失败 M）。
- **控件**：prop `showControls?: boolean`（默认 false）。为 true 时内置「立即同步」「停止」按钮，互斥与提示逻辑同既有（运行中禁用立即同步、空闲禁用停止；动作后立即刷新一次状态）。
- 不接收外部状态，纯靠接口；调用方只决定是否显示控件。

### 3.2 桌面端新菜单 + 页面
- `app/src/router/routes.ts` 新增一条路由，放在「数据看板」之后、「同步记录」之前：
  ```
  { path: '/syncstatus', name: '同步状态',
    meta: { icon: 'PlayCircleOutlined', view: 'self', target: '_self', renderMenu: true, cacheable: false },
    component: () => import('@/pages/syncstatus/index.vue') }
  ```
- 新页面 `app/src/pages/syncstatus/index.vue`：仅包一层 `<SyncStatusPanel :show-controls="true" />`。

### 3.3 移动端对齐
- `app/src/pages/mobile/MobileDashboard.vue`：在合适位置嵌入 `<SyncStatusPanel :show-controls="true" />`。
  紧凑展示即可，不为移动端单独写一套逻辑。

### 3.4 「同步记录」页瘦身
- `app/src/pages/workplace/RecordTable.vue`：删除内联进度 `<a-card v-if="syncStatus.running">` 面板块；
  **保留**「立即同步 / 停止」按钮、`fetchSyncStatus` 轮询、`syncStatus`/`isStopping` 等（按钮互斥依赖）。
  其它逻辑不动。

### 3.5 后端：`SyncRunState` 增加"上轮摘要"
- 新增内部字段记录最近一轮结束摘要：结束时间 + **每类型冻结明细**（类型、名称、下载数、失败数、账号）。
- 触发时机：`RegisterFinish` 执行后，若已无任何类型在运行（批次刚结束），把当前各类型的计数冻结为 `lastRun`
  （结束时间 = 此刻）。`RegisterStart` 开新批次时不清除 `lastRun`（保留给空闲展示），新批次结束时再覆盖。
- `GetSnapshot` 输出新增 `lastRun`：`{ endedAt, types: [{type,name,downloaded,failed,cookieName}] }`，无则为 null。
  所有新 DTO 属性带 `[JsonPropertyName(camelCase)]`，与项目约定一致。
- 前端：`SyncStatusPanel` 空闲分支读取 `snapshot.lastRun`。

## 4. 数据流

```
作业(Execute) → SyncRunState.RegisterStart/OnDownloaded/RegisterFinish
                         │ 批次结束冻结 lastRun
SyncStatusPanel ─轮询→ GET /api/config/SyncStatus → GetSnapshot(running? 实时 : lastRun)
                         │
                         └ showControls 时：点击 → TriggerSyncNow / StopSyncNow → 立即再轮询一次
```

## 5. 测试
- 后端 `SyncRunStateTests` 增加用例：
  - 一轮结束后 `GetSnapshot().lastRun` 含每类型明细（下载/失败正确）、`endedAt` 有值、`running=false`。
  - 多类型批次：全部 finish 后 lastRun 汇总每类型；开新批次时 lastRun 仍保留，直到该批次结束被覆盖。
- 前端无单元测试；由 CI 的 Docker `vite build` 验证可编译。
- `tests/README.md` 更新 `SyncRunState` 覆盖描述（追加 lastRun）。

## 6. 取舍（YAGNI）
- `lastRun` 每类型明细只存 类型/名称/下载/失败/账号，不存当前视频标题（空闲无意义）与日志。
- `lastRun` 内存存储，重启清零。
- 移动端复用同一组件，不单独定制交互。
- 菜单名「同步状态」、图标 `PlayCircleOutlined`、位置在「数据看板」之后——均可后续微调。
- 记录页"只删面板、保留按钮"，不顺带把按钮也抽进共享组件（控制改动面）。

## 7. 影响文件清单（预计）
- 新增：`app/src/components/SyncStatusPanel.vue`、`app/src/pages/syncstatus/index.vue`
- 修改：`app/src/router/routes.ts`（菜单路由）、`app/src/pages/mobile/MobileDashboard.vue`（嵌入）、
  `app/src/pages/workplace/RecordTable.vue`（删面板、保留按钮）、
  `service/SyncRunState.cs`（lastRun + 快照）、`tests/dy.net.Tests/SyncRunStateTests.cs`（lastRun 用例）、
  `tests/README.md`
- 可选：`app/src/store/coreapi.ts` 已有 `SyncStatus/StopSyncNow/TriggerSyncNow`，无需新增接口。
