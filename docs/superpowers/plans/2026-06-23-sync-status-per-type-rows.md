# 同步状态改为「每类型常驻一行」实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development 逐任务实现。步骤用复选框（`- [ ]`）跟踪。

**目标：** 修复同步状态面板只显示 1 条的缺陷——改成每个参与的类型常驻一行（运行中显示实时进度，跑完保留"已完成 + 本轮下载/失败 + 完成时间"，下次该类型再跑刷新它那一行）。

**根因：** 旧 `SyncRunState` 用"批次"模型——`RegisterStart` 在 `firstOfBatch`（当前无人在跑）时 `_types.Clear()` 并把结果冻结进 `_lastRun`。5 个作业相互独立、常不重叠（喜欢/合集/短剧秒结束、收藏慢），导致每个新作业起跑时清掉前一个，任意时刻只剩 1 条、`_lastRun` 也只剩最后一个。

**方案：** 去掉 `_types.Clear()` 与 `_lastRun`；`_types` 每类型一条持久保留（该类型再次 `RegisterStart` 时刷新它自己那条），新增 `EndedAt`；快照返回**全部类型**（带 `running`/`endedAt`），不再按 Running 过滤、不再有 `lastRun`。取消令牌逻辑（firstOfBatch 重建）保持不变。

**技术栈：** .NET 8 / xUnit；Vue 3 + Ant Design Vue。

**测试命令：** `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`；构建 `dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`。

---

## 文件结构
| 文件 | 动作 |
|---|---|
| `service/SyncRunState.cs` | 改：移除 clear/lastRun；TypeProgress 加 EndedAt；快照返回全部类型 + running/endedAt | 
| `tests/dy.net.Tests/SyncRunStateTests.cs` | 改：重写 idle/lastRun 相关用例为"每类型持久"语义 |
| `app/src/components/SyncStatusPanel.vue` | 改：单一类型列表（运行/完成状态），去掉 lastRun 分支 |
| `tests/README.md` | 改：更新 SyncRunState 覆盖描述 |

> `job/DouyinBasicSyncJob.cs` 无需改动（`RegisterStart`/`RegisterFinish` 签名不变）。`RecordTable.vue` 不消费 types/lastRun（只用 running），无需改动。

---

## 任务 1：后端 `SyncRunState` 改每类型持久（TDD）

**文件：** 改 `service/SyncRunState.cs`、`tests/dy.net.Tests/SyncRunStateTests.cs`

- [ ] **步骤 1：改 `SyncRunState.cs`**

(a) 删除字段 `private LastRunSnapshot _lastRun;`（第 20 行）。

(b) `RegisterStart` 的 `if (firstOfBatch)` 块里**删除** `_types.Clear();` 那一行（保留 `_cts` 重建与 `_manualTriggerAt = null;`）。并把新建 `TypeProgress` 加上 `EndedAt = null`：
```csharp
                _types[type] = new TypeProgress
                {
                    Running = true,
                    StartedAt = now,
                    EndedAt = null,
                    CookieName = cookieName ?? "",
                    Downloaded = 0,
                    Failed = 0,
                    PageTotal = 0,
                    CurrentTitle = ""
                };
```

(c) 把整个 `RegisterFinish` 替换为（去掉 lastRun 冻结，改为记录该类型结束时间）：
```csharp
        public void RegisterFinish(VideoTypeEnum type, DateTime now)
        {
            lock (_gate)
            {
                if (_types.TryGetValue(type, out var p))
                {
                    p.Running = false;
                    p.CurrentTitle = "";
                    p.EndedAt = now;
                }
            }
        }
```

(d) 把整个 `GetSnapshot` 替换为（返回全部类型；带 running/endedAt；去掉 LastRun）：
```csharp
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
                        .OrderByDescending(kv => kv.Value.Running)   // 进行中的排前
                        .Select(kv => new TypeProgressSnapshot
                        {
                            Type = kv.Key.ToString(),
                            Name = kv.Key.GetDesc(),
                            Running = kv.Value.Running,
                            Downloaded = kv.Value.Downloaded,
                            Failed = kv.Value.Failed,
                            PageTotal = kv.Value.PageTotal,
                            CurrentTitle = kv.Value.CurrentTitle,
                            CookieName = kv.Value.CookieName,
                            EndedAt = kv.Value.EndedAt
                        })
                        .ToList(),
                    RecentLogs = _logs.ToArray().Reverse().ToList()   // 锁内拍快照再反转：最新在前
                };
            }
        }
```

(e) `TypeProgress` 内部类加字段 `public DateTime? EndedAt;`：
```csharp
        private class TypeProgress
        {
            public bool Running;
            public DateTime StartedAt;
            public DateTime? EndedAt;
            public int Downloaded;
            public int Failed;
            public int PageTotal;
            public string CurrentTitle = "";
            public string CookieName = "";
        }
```

