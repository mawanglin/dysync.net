# 同步停止 + 立即同步互斥 + 详细实时进度 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 给抖音同步加「停止」、让「立即同步」与「停止」互斥（前后端双向校验），并提供详细的实时进度查看。

**架构：** 新增一个内存单例 `SyncRunState` 作为运行状态中枢——记录每类型进度、最近日志、一个批次取消令牌。5 个 Quartz 同步作业（基类 `DouyinBasicSyncJob`）在 `Execute` 里自报启停、在下载循环检查点协作式取消、并实时写进度。控制器新增 `StopSyncNow`/`SyncStatus` 接口并给 `TriggerSyncNow` 加"已在运行则拒绝"的校验。前端轮询状态接口驱动按钮互斥与进度面板。

**技术栈：** .NET 8 / ASP.NET Core、Quartz、xUnit、Vue 3 + Ant Design Vue、pnpm。

**规格：** `docs/superpowers/specs/2026-06-23-sync-stop-and-progress-design.md`

**测试运行方式（本环境 net8.0 主机缺失，需 roll-forward）：**
`DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`

---

## 文件结构

| 文件 | 职责 | 动作 |
|---|---|---|
| `service/SyncRunState.cs` | 运行状态单例：每类型进度、最近日志、批次取消令牌、启停/快照方法 | 创建 |
| `tests/dy.net.Tests/SyncRunStateTests.cs` | `SyncRunState` 状态机特征化测试 | 创建 |
| `Program.cs` | 注册 `SyncRunState` 单例 | 修改 |
| `job/DouyinBasicSyncJob.cs` | 注入 `SyncRunState`；`Execute` 自报启停；循环检查点取消；写进度 | 修改 |
| `job/DouYinCollectSyncJob.cs` 等 5 个子类 | 构造函数透传新依赖 | 修改 |
| `Controllers/ConfigController.cs` | 注入 `SyncRunState`；新增 Stop/Status 接口；Trigger 加校验 | 修改 |
| `app/src/store/coreapi.ts` | 新增 `StopSyncNow()`、`SyncStatus()` | 修改 |
| `app/src/pages/workplace/RecordTable.vue` | 「停止」按钮、轮询、按钮互斥、进度面板 | 修改 |
| `tests/README.md` | 特征化覆盖表加 `SyncRunStateTests` | 修改 |

---

## 任务 1：`SyncRunState` 单例（核心状态机，TDD）

**文件：**
- 创建：`service/SyncRunState.cs`
- 测试：`tests/dy.net.Tests/SyncRunStateTests.cs`

- [ ] **步骤 1：创建 `SyncRunState.cs`（先写实现，因为测试要引用其类型；随后步骤补测试并验证行为）**

创建 `service/SyncRunState.cs`：

