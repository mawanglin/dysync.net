# 同步任务：停止 + 立即同步互斥 + 详细实时进度 —— 设计规格

- 日期：2026-06-23
- 分支：improve/dysync-refactor
- 状态：已与用户确认，待实现

## 1. 背景与目标

已有「立即同步」按钮（`/api/config/TriggerSyncNow`，触发全部视频下载类作业各跑一次）。
本次新增三项能力：

1. **停止同步**：中止当前正在执行的同步。
2. **立即同步 ↔ 停止 的互斥**：有任务在跑时只能停止、不能再触发；无任务时只能触发、不能停止。前后端都要校验。
3. **详细实时进度**：任务执行时可查看当前执行情况（详细级别）。

同步实际由 5 个独立的 Quartz 作业承担：收藏 `dy_collects`、喜欢 `dy_favorite`、
关注作品 `dy_follows`、合集 `dy_mix`、短剧 `dy_series`。定时周期到点时它们几乎同时各自触发；
「立即同步」也是一次性触发全部 5 个。

## 2. 已确认的需求语义

### 2.1 停止（单按钮，无"暂停"）
- 语义：**下完当前正在下载的这一个视频后，即中止本轮整个同步**（协作式取消）。
- 恢复方式：用户手动再点「立即同步」，或等下一个定时周期自动重跑。
- 不需要断点续传：系统已有去重（`GetByAwemeId` + 文件存在即跳过），重新跑会自动跳过已下载的，
  等于"接着没下完的继续"。
- 不暂停定时调度：停止只影响"当前这一轮"，下个定时周期照常自动运行。

### 2.2 互斥状态机（批次粒度）
将 5 类作业视为一个「同步批次」：
- **有任务在跑**（任意一类在执行，无论手动还是定时触发）→ 立即同步禁用、停止可用。
- **无任务在跑** → 停止禁用、立即同步可用。
- 前端按钮置灰 + **后端接口同样校验并拒绝非法调用**（防止绕过前端直接调接口）。

### 2.3 详细进度（C 级）
- 顶部：`同步进行中 / 空闲`、开始时间、已运行时长。
- 每个正在跑的类型一行：`类型名 · 本轮已下载 N / 页内总数 · 失败 M · 当前：<正在下载的视频标题>`。
- 底部：最近若干条下载事件日志，滚动刷新。
- 前端每 2~3 秒轮询刷新；任务结束自动回到"空闲"，按钮互斥随之切换。

## 3. 架构设计

### 3.1 共享运行状态单例 `SyncRunState`（内存，DI 单例）

所有同步作业与控制器共用的中枢。**不持久化**（同步状态是瞬时的，容器重启清零）。

字段：
- `IsAnyRunning`（派生）：任意类型处于运行中即为 true。
- `ConcurrentDictionary<VideoTypeEnum, TypeProgress>`，`TypeProgress` 含：
  - `Status`（Running / Idle）
  - `StartedAt`
  - `Downloaded`（本轮成功下载数）
  - `Failed`（本轮失败数）
  - `PageTotal`（当前页条数，best-effort 进度分母）
  - `CurrentTitle`（当前正在下载的视频标题）
  - `CookieName`
- `CancellationTokenSource _cts`：一个批次一个。
- 最近日志环形缓冲 `RingBuffer<LogLine>`（容量固定，如 50 条）。
- 私有锁 `_gate` 保护启停/令牌重建的原子性。

方法：
- `RegisterStart(type, cookieName)`：加锁；若当前无任何类型在跑（新批次开始）→ Dispose 旧 `_cts`、新建 `_cts`；置该类型 Running、StartedAt=now、清零计数。
- `RegisterFinish(type)`：加锁；置该类型 Idle（保留最后一轮计数用于显示，直到下一批次开始时被新 StartedAt 覆盖）。
- `SetPageTotal(type, n)` / `UpdateCurrentVideo(type, title)` / `OnDownloaded(type, ok)`（递增 Downloaded/Failed + 追加一条日志）。
- `Token`：返回当前 `_cts.Token`（无则 `CancellationToken.None`）。
- `bool RequestStop()`：加锁；若 `!IsAnyRunning` 返回 false；否则 `_cts.Cancel()` 返回 true。
- `TryBeginManualTrigger()`：加锁；若 `IsAnyRunning` 或处于"启动中"短时闸 → 返回 false；否则置短时"启动中"标志（防双击竞态）返回 true。
- `GetSnapshot()`：返回供状态接口序列化的快照 DTO。

并发与令牌生命周期：
- 5 个作业可能并发 `RegisterStart`；用 `_gate` 保证"无人在跑时才重建令牌"不产生竞态。
- 停止后各作业观察到取消、下完当前视频、退出并 `RegisterFinish`；全部结束后下一次 `RegisterStart` 才会重建新令牌，保证下一轮不被旧的已取消令牌污染。