(f) `SyncStatusSnapshot` 删除 `[JsonPropertyName("lastRun")] public LastRunSnapshot LastRun { get; set; }` 这一行。

(g) `TypeProgressSnapshot` 加两个属性（在 `CookieName` 之后）：
```csharp
        [JsonPropertyName("running")] public bool Running { get; set; }
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
```

(h) 删除整个 `LastRunSnapshot` 类（文件末尾那段）。

- [ ] **步骤 2：重写测试**

打开 `tests/dy.net.Tests/SyncRunStateTests.cs`：

(a) **删除** 这 3 个旧测试方法（lastRun 模型已移除）：`LastRun_frozen_when_batch_finishes_with_per_type_detail`、`LastRun_aggregates_all_types_of_the_batch`、`New_batch_keeps_previous_lastRun_until_it_finishes`。

(b) **替换** `Snapshot_when_idle_has_no_running_types` 方法为如下（新语义：空闲时仍保留已完成类型行）：
```csharp
        [Fact]
        public void Finished_type_persists_in_snapshot_with_endedAt()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.OnDownloaded(VideoTypeEnum.dy_collects, true, "A", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects, T0.AddSeconds(10));

            var snap = s.GetSnapshot(T0.AddSeconds(20));
            Assert.False(snap.Running);
            var t = Assert.Single(snap.Types);       // 跑完仍保留该类型行
            Assert.False(t.Running);
            Assert.Equal(1, t.Downloaded);
            Assert.Equal(T0.AddSeconds(10), t.EndedAt);
        }
```

(c) **新增** 关键回归测试（证明多类型即便不重叠也各占一行——旧 clear 缺陷会只剩 1 条）：
```csharp
        [Fact]
        public void NonOverlapping_types_each_keep_their_own_row()
        {
            var s = new SyncRunState();
            // collect 跑完再起 favorite（顺序、不重叠）
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.OnDownloaded(VideoTypeEnum.dy_collects, true, "A", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects, T0.AddSeconds(5));
            s.RegisterStart(VideoTypeEnum.dy_favorite, "Zoe", T0.AddSeconds(6));
            s.OnDownloaded(VideoTypeEnum.dy_favorite, true, "B", T0.AddSeconds(6));
            s.RegisterFinish(VideoTypeEnum.dy_favorite, T0.AddSeconds(8));

            var snap = s.GetSnapshot(T0.AddSeconds(9));
            Assert.False(snap.Running);
            Assert.Equal(2, snap.Types.Count);       // 两个类型都各占一行
            Assert.All(snap.Types, t => Assert.False(t.Running));
            Assert.Contains(snap.Types, t => t.Type == "dy_collects" && t.Downloaded == 1);
            Assert.Contains(snap.Types, t => t.Type == "dy_favorite" && t.Downloaded == 1);
        }

        [Fact]
        public void Running_and_finished_types_coexist_in_snapshot()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.RegisterStart(VideoTypeEnum.dy_favorite, "Zoe", T0);
            s.RegisterFinish(VideoTypeEnum.dy_favorite, T0.AddSeconds(3));   // favorite 完成，collect 仍跑

            var snap = s.GetSnapshot(T0.AddSeconds(4));
            Assert.True(snap.Running);
            Assert.Equal(2, snap.Types.Count);
            Assert.Contains(snap.Types, t => t.Type == "dy_collects" && t.Running);
            Assert.Contains(snap.Types, t => t.Type == "dy_favorite" && !t.Running);
        }

        [Fact]
        public void Rerunning_a_type_refreshes_its_row()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.OnDownloaded(VideoTypeEnum.dy_collects, true, "A", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects, T0.AddSeconds(5));
            // 再次跑 collect → 计数清零、重新进行中
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0.AddMinutes(30));

            var snap = s.GetSnapshot(T0.AddMinutes(30));
            var t = Assert.Single(snap.Types);
            Assert.True(t.Running);
            Assert.Equal(0, t.Downloaded);
            Assert.Null(t.EndedAt);
        }
```

(d) 其余既有测试（`Idle_initially...`、`RegisterStart_marks_running...`、`RequestStop_cancels_token...`、`New_batch_after_all_finished_rebuilds_token`、`Concurrent_types_running_flag_tracks_any`、`OnDownloaded_accumulates...`、`OnDownloaded_caps_recent_logs_at_50`）**保持不变**（它们不依赖被移除的语义；`OnDownloaded_accumulates...` 仍只有 1 个类型在跑，`Assert.Single(snap.Types)` 依旧成立）。

- [ ] **步骤 3：构建 + 测试**
`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`（0 错误）
`DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`
（原 188 − 删 3 + 替换 1（净 0）+ 新增 3 = 188。若数字略有出入以实际通过为准，关键是全绿。）

- [ ] **步骤 4：Commit**
```bash
git add service/SyncRunState.cs tests/dy.net.Tests/SyncRunStateTests.cs
git commit -m "fix(sync): 同步状态改为每类型常驻一行（去掉脆弱的批次清空/lastRun）"
```