```csharp
using System.Text.Json.Serialization;   // [JsonPropertyName]，与项目响应 DTO 约定一致（显式 camelCase，不依赖全局策略）
using dy.net.extension;       // VideoTypeEnum.GetDesc()
using dy.net.model.dto;       // VideoTypeEnum

namespace dy.net.service
{
    /// <summary>
    /// 同步运行状态中枢（内存单例，不持久化）。
    /// 5 类同步作业共用：自报启停、写进度、协作式取消、供状态接口快照。
    /// 所有可变状态读写都在 _gate 锁内，保证 5 作业并发安全。
    /// 时间统一由调用方传入（now），便于单元测试且避免散落 DateTime.Now。
    /// </summary>
    public class SyncRunState
    {
        private readonly object _gate = new();
        private readonly Dictionary<VideoTypeEnum, TypeProgress> _types = new();
        private readonly Queue<SyncLogSnapshot> _logs = new();
        private CancellationTokenSource _cts;
        private DateTime? _manualTriggerAt;   // 防双击/防并发触发的短时闸
        private const int MaxLogs = 50;
        private static readonly TimeSpan ManualTriggerGuard = TimeSpan.FromSeconds(15);

        /// <summary>任意类型正在运行即为 true。</summary>
        public bool IsAnyRunning
        {
            get { lock (_gate) { return _types.Values.Any(t => t.Running); } }
        }

        /// <summary>当前批次的取消令牌（无批次时为 None）。作业应在循环检查点读取。</summary>
        public CancellationToken Token
        {
            get { lock (_gate) { return _cts?.Token ?? CancellationToken.None; } }
        }

        /// <summary>手动触发前调用：已在运行或处于短时闸内则返回 false（拒绝触发）。</summary>
        public bool TryBeginManualTrigger(DateTime now)
        {
            lock (_gate)
            {
                if (_types.Values.Any(t => t.Running)) return false;
                if (_manualTriggerAt.HasValue && now - _manualTriggerAt.Value < ManualTriggerGuard) return false;
                _manualTriggerAt = now;
                return true;
            }
        }

        /// <summary>作业开始：批次首个作业会重建取消令牌。</summary>
        public void RegisterStart(VideoTypeEnum type, string cookieName, DateTime now)
        {
            lock (_gate)
            {
                bool firstOfBatch = !_types.Values.Any(t => t.Running);
                if (firstOfBatch)
                {
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                    _manualTriggerAt = null;   // 批次已真正开始，清闸
                }
                _types[type] = new TypeProgress
                {
                    Running = true,
                    StartedAt = now,
                    CookieName = cookieName ?? "",
                    Downloaded = 0,
                    Failed = 0,
                    PageTotal = 0,
                    CurrentTitle = ""
                };
            }
        }

        public void RegisterFinish(VideoTypeEnum type)
        {
            lock (_gate)
            {
                if (_types.TryGetValue(type, out var p))
                {
                    p.Running = false;
                    p.CurrentTitle = "";
                }
            }
        }

        public void SetCurrentCookie(VideoTypeEnum type, string cookieName)
        {
            lock (_gate) { if (_types.TryGetValue(type, out var p)) p.CookieName = cookieName ?? ""; }
        }

        public void SetPageTotal(VideoTypeEnum type, int total)
        {
            lock (_gate) { if (_types.TryGetValue(type, out var p)) p.PageTotal = total; }
        }

        public void UpdateCurrentVideo(VideoTypeEnum type, string title)
        {
            lock (_gate) { if (_types.TryGetValue(type, out var p)) p.CurrentTitle = title ?? ""; }
        }

        public void OnDownloaded(VideoTypeEnum type, bool ok, string title, DateTime now)
        {
            lock (_gate)
            {
                if (_types.TryGetValue(type, out var p))
                {
                    if (ok) p.Downloaded++; else p.Failed++;
                }
                _logs.Enqueue(new SyncLogSnapshot
                {
                    Time = now,
                    Text = $"[{type.GetDesc()}]{(ok ? "完成" : "失败")}：{title}"
                });
                while (_logs.Count > MaxLogs) _logs.Dequeue();
            }
        }

        /// <summary>请求停止当前批次：无运行返回 false；否则取消令牌返回 true。</summary>
        public bool RequestStop()
        {
            lock (_gate)
            {
                if (!_types.Values.Any(t => t.Running)) return false;
                _cts?.Cancel();
                return true;
            }
        }

        public SyncStatusSnapshot GetSnapshot(DateTime now)
        {
            lock (_gate)
            {
                bool running = _types.Values.Any(t => t.Running);
                DateTime? startedAt = _types.Values
                    .Where(t => t.Running)
                    .Select(t => (DateTime?)t.StartedAt)
                    .OrderBy(t => t)
                    .FirstOrDefault();
                return new SyncStatusSnapshot
                {
                    Running = running,
                    StartedAt = startedAt,
                    ElapsedSec = startedAt.HasValue ? (int)(now - startedAt.Value).TotalSeconds : 0,
                    Types = _types
                        .Where(kv => kv.Value.Running)
                        .Select(kv => new TypeProgressSnapshot
                        {
                            Type = kv.Key.ToString(),
                            Name = kv.Key.GetDesc(),
                            Downloaded = kv.Value.Downloaded,
                            Failed = kv.Value.Failed,
                            PageTotal = kv.Value.PageTotal,
                            CurrentTitle = kv.Value.CurrentTitle,
                            CookieName = kv.Value.CookieName
                        })
                        .ToList(),
                    RecentLogs = _logs.Reverse().ToList()   // 最新在前
                };
            }
        }

        private class TypeProgress
        {
            public bool Running;
            public DateTime StartedAt;
            public int Downloaded;
            public int Failed;
            public int PageTotal;
            public string CurrentTitle = "";
            public string CookieName = "";
        }
    }

    public class SyncStatusSnapshot
    {
        [JsonPropertyName("running")] public bool Running { get; set; }
        [JsonPropertyName("startedAt")] public DateTime? StartedAt { get; set; }
        [JsonPropertyName("elapsedSec")] public int ElapsedSec { get; set; }
        [JsonPropertyName("types")] public List<TypeProgressSnapshot> Types { get; set; } = new();
        [JsonPropertyName("recentLogs")] public List<SyncLogSnapshot> RecentLogs { get; set; } = new();
    }

    public class TypeProgressSnapshot
    {
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("downloaded")] public int Downloaded { get; set; }
        [JsonPropertyName("failed")] public int Failed { get; set; }
        [JsonPropertyName("pageTotal")] public int PageTotal { get; set; }
        [JsonPropertyName("currentTitle")] public string CurrentTitle { get; set; }
        [JsonPropertyName("cookieName")] public string CookieName { get; set; }
    }

    public class SyncLogSnapshot
    {
        [JsonPropertyName("time")] public DateTime Time { get; set; }
        [JsonPropertyName("text")] public string Text { get; set; }
    }
}
```