### 3.2 作业协作式取消（改基类 `DouyinBasicSyncJob`，5 子类自动继承）

构造注入 `SyncRunState`。在天然检查点检查取消：
- `Execute`：try/finally 包裹 → `RegisterStart(VideoType, ...)` / `RegisterFinish(VideoType)`。
- `GetAndSaveViedos` 翻页 `while (hasMore)`：每页开始前、处理完一页后（进入随机延迟前）检查取消 → break。
- `ProcessVideoList` 的 `foreach (item in data.AwemeList)`：**每下完一条视频后**检查取消 → break
  （实现"下完当前视频再停"）；同时 `UpdateCurrentVideo` / `OnDownloaded` / `SetPageTotal`。
- `Task.Delay(...)` 改为可被取消打断（传入 token 或在等待点检查）。

> 取消导致的循环提前结束属于正常控制流，不应被 `ProcessSyncUserCookie` 的 try/catch 记成 `同步出错`；
> 实现时对 `OperationCanceledException` 单独处理为"已停止"日志（Debug 级），不计入错误。

### 3.3 后端接口（`ConfigController`，沿用类级 `[Authorize]`）

| 接口 | 方法 | 行为与校验 |
|---|---|---|
| `/api/config/TriggerSyncNow`（已有，补校验） | GET | `IsAnyRunning` 或 `!TryBeginManualTrigger()` → 拒绝 `code=-1,"已有同步任务在执行"`；否则触发全部视频任务 |
| `/api/config/StopSyncNow`（新增） | GET | `!IsAnyRunning` → 拒绝 `"当前没有正在执行的同步任务"`；否则 `RequestStop()` → 成功 |
| `/api/config/SyncStatus`（新增） | GET | 返回 `GetSnapshot()`：`{running, startedAt, elapsedSec, types:[{type,name,downloaded,failed,pageTotal,currentTitle,cookieName}], recentLogs:[{time,text}]}` |

### 3.4 前端（`app/src/store/coreapi.ts` + `app/src/pages/workplace/RecordTable.vue`）

- `coreapi.ts` 新增 `StopSyncNow()`、`SyncStatus()`。
- 「立即同步」按钮旁新增「停止」按钮。
- 轮询：进入页面起每 2~3 秒调 `SyncStatus`：
  - 用 `running` 驱动两个按钮的 `disabled`（互斥）。
  - 刷新详细进度面板。
- 详细进度面板（C）：一张卡片/抽屉：
  - 顶部状态行（进行中/空闲 + 开始时间 + 已运行时长）。
  - 每个正在跑类型一行（类型 · 已下载/页内总数 · 失败 · 当前视频标题）。
  - 底部最近日志列表，滚动刷新。
- 触发/停止动作后立即拉一次状态，使按钮即时切换。

## 4. 测试

- `SyncRunStateTests`（特征化，纯内存状态机）：
  - `RegisterStart` 后 `IsAnyRunning` 为 true；全部 `RegisterFinish` 后为 false。
  - 空闲时 `RequestStop()` 返回 false；运行时返回 true 且令牌被取消。
  - 多类型并发启停：第一个 Start 建令牌，最后一个 Finish 后再次 Start 会重建新令牌。
  - `TryBeginManualTrigger`：运行中返回 false。
  - `OnDownloaded` 正确累加 Downloaded/Failed；快照内容正确。
- I/O 循环里的取消检查点不写单测（与现有 CreateSaveFolder 等 I/O 不单测的约定一致）。
- 文档：在 `tests/README.md` 特征化覆盖表新增 `SyncRunStateTests` 条目。

## 5. 取舍（YAGNI）

- 进度分母用"当前页条数"（接口不便宜地给总数；`OnlySyncNew` 下本就只拉第一页，够用且诚实）。
- 不做暂停 / 断点续传（去重已能接力；价值低）。
- 停止不暂停定时调度（用户明确要"下个周期可自动重跑"）。
- 运行状态内存存储，不持久化（瞬时状态，重启清零可接受）。
- 批次粒度（A）：5 类作业当一个开关；后续如需按类型分别控制再衍生。

## 6. 影响文件清单（预计）

- 新增：`utils/SyncRunState.cs`（或 `service/SyncRunState.cs`）、`tests/dy.net.Tests/SyncRunStateTests.cs`
- 修改：`extension/ServiceExtension.cs`（注册单例）、`job/DouyinBasicSyncJob.cs`（注入+取消+进度）、
  `Controllers/ConfigController.cs`（Stop/Status 接口 + Trigger 校验）、
  `app/src/store/coreapi.ts`、`app/src/pages/workplace/RecordTable.vue`、`tests/README.md`
- 5 个子类作业构造函数需透传新依赖（若基类构造签名变化）。