---

## 任务 2：前端面板改单一类型列表

**文件：** 改 `app/src/components/SyncStatusPanel.vue`

- [ ] **步骤 1：替换 `<template>` 主体（标题 + 类型列表 + 日志）**

把 `<a-card>` 内从 `<template #title>` 到日志块整段，替换为：
```vue
    <template #title>
      <span v-if="status.running">同步进行中 · 已运行 {{ status.elapsedSec }} 秒</span>
      <span v-else>同步状态 · 当前空闲</span>
    </template>
    <template #extra v-if="showControls">
      <a-button type="primary" ghost size="small" :loading="isTriggering" :disabled="status.running" @click="triggerNow">
        <SyncOutlined />立即同步
      </a-button>
      <a-button danger ghost size="small" :loading="isStopping" :disabled="!status.running" @click="stopNow" style="margin-left:8px;">
        <CloseOutlined />停止
      </a-button>
    </template>

    <div v-if="status.types && status.types.length">
      <div v-for="t in status.types" :key="t.type" style="margin-bottom:6px;">
        <b>{{ t.name }}</b>
        <a-tag :color="t.running ? 'processing' : 'default'" style="margin-left:6px;">
          {{ t.running ? '进行中' : '已完成' }}
        </a-tag>
        · 本轮已下载 {{ t.downloaded }} 条 · 失败 {{ t.failed }}
        <span v-if="t.cookieName">· 账号 {{ t.cookieName }}</span>
        <div v-if="t.running" style="color:#888;font-size:12px;">当前：{{ t.currentTitle || '—' }}</div>
        <div v-else-if="t.endedAt" style="color:#aaa;font-size:12px;">完成于 {{ formatTime(t.endedAt) }}</div>
      </div>
      <div v-if="status.recentLogs && status.recentLogs.length"
           style="margin-top:8px;max-height:160px;overflow:auto;border-top:1px solid #f0f0f0;padding-top:6px;">
        <div v-for="(log, i) in status.recentLogs" :key="i"
             style="font-size:12px;color:#666;line-height:1.6;">{{ log.text }}</div>
      </div>
    </div>
    <div v-else style="color:#999;">暂无同步记录</div>
```
（即删除原来的 `<template v-if="status.running">...</template>` 与 `<template v-else>...lastRun...</template>` 两个分支，换成上面这一套统一列表。）

- [ ] **步骤 2：script 部分**
- `status` 初值里 `lastRun: null` 可保留也可删除（不再使用），建议删掉它，改为 `const status = ref<any>({ running: false, elapsedSec: 0, types: [], recentLogs: [] });`
- `formatTime` 保留（现在给 `t.endedAt` 用）。
- 其余（轮询、triggerNow、stopNow、onMounted/onBeforeUnmount）不变。
- `a-tag` 是 Ant Design Vue 全局组件，无需额外 import。

- [ ] **步骤 3：Commit（仅此一个文件）**
```bash
git add app/src/components/SyncStatusPanel.vue
git commit -m "feat(web): 同步状态面板改为每类型一行（进行中/已完成 + 完成时间）"
```

---

## 任务 3：文档 + 验证 + 推送

**文件：** 改 `tests/README.md`

- [ ] **步骤 1：更新 SyncRunState 覆盖描述**
把 `tests/README.md` 中 `SyncRunState` 行里 `批次结束冻结 lastRun ... 直至本批次结束覆盖` 这段，替换为：
`每类型独立持久一行（不再批次清空）·跑完保留 running=false+endedAt+计数·重跑刷新该类型行·多类型即便不重叠也各占一行`

- [ ] **步骤 2：全量构建 + 测试**
`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`（0 错误）；测试全绿。

- [ ] **步骤 3：（如本机 pnpm 可用）`cd app && pnpm exec vite build`；否则跳过由 CI 验证。不要改 lockfile/workspace。**

- [ ] **步骤 4：Commit + 推送**
```bash
git add tests/README.md
git commit -m "docs(tests): SyncRunState 覆盖更新为每类型持久语义"
git push
```

---

## 实现后人工验证（部署新镜像后）
- 「同步状态」页点「立即同步」：每个已启用类型各显示一行（进行中实时刷新；跑完转"已完成 + 完成时间"，行保留）。
- 全部跑完后页面仍显示所有类型行（各自上轮结果），非空闲一条。
- 再次「立即同步」：各行刷新（计数归零→重新进行中）。

## 自检
- 规格点（去 clear/lastRun、每类型持久、running/endedAt、全部返回）均有任务覆盖 ✅
- 无占位符；代码步骤含完整代码 ✅
- 字段一致：后端 `TypeProgressSnapshot` 新增 `running`/`endedAt`（camelCase）与前端 `t.running`/`t.endedAt` 对应；移除的 `lastRun` 前端同步移除 ✅
- `RegisterStart`/`RegisterFinish` 签名未变 → 基类与既有调用无需改 ✅