- [ ] **步骤 2：编写失败的测试**

创建 `tests/dy.net.Tests/SyncRunStateTests.cs`：

```csharp
using dy.net.model.dto;
using dy.net.service;

namespace dy.net.Tests
{
    public class SyncRunStateTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 6, 23, 10, 0, 0);

        [Fact]
        public void Idle_initially_notRunning_and_stop_returns_false()
        {
            var s = new SyncRunState();
            Assert.False(s.IsAnyRunning);
            Assert.False(s.RequestStop());          // 空闲时停止无效
            Assert.False(s.Token.IsCancellationRequested);
        }

        [Fact]
        public void RegisterStart_marks_running_and_TryBeginManual_rejected_while_running()
        {
            var s = new SyncRunState();
            Assert.True(s.TryBeginManualTrigger(T0));        // 空闲可触发
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            Assert.True(s.IsAnyRunning);
            Assert.False(s.TryBeginManualTrigger(T0));       // 运行中拒绝再次触发
        }

        [Fact]
        public void RequestStop_cancels_token_while_running()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_favorite, "Zoe", T0);
            var token = s.Token;
            Assert.True(s.RequestStop());
            Assert.True(token.IsCancellationRequested);
        }

        [Fact]
        public void New_batch_after_all_finished_rebuilds_token()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            var first = s.Token;
            s.RequestStop();
            s.RegisterFinish(VideoTypeEnum.dy_collects);
            Assert.False(s.IsAnyRunning);

            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0.AddMinutes(30));
            var second = s.Token;
            Assert.False(second.IsCancellationRequested);    // 新批次令牌未被取消
            Assert.NotEqual(first, second);
        }

        [Fact]
        public void Concurrent_types_running_flag_tracks_any()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.RegisterStart(VideoTypeEnum.dy_favorite, "Zoe", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects);
            Assert.True(s.IsAnyRunning);                     // 喜欢仍在跑
            s.RegisterFinish(VideoTypeEnum.dy_favorite);
            Assert.False(s.IsAnyRunning);
        }

        [Fact]
        public void OnDownloaded_accumulates_and_snapshot_reflects_progress()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.SetPageTotal(VideoTypeEnum.dy_collects, 18);
            s.UpdateCurrentVideo(VideoTypeEnum.dy_collects, "视频A");
            s.OnDownloaded(VideoTypeEnum.dy_collects, true, "视频A", T0);
            s.OnDownloaded(VideoTypeEnum.dy_collects, false, "视频B", T0);

            var snap = s.GetSnapshot(T0.AddSeconds(5));
            Assert.True(snap.Running);
            Assert.Equal(5, snap.ElapsedSec);
            var t = Assert.Single(snap.Types);
            Assert.Equal(1, t.Downloaded);
            Assert.Equal(1, t.Failed);
            Assert.Equal(18, t.PageTotal);
            Assert.Equal(2, snap.RecentLogs.Count);
            Assert.Contains("视频B", snap.RecentLogs[0].Text);   // 最新在前
        }

        [Fact]
        public void Snapshot_when_idle_has_no_running_types()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects);
            var snap = s.GetSnapshot(T0.AddSeconds(1));
            Assert.False(snap.Running);
            Assert.Empty(snap.Types);
            Assert.Equal(0, snap.ElapsedSec);
        }
    }
}
```

- [ ] **步骤 3：运行测试验证通过**

运行：`DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`
预期：全部通过（既有 177 + 新增 7 = 184）。若失败，按断言修正 `SyncRunState`。

- [ ] **步骤 4：Commit**

```bash
git add service/SyncRunState.cs tests/dy.net.Tests/SyncRunStateTests.cs
git commit -m "feat(sync): 新增 SyncRunState 运行状态单例（进度/取消/快照）+ 特征化测试"
```

---

## 任务 2：注册单例

**文件：**
- 修改：`Program.cs`（`ConfigureServices` 内，`services.AddControllers(...)` 之后）

- [ ] **步骤 1：注册 `SyncRunState` 为单例**

在 `Program.cs` 中 `}).AddGlobalExceptionFilter();` 这一行之后新增一行：

```csharp
            // 同步运行状态中枢（所有同步作业与控制器共用，单例）
            services.AddSingleton<dy.net.service.SyncRunState>();
```

- [ ] **步骤 2：构建验证**

运行：`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`
预期：0 错误。

- [ ] **步骤 3：Commit**

```bash
git add Program.cs
git commit -m "feat(sync): DI 注册 SyncRunState 单例"
```

---

## 任务 3：基类作业注入 + 自报启停 + 协作式取消 + 写进度

**文件：**
- 修改：`job/DouyinBasicSyncJob.cs`

- [ ] **步骤 1：基类增加字段与构造参数**

把构造函数（当前最后一个参数是 `DouyinCollectCateService douyinCollectCateService`）改为追加 `SyncRunState syncRunState` 参数并赋值。

将这段：

```csharp
        private readonly DouyinCollectCateService douyinCollectCateService;
```

改为（在其后追加字段）：

```csharp
        private readonly DouyinCollectCateService douyinCollectCateService;
        /// <summary>同步运行状态中枢（进度/取消）</summary>
        protected readonly SyncRunState syncRunState;
```

将构造函数签名与赋值：

```csharp
            DouyinMergeVideoService douyinMergeVideoService,
            DouyinCollectCateService douyinCollectCateService)
        {
```

改为：

```csharp
            DouyinMergeVideoService douyinMergeVideoService,
            DouyinCollectCateService douyinCollectCateService,
            SyncRunState syncRunState)
        {
            this.syncRunState = syncRunState;
```

（`using dy.net.service;` 基类已存在，无需新增。）

- [ ] **步骤 2：`Execute` 自报启停（try/finally）**

将 `Execute` 中遍历 cookie 的循环：

```csharp
            // 遍历每个有效的Cookie，执行同步
            foreach (var cookie in cookies)
            {
                await ProcessSyncUserCookie(cookie, config);
            }
        }
```

改为：

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
                syncRunState.RegisterFinish(VideoType);
            }
        }
```

- [ ] **步骤 3：`GetAndSaveViedos` 翻页循环加取消检查**

将 `while (hasMore)` 循环体开头：

```csharp
            // 循环获取视频数据
            while (hasMore)
            {
                // 获取视频数据
                var data = await FetchVideoData(cookie, cursor, followed, cate);
```

改为：

```csharp
            // 循环获取视频数据
            while (hasMore)
            {
                // 协作式取消：用户点了停止则中止翻页（当前已下载的保留）
                if (syncRunState.Token.IsCancellationRequested) break;
                // 获取视频数据
                var data = await FetchVideoData(cookie, cursor, followed, cate);
```

将循环末尾的随机等待：

```csharp
                //随机等待
                await Task.Delay(_random.Next(2, 10) * 1000);
            }
```

改为（等待可被停止打断）：

```csharp
                //随机等待（可被停止打断）
                try
                {
                    await Task.Delay(_random.Next(2, 10) * 1000, syncRunState.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
```

- [ ] **步骤 4：`ProcessVideoList` 写进度 + 每条后检查取消**

将方法体开头（`foreach (var item in data.AwemeList)` 之前）：

```csharp
            int syncCount = 0;
            var videos = new List<DouyinVideo>();
            foreach (var item in data.AwemeList)
            {
```

改为：

```csharp
            int syncCount = 0;
            var videos = new List<DouyinVideo>();
            syncRunState.SetPageTotal(VideoType, data.AwemeList.Count);
            foreach (var item in data.AwemeList)
            {
                // 协作式取消：下完当前视频后、开始下一条前停止
                if (syncRunState.Token.IsCancellationRequested) break;
                syncRunState.UpdateCurrentVideo(VideoType, item.Desc);
```

将主下载路径（`var video = await ProcessSingleVideo(...)` 处，当前约 569-573）：

```csharp
                var video = await ProcessSingleVideo(cookie, item, config, followed, cate);
                if (video != null)
                {
                    videos.Add(video);
                    syncCount++;
```

改为：

```csharp
                var video = await ProcessSingleVideo(cookie, item, config, followed, cate);
                if (video != null)
                {
                    videos.Add(video);
                    syncCount++;
                    syncRunState.OnDownloaded(VideoType, true, item.Desc, DateTime.Now);
```

- [ ] **步骤 5：`ProcessSyncUserCookie` 吞掉取消异常（不记为错误）**

`ProcessSyncUserCookie` 末尾的 catch 当前为：

```csharp
            catch (Exception ex)
            {
                Log.Error(ex, $"[{cookie.UserName}][{VideoType.GetDesc()}]同步出错!!!,{ex.StackTrace}");
            }
```

改为（先捕获取消，按"已停止"处理）：

```csharp
            catch (OperationCanceledException)
            {
                Log.Debug($"[{cookie.UserName}][{VideoType.GetDesc()}]已停止同步（用户触发）");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{cookie.UserName}][{VideoType.GetDesc()}]同步出错!!!,{ex.StackTrace}");
            }
```

- [ ] **步骤 6：构建（此时会因 5 个子类构造未更新而报错，预期，下一任务修）**

运行：`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`
预期：报错——5 个子类的 `base(...)` 调用参数数量不匹配。继续任务 4。

---

## 任务 4：5 个子类构造函数透传新依赖

**文件：**
- 修改：`job/DouYinCollectSyncJob.cs`、`job/DouYinFavoritSyncJob.cs`、`job/DouyinFollowedSyncJob.cs`、`job/DouyinMixSyncJob.cs`、`job/DouyinSeriesSyncJob.cs`

5 个子类构造函数签名结构相同：末尾参数都是 `DouyinCollectCateService douyinCollectCateService`，`base(...)` 末尾都是 `douyinCollectCateService`。对每个文件做相同变换：在构造参数末尾追加 `, SyncRunState syncRunState`，在 `base(...)` 末尾追加 `, syncRunState`。各文件已 `using dy.net.service;`，无需新增。

- [ ] **步骤 1：改 `DouYinCollectSyncJob.cs`**

将：
```csharp
        public DouyinCollectSyncJob(DouyinCookieService douyinCookieService, DouyinHttpClientService douyinHttpClientService, DouyinVideoService douyinVideoService, DouyinCommonService douyinCommonService, DouyinFollowService douyinFollowService, DouyinMergeVideoService douyinMergeVideoService, DouyinCollectCateService douyinCollectCateService) : base(douyinCookieService, douyinHttpClientService, douyinVideoService, douyinCommonService, douyinFollowService, douyinMergeVideoService, douyinCollectCateService)
```
改为：
```csharp
        public DouyinCollectSyncJob(DouyinCookieService douyinCookieService, DouyinHttpClientService douyinHttpClientService, DouyinVideoService douyinVideoService, DouyinCommonService douyinCommonService, DouyinFollowService douyinFollowService, DouyinMergeVideoService douyinMergeVideoService, DouyinCollectCateService douyinCollectCateService, SyncRunState syncRunState) : base(douyinCookieService, douyinHttpClientService, douyinVideoService, douyinCommonService, douyinFollowService, douyinMergeVideoService, douyinCollectCateService, syncRunState)
```

- [ ] **步骤 2：改 `DouYinFavoritSyncJob.cs`**

将 `public DouyinFavoritSyncJob(... douyinCollectCateService) : base(... douyinCollectCateService)` 同样追加 `, SyncRunState syncRunState` 到参数末尾、`, syncRunState` 到 base 末尾：
```csharp
        public DouyinFavoritSyncJob(DouyinCookieService douyinCookieService, DouyinHttpClientService douyinHttpClientService, DouyinVideoService douyinVideoService, DouyinCommonService douyinCommonService, DouyinFollowService douyinFollowService, DouyinMergeVideoService douyinMergeVideoService, DouyinCollectCateService douyinCollectCateService, SyncRunState syncRunState) : base(douyinCookieService, douyinHttpClientService, douyinVideoService, douyinCommonService, douyinFollowService, douyinMergeVideoService, douyinCollectCateService, syncRunState)
```

- [ ] **步骤 3：改 `DouyinFollowedSyncJob.cs`**

```csharp
        public DouyinFollowedSyncJob(DouyinCookieService douyinCookieService, DouyinHttpClientService douyinHttpClientService, DouyinVideoService douyinVideoService, DouyinCommonService douyinCommonService, DouyinFollowService douyinFollowService, DouyinMergeVideoService douyinMergeVideoService, DouyinCollectCateService douyinCollectCateService, SyncRunState syncRunState) : base(douyinCookieService, douyinHttpClientService, douyinVideoService, douyinCommonService, douyinFollowService, douyinMergeVideoService, douyinCollectCateService, syncRunState)
```

- [ ] **步骤 4：改 `DouyinMixSyncJob.cs`**

```csharp
        public DouyinMixSyncJob(DouyinCookieService douyinCookieService, DouyinHttpClientService douyinHttpClientService, DouyinVideoService douyinVideoService, DouyinCommonService douyinCommonService, DouyinFollowService douyinFollowService, DouyinMergeVideoService douyinMergeVideoService, DouyinCollectCateService douyinCollectCateService, SyncRunState syncRunState) : base(douyinCookieService, douyinHttpClientService, douyinVideoService, douyinCommonService, douyinFollowService, douyinMergeVideoService, douyinCollectCateService, syncRunState)
```

- [ ] **步骤 5：改 `DouyinSeriesSyncJob.cs`**

```csharp
        public DouyinSeriesSyncJob(DouyinCookieService douyinCookieService, DouyinHttpClientService douyinHttpClientService, DouyinVideoService douyinVideoService, DouyinCommonService douyinCommonService, DouyinFollowService douyinFollowService, DouyinMergeVideoService douyinMergeVideoService, DouyinCollectCateService douyinCollectCateService, SyncRunState syncRunState) : base(douyinCookieService, douyinHttpClientService, douyinVideoService, douyinCommonService, douyinFollowService, douyinMergeVideoService, douyinCollectCateService, syncRunState)
```

- [ ] **步骤 6：构建 + 跑测试验证**

运行：`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`，预期 0 错误。
运行：`DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`，预期全部通过（184）。

- [ ] **步骤 7：Commit**

```bash
git add job/DouyinBasicSyncJob.cs job/DouYinCollectSyncJob.cs job/DouYinFavoritSyncJob.cs job/DouyinFollowedSyncJob.cs job/DouyinMixSyncJob.cs job/DouyinSeriesSyncJob.cs
git commit -m "feat(sync): 作业自报启停+协作式取消+实时进度，子类透传 SyncRunState"
```

---

## 任务 5：后端接口（Stop / Status + Trigger 校验）

**文件：**
- 修改：`Controllers/ConfigController.cs`

- [ ] **步骤 1：控制器注入 `SyncRunState`**

构造函数当前为：
```csharp
        public ConfigController(DouyinCookieService dyCookieService, DouyinCommonService commonService, DouyinQuartzJobService quartzJobService, DouyinFollowService douyinFollowService, DouyinCookieService douyinCookieService, DouyinHttpClientService httpClientService)
        {
            this.dyCookieService = dyCookieService;
            this.commonService = commonService;
            this.quartzJobService = quartzJobService;
            this.douyinFollowService = douyinFollowService;
            this.douyinCookieService = douyinCookieService;
            this.httpClientService = httpClientService;
        }
```
改为追加 `SyncRunState syncRunState` 参数与字段：
```csharp
        private readonly SyncRunState syncRunState;

        public ConfigController(DouyinCookieService dyCookieService, DouyinCommonService commonService, DouyinQuartzJobService quartzJobService, DouyinFollowService douyinFollowService, DouyinCookieService douyinCookieService, DouyinHttpClientService httpClientService, SyncRunState syncRunState)
        {
            this.dyCookieService = dyCookieService;
            this.commonService = commonService;
            this.quartzJobService = quartzJobService;
            this.douyinFollowService = douyinFollowService;
            this.douyinCookieService = douyinCookieService;
            this.httpClientService = httpClientService;
            this.syncRunState = syncRunState;
        }
```
（`ConfigController.cs` 已 `using dy.net.service;`；若无则在文件顶部补上。）

- [ ] **步骤 2：`TriggerSyncNow` 加"已在运行则拒绝"校验**

将 `TriggerSyncNow` 方法体最前面加入运行校验。当前方法体首行是 `if (!string.IsNullOrWhiteSpace(type))`，在其之前插入：

```csharp
            // 后端互斥校验：已有同步在执行（含定时触发）则拒绝再次触发
            if (!syncRunState.TryBeginManualTrigger(DateTime.Now))
                return ApiResult.Fail("已有同步任务正在执行，请先停止或等待完成");

```

- [ ] **步骤 3：新增 `StopSyncNow` 与 `SyncStatus` 接口**

在 `TriggerSyncNow` 方法之后新增：

```csharp
        /// <summary>
        /// 停止当前正在执行的同步（下完当前视频后中止本轮）。无任务执行时返回失败。
        /// </summary>
        [HttpGet("StopSyncNow")]
        public IActionResult StopSyncNow()
        {
            var stopped = syncRunState.RequestStop();
            return stopped
                ? ApiResult.Success(new { stopped = true }, "已发出停止指令，正在结束当前视频后中止")
                : ApiResult.Fail("当前没有正在执行的同步任务");
        }

        /// <summary>
        /// 查询当前同步执行情况（前端轮询：驱动按钮互斥 + 进度面板）。
        /// </summary>
        [HttpGet("SyncStatus")]
        public IActionResult SyncStatus()
        {
            return ApiResult.Success(syncRunState.GetSnapshot(DateTime.Now));
        }
```

- [ ] **步骤 4：构建验证**

运行：`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`
预期：0 错误。

- [ ] **步骤 5：Commit**

```bash
git add Controllers/ConfigController.cs
git commit -m "feat(sync): 新增 StopSyncNow/SyncStatus 接口 + TriggerSyncNow 互斥校验"
```

---

## 任务 6：前端 API 封装

**文件：**
- 修改：`app/src/store/coreapi.ts`

- [ ] **步骤 1：新增两个 API 方法**

在已有的 `TriggerSyncNow` 函数之后新增：

```typescript
  // 停止当前同步
  async function StopSyncNow() {
    return http.request<any, Response<any>>('/api/config/StopSyncNow', 'get').then(r => {
      return r;
    }).finally(() => {

    });
  }
  // 查询同步执行状态
  async function SyncStatus() {
    return http.request<any, Response<any>>('/api/config/SyncStatus', 'get').then(r => {
      return r;
    }).finally(() => {

    });
  }
```

- [ ] **步骤 2：导出**

在 `return { ... }` 导出块中，`TriggerSyncNow,` 之后新增：

```typescript
    StopSyncNow,
    SyncStatus,
```

- [ ] **步骤 3：Commit**

```bash
git add app/src/store/coreapi.ts
git commit -m "feat(web): coreapi 新增 StopSyncNow/SyncStatus"
```

---

## 任务 7：前端「停止」按钮 + 轮询 + 互斥 + 进度面板

**文件：**
- 修改：`app/src/pages/workplace/RecordTable.vue`

- [ ] **步骤 1：确认 vue 生命周期 import**

确保 `<script setup>` 顶部从 `vue` 引入了 `ref`、`onMounted`、`onBeforeUnmount`。`ref` 已在用；若缺 `onMounted`/`onBeforeUnmount` 则加入对应 import，例如：

```typescript
import { ref, reactive, computed, onMounted, onBeforeUnmount } from 'vue';
```
（按文件现有 import 风格合并，不要重复声明已存在的标识符。）

- [ ] **步骤 2：新增状态与轮询逻辑**

在 `const isTriggering = ref(false);` 附近新增：

```typescript
// 同步实时状态（由 SyncStatus 轮询填充）
const syncStatus = ref<any>({ running: false, startedAt: null, elapsedSec: 0, types: [], recentLogs: [] });
const isStopping = ref(false);
let syncPollTimer: any = null;

const fetchSyncStatus = async () => {
  try {
    const res = await useApiStore().SyncStatus();
    if (res.code === 0 && res.data) {
      syncStatus.value = res.data;
      // 运行中时同步禁用“立即同步”，空闲时复位
      isSyncing.value = !!res.data.running;
    }
  } catch (e) {
    // 轮询失败静默，不打断页面
  }
};

const StopNow = () => {
  if (isStopping.value || !syncStatus.value.running) return;
  isStopping.value = true;
  useApiStore()
    .StopSyncNow()
    .then((res) => {
      if (res.code === 0) message.success(res.message || '已发出停止指令');
      else message.warning(res.message || '当前没有正在执行的同步任务');
      fetchSyncStatus();
    })
    .catch(() => message.error('停止失败，请检查网络'))
    .finally(() => { isStopping.value = false; });
};

onMounted(() => {
  fetchSyncStatus();
  syncPollTimer = setInterval(fetchSyncStatus, 2500);
});
onBeforeUnmount(() => {
  if (syncPollTimer) clearInterval(syncPollTimer);
});
```

- [ ] **步骤 3：触发后立即刷新状态**

在 `TriggerNow` 的 `.then(...)` 成功分支里，把已有的 `GetRecords();` 之后追加一行 `fetchSyncStatus();`，使「立即同步」点击后按钮立即切换为运行态：

```typescript
        message.success(n ? `已触发 ${n} 个同步任务，请稍候查看同步记录` : '已触发同步任务');
        GetRecords();
        fetchSyncStatus();
```

- [ ] **步骤 4：模板加「停止」按钮 + 互斥禁用**

将已有的「立即同步」按钮（`@click="TriggerNow"`）这一段：

```html
          <a-button type="primary" ghost @click="TriggerNow" class="query-button" :loading="isTriggering" :disabled="isSyncing" style="margin-left:8px;">
            <SyncOutlined />立即同步
          </a-button>
```

改为「立即同步」+「停止」两个按钮，互斥禁用：

```html
          <a-button type="primary" ghost @click="TriggerNow" class="query-button" :loading="isTriggering" :disabled="syncStatus.running" style="margin-left:8px;">
            <SyncOutlined />立即同步
          </a-button>
          <a-button danger ghost @click="StopNow" class="query-button" :loading="isStopping" :disabled="!syncStatus.running" style="margin-left:8px;">
            <CloseOutlined />停止
          </a-button>
```
（`CloseOutlined` 若未引入，在该文件图标 import 中加入；`SyncOutlined` 已在用。）

- [ ] **步骤 5：模板加详细进度面板**

在工具栏区域下方（表格 `<a-table>` 之前）插入一个进度卡片，仅在运行中显示：

```html
      <a-card v-if="syncStatus.running" size="small" class="sync-progress-card" style="margin-bottom:12px;">
        <template #title>
          同步进行中 · 已运行 {{ syncStatus.elapsedSec }} 秒
        </template>
        <div v-for="t in syncStatus.types" :key="t.type" style="margin-bottom:4px;">
          <b>{{ t.name }}</b>
          · 已下载 {{ t.downloaded }}<span v-if="t.pageTotal">/{{ t.pageTotal }}</span>
          · 失败 {{ t.failed }}
          <span v-if="t.cookieName">· 账号 {{ t.cookieName }}</span>
          <div style="color:#888;font-size:12px;">当前：{{ t.currentTitle || '—' }}</div>
        </div>
        <div v-if="syncStatus.recentLogs && syncStatus.recentLogs.length" style="margin-top:8px;max-height:160px;overflow:auto;border-top:1px solid #f0f0f0;padding-top:6px;">
          <div v-for="(log, i) in syncStatus.recentLogs" :key="i" style="font-size:12px;color:#666;line-height:1.6;">
            {{ log.text }}
          </div>
        </div>
      </a-card>
```

- [ ] **步骤 6：构建前端验证（如本机 pnpm 可用）**

运行：`cd app && pnpm install && pnpm exec vite build`
预期：构建成功，无报错。若本机前端工具链不全，跳过此步，由 CI 的 Docker 构建验证。

- [ ] **步骤 7：Commit**

```bash
git add app/src/pages/workplace/RecordTable.vue
git commit -m "feat(web): 工作台新增停止按钮 + 同步状态轮询 + 详细进度面板（与立即同步互斥）"
```

---

## 任务 8：文档与最终验证

**文件：**
- 修改：`tests/README.md`

- [ ] **步骤 1：在特征化覆盖表加 `SyncRunState` 条目**

在 `tests/README.md` 的覆盖表新增一行：

```markdown
| `SyncRunState` | `SyncRunStateTests` | 运行状态机：空闲/运行标志、RequestStop 空闲返回 false·运行取消令牌、批次结束后重建令牌、多类型并发 IsAnyRunning、TryBeginManualTrigger 运行中拒绝、OnDownloaded 累加 + 快照（进度/最新日志在前） |
```

- [ ] **步骤 2：全量构建 + 测试**

运行：`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`（预期 0 错误）
运行：`DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`（预期 184 全通过）

- [ ] **步骤 3：Commit**

```bash
git add tests/README.md
git commit -m "docs(tests): 记录 SyncRunStateTests 覆盖"
```

- [ ] **步骤 4：推送（触发 CI 构建新镜像供线上验证）**

```bash
git push
```

---

## 实现后人工验证清单（部署新镜像后）

- 空闲时：`立即同步` 可点、`停止` 置灰；`curl /api/config/StopSyncNow` 返回失败"当前没有正在执行的同步任务"。
- 点「立即同步」后：按钮立即互斥（立即同步置灰、停止可点），进度卡片出现并实时刷新（每类型已下载数、当前视频、日志滚动）。
- 运行中：再次 `curl /api/config/TriggerSyncNow` 返回失败"已有同步任务正在执行"。
- 点「停止」：当前视频下完后本轮中止，日志出现"已停止同步（用户触发）"，几秒后卡片消失、按钮复位。
- 停止后点「立即同步」或等下一个定时周期：能重新开始，去重自动跳过已下载的。
```
