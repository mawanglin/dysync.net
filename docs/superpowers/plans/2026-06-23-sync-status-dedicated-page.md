# 独立「同步状态」菜单页 + 移动端对齐 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 新增独立左侧菜单页「同步状态」作为同步监控/控制中心（含空闲态上轮摘要），并在移动端单页对齐；逻辑收敛到一个可复用组件。

**架构：** 后端 `SyncRunState` 在批次结束时冻结"上一轮每类型明细 + 结束时间"为 `lastRun`，由 `SyncStatus` 接口带出。前端抽一个自包含组件 `SyncStatusPanel.vue`（自轮询 + 运行/空闲展示 + 可选控件），桌面新页与移动端各嵌入一次；「同步记录」页删掉内联面板、保留按钮。

**技术栈：** .NET 8 / xUnit；Vue 3 `<script setup lang="ts">` + Ant Design Vue；pnpm。

**规格：** `docs/superpowers/specs/2026-06-23-sync-status-dedicated-page-design.md`

**测试命令（本环境 net8.0 主机缺失，需 roll-forward）：**
`DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`
构建：`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`

---

## 文件结构

| 文件 | 职责 | 动作 |
|---|---|---|
| `service/SyncRunState.cs` | 增加 `lastRun`（批次结束冻结每类型明细+结束时间）；`RegisterFinish` 加 `now` 参；批次开始清 `_types` | 修改 |
| `tests/dy.net.Tests/SyncRunStateTests.cs` | 更新既有 `RegisterFinish` 调用；新增 lastRun 用例 | 修改 |
| `job/DouyinBasicSyncJob.cs` | `RegisterFinish(VideoType)` → `RegisterFinish(VideoType, DateTime.Now)` | 修改 |
| `app/src/components/SyncStatusPanel.vue` | 可复用同步状态组件（轮询+运行/空闲+可选控件） | 创建 |
| `app/src/pages/syncstatus/index.vue` | 桌面页，包一层组件（带控件） | 创建 |
| `app/src/router/routes.ts` | 新增「同步状态」菜单路由 | 修改 |
| `app/src/pages/mobile/MobileDashboard.vue` | 嵌入组件 | 修改 |
| `app/src/pages/workplace/RecordTable.vue` | 删内联进度面板（保留按钮） | 修改 |
| `tests/README.md` | 更新 `SyncRunState` 覆盖描述（追加 lastRun） | 修改 |

---

## 任务 1：后端 `SyncRunState` 增加 lastRun（TDD）

**文件：**
- 修改：`service/SyncRunState.cs`
- 修改：`tests/dy.net.Tests/SyncRunStateTests.cs`
- 修改：`job/DouyinBasicSyncJob.cs`

- [ ] **步骤 1：在 `SyncRunState.cs` 增加 lastRun 状态与 DTO**

(a) 在字段区（`private DateTime? _manualTriggerAt;` 之后）新增：
```csharp
        private LastRunSnapshot _lastRun;   // 最近一轮结束摘要（空闲时展示），重启清零
```

(b) `RegisterStart` 的 `firstOfBatch` 分支里，新增清空 `_types`（确保 `_lastRun` 只含本批次类型；上一轮数据此刻已冻结在 `_lastRun`）。把：
```csharp
                if (firstOfBatch)
                {
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                    _manualTriggerAt = null;   // 批次已真正开始，清闸
                }
```
改为：
```csharp
                if (firstOfBatch)
                {
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                    _manualTriggerAt = null;   // 批次已真正开始，清闸
                    _types.Clear();            // 新批次：清掉上一轮残留类型（其摘要已冻结进 _lastRun）
                }
```

(c) `RegisterFinish` 加 `DateTime now` 参数，并在"批次刚结束（无类型在跑）"时冻结 `_lastRun`。整体替换该方法：
```csharp
        public void RegisterFinish(VideoTypeEnum type, DateTime now)
        {
            lock (_gate)
            {
                if (_types.TryGetValue(type, out var p))
                {
                    p.Running = false;
                    p.CurrentTitle = "";
                }
                // 批次刚全部结束 → 冻结本轮每类型明细为上轮摘要
                if (!_types.Values.Any(t => t.Running))
                {
                    _lastRun = new LastRunSnapshot
                    {
                        EndedAt = now,
                        Types = _types.Select(kv => new TypeProgressSnapshot
                        {
                            Type = kv.Key.ToString(),
                            Name = kv.Key.GetDesc(),
                            Downloaded = kv.Value.Downloaded,
                            Failed = kv.Value.Failed,
                            PageTotal = kv.Value.PageTotal,
                            CurrentTitle = "",
                            CookieName = kv.Value.CookieName
                        }).ToList()
                    };
                }
            }
        }
```

(d) `GetSnapshot` 输出新增 `LastRun`。把 return 对象里 `RecentLogs = ...` 那一行后补一行（注意逗号）：
```csharp
                    RecentLogs = _logs.ToArray().Reverse().ToList(),   // 锁内拍快照再反转：最新在前
                    LastRun = _lastRun
```
（即把原来 `RecentLogs = _logs.ToArray().Reverse().ToList()` 末尾加逗号，再加 `LastRun = _lastRun`。）

(e) `SyncStatusSnapshot` 类增加属性（在 `RecentLogs` 属性之后）：
```csharp
        [JsonPropertyName("lastRun")] public LastRunSnapshot LastRun { get; set; }
```

(f) 在文件末尾（`SyncLogSnapshot` 类之后、命名空间右括号之前）新增 DTO：
```csharp
    public class LastRunSnapshot
    {
        [JsonPropertyName("endedAt")] public DateTime EndedAt { get; set; }
        [JsonPropertyName("types")] public List<TypeProgressSnapshot> Types { get; set; } = new();
    }
```

- [ ] **步骤 2：更新 `job/DouyinBasicSyncJob.cs` 的 RegisterFinish 调用**

找到 `Execute` 里 finally 中的：
```csharp
            finally
            {
                syncRunState.RegisterFinish(VideoType);
            }
```
改为：
```csharp
            finally
            {
                syncRunState.RegisterFinish(VideoType, DateTime.Now);
            }
```
（全代码库 `RegisterFinish(` 的生产调用只此一处。）

- [ ] **步骤 3：更新既有测试调用 + 新增 lastRun 测试**

在 `tests/dy.net.Tests/SyncRunStateTests.cs` 中：

(a) 既有 4 处 `RegisterFinish(<type>)` 调用都要补 `now` 参数。逐处替换：
- `New_batch_after_all_finished_rebuilds_token`：`s.RegisterFinish(VideoTypeEnum.dy_collects);` → `s.RegisterFinish(VideoTypeEnum.dy_collects, T0);`
- `Concurrent_types_running_flag_tracks_any`：`s.RegisterFinish(VideoTypeEnum.dy_collects);` → `s.RegisterFinish(VideoTypeEnum.dy_collects, T0);`，`s.RegisterFinish(VideoTypeEnum.dy_favorite);` → `s.RegisterFinish(VideoTypeEnum.dy_favorite, T0);`
- `Snapshot_when_idle_has_no_running_types`：`s.RegisterFinish(VideoTypeEnum.dy_collects);` → `s.RegisterFinish(VideoTypeEnum.dy_collects, T0);`

（用编辑器全局确认没有遗漏的无参 `RegisterFinish(` 调用。）

(b) 新增 3 个测试方法（类内追加）：
```csharp
        [Fact]
        public void LastRun_frozen_when_batch_finishes_with_per_type_detail()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.OnDownloaded(VideoTypeEnum.dy_collects, true, "A", T0);
            s.OnDownloaded(VideoTypeEnum.dy_collects, false, "B", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects, T0.AddSeconds(10));

            var snap = s.GetSnapshot(T0.AddSeconds(20));
            Assert.False(snap.Running);
            Assert.NotNull(snap.LastRun);
            Assert.Equal(T0.AddSeconds(10), snap.LastRun.EndedAt);
            var t = Assert.Single(snap.LastRun.Types);
            Assert.Equal("dy_collects", t.Type);
            Assert.Equal(1, t.Downloaded);
            Assert.Equal(1, t.Failed);
        }

        [Fact]
        public void LastRun_aggregates_all_types_of_the_batch()
        {
            var s = new SyncRunState();
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.RegisterStart(VideoTypeEnum.dy_favorite, "Zoe", T0);
            s.OnDownloaded(VideoTypeEnum.dy_collects, true, "A", T0);
            s.OnDownloaded(VideoTypeEnum.dy_favorite, true, "B", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects, T0.AddSeconds(5));
            // 仍有 favorite 在跑 → 尚未冻结
            Assert.Null(s.GetSnapshot(T0.AddSeconds(6)).LastRun);
            s.RegisterFinish(VideoTypeEnum.dy_favorite, T0.AddSeconds(8));

            var snap = s.GetSnapshot(T0.AddSeconds(9));
            Assert.NotNull(snap.LastRun);
            Assert.Equal(2, snap.LastRun.Types.Count);
        }

        [Fact]
        public void New_batch_keeps_previous_lastRun_until_it_finishes()
        {
            var s = new SyncRunState();
            // 批次1：collect
            s.RegisterStart(VideoTypeEnum.dy_collects, "Zoe", T0);
            s.RegisterFinish(VideoTypeEnum.dy_collects, T0.AddSeconds(5));
            // 批次2：favorite 开始（清掉 collect 残留），但 lastRun 仍是批次1
            s.RegisterStart(VideoTypeEnum.dy_favorite, "Zoe", T0.AddMinutes(30));
            var midSnap = s.GetSnapshot(T0.AddMinutes(30));
            Assert.True(midSnap.Running);
            Assert.NotNull(midSnap.LastRun);
            Assert.Equal("dy_collects", Assert.Single(midSnap.LastRun.Types).Type);
            // 批次2 结束 → lastRun 覆盖为 favorite（证明 _types 在新批次开始时被清）
            s.RegisterFinish(VideoTypeEnum.dy_favorite, T0.AddMinutes(31));
            var endSnap = s.GetSnapshot(T0.AddMinutes(32));
            Assert.Equal("dy_favorite", Assert.Single(endSnap.LastRun.Types).Type);
        }
```

- [ ] **步骤 4：构建 + 跑测试**

`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`（预期 0 错误）
`DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`（预期全通过：原 185 + 新 3 = 188）

- [ ] **步骤 5：Commit**
```bash
git add service/SyncRunState.cs job/DouyinBasicSyncJob.cs tests/dy.net.Tests/SyncRunStateTests.cs
git commit -m "feat(sync): SyncRunState 增加上一轮每类型摘要 lastRun（空闲态展示）"
```

---

## 任务 2：可复用组件 `SyncStatusPanel.vue`

**文件：**
- 创建：`app/src/components/SyncStatusPanel.vue`

- [ ] **步骤 1：创建组件（逐字）**
```vue
<template>
  <a-card size="small" class="sync-status-panel">
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

    <template v-if="status.running">
      <div v-for="t in status.types" :key="t.type" style="margin-bottom:4px;">
        <b>{{ t.name }}</b> · 本轮已下载 {{ t.downloaded }} 条 · 失败 {{ t.failed }}
        <span v-if="t.cookieName">· 账号 {{ t.cookieName }}</span>
        <div style="color:#888;font-size:12px;">当前：{{ t.currentTitle || '—' }}</div>
      </div>
      <div v-if="status.recentLogs && status.recentLogs.length"
           style="margin-top:8px;max-height:160px;overflow:auto;border-top:1px solid #f0f0f0;padding-top:6px;">
        <div v-for="(log, i) in status.recentLogs" :key="i"
             style="font-size:12px;color:#666;line-height:1.6;">{{ log.text }}</div>
      </div>
    </template>

    <template v-else>
      <div v-if="status.lastRun">
        <div style="margin-bottom:6px;color:#666;">上次同步 · 结束于 {{ formatTime(status.lastRun.endedAt) }}</div>
        <div v-for="t in status.lastRun.types" :key="t.type" style="margin-bottom:4px;">
          <b>{{ t.name }}</b> · 下载 {{ t.downloaded }} 条 · 失败 {{ t.failed }}
          <span v-if="t.cookieName">· 账号 {{ t.cookieName }}</span>
        </div>
      </div>
      <div v-else style="color:#999;">暂无同步记录</div>
    </template>
  </a-card>
</template>

<script lang="ts" setup>
import { ref, onMounted, onBeforeUnmount } from 'vue';
import { useApiStore } from '@/store';
import { message } from 'ant-design-vue';
import { SyncOutlined, CloseOutlined } from '@ant-design/icons-vue';

defineProps<{ showControls?: boolean }>();

const status = ref<any>({ running: false, elapsedSec: 0, types: [], recentLogs: [], lastRun: null });
const isTriggering = ref(false);
const isStopping = ref(false);
let timer: any = null;

const fetchStatus = async () => {
  try {
    const res = await useApiStore().SyncStatus();
    if (res.code === 0 && res.data) status.value = res.data;
  } catch (e) {
    // 轮询失败静默，不打断页面
  }
};

const triggerNow = () => {
  if (isTriggering.value || status.value.running) return;
  isTriggering.value = true;
  useApiStore()
    .TriggerSyncNow()
    .then((res) => {
      if (res.code === 0) message.success(res.message || '已触发同步');
      else message.warning(res.message || '无法触发同步');
      fetchStatus();
    })
    .catch(() => message.error('触发失败，请检查网络'))
    .finally(() => { isTriggering.value = false; });
};

const stopNow = () => {
  if (isStopping.value || !status.value.running) return;
  isStopping.value = true;
  useApiStore()
    .StopSyncNow()
    .then((res) => {
      if (res.code === 0) message.success(res.message || '已发出停止指令');
      else message.warning(res.message || '当前没有正在执行的同步任务');
      fetchStatus();
    })
    .catch(() => message.error('停止失败，请检查网络'))
    .finally(() => { isStopping.value = false; });
};

const formatTime = (s: string) => {
  if (!s) return '';
  try { return new Date(s).toLocaleString(); } catch { return s; }
};

onMounted(() => {
  fetchStatus();
  timer = setInterval(fetchStatus, 2500);
});
onBeforeUnmount(() => {
  if (timer) clearInterval(timer);
});
</script>

<style scoped lang="less">
.sync-status-panel {
  margin-bottom: 12px;
}
</style>
```

- [ ] **步骤 2：Commit**
```bash
git add app/src/components/SyncStatusPanel.vue
git commit -m "feat(web): 新增可复用同步状态组件 SyncStatusPanel"
```

---

## 任务 3：桌面端「同步状态」菜单 + 页面

**文件：**
- 创建：`app/src/pages/syncstatus/index.vue`
- 修改：`app/src/router/routes.ts`

- [ ] **步骤 1：创建页面（包一层组件，带控件）**
`app/src/pages/syncstatus/index.vue`：
```vue
<template>
  <div class="syncstatus" style="margin-top: 8px;">
    <SyncStatusPanel :show-controls="true" />
  </div>
</template>

<script lang="ts" setup>
import SyncStatusPanel from '@/components/SyncStatusPanel.vue';
import { useUnbounded } from '@/utils/useTheme';

useUnbounded();
</script>

<style scoped lang="less">
.syncstatus {
}
</style>
```

- [ ] **步骤 2：注册菜单路由（放在「数据看板」之后、「同步记录」之前）**
在 `app/src/router/routes.ts` 中，找到「数据看板」路由对象：
```javascript
  {
    path: '/dashboard',
    name: '数据看板',
    meta: {
      icon: 'RadarChartOutlined',
      view: 'self',
      target: '_self',
      renderMenu: true,
      cacheable: false,
    },
    component: () => import('@/pages/workplace/statics.vue'),
  },
```
在它**之后**插入：
```javascript
  {
    path: '/syncstatus',
    name: '同步状态',
    meta: {
      icon: 'PlayCircleOutlined',
      view: 'self',
      target: '_self',
      renderMenu: true,
      cacheable: false,
    },
    component: () => import('@/pages/syncstatus/index.vue'),
  },
```

- [ ] **步骤 3：Commit**
```bash
git add app/src/pages/syncstatus/index.vue app/src/router/routes.ts
git commit -m "feat(web): 新增「同步状态」独立菜单页"
```

---

## 任务 4：移动端 `MobileDashboard` 嵌入组件

**文件：**
- 修改：`app/src/pages/mobile/MobileDashboard.vue`

- [ ] **步骤 1：导入组件**
先 Read `app/src/pages/mobile/MobileDashboard.vue` 了解结构。在其 `<script setup>` 的 import 区加入：
```typescript
import SyncStatusPanel from '@/components/SyncStatusPanel.vue';
```

- [ ] **步骤 2：在模板顶部内容区嵌入**
在移动端主内容容器**靠上**的位置（例如统计卡片之后、日志/列表之前）插入一行：
```html
<SyncStatusPanel :show-controls="true" />
```
选一个合法的同级位置（在根容器内、不破坏既有布局）；若不确定，放在主内容根容器的第一个子元素位置即可。

- [ ] **步骤 3：Commit**
```bash
git add app/src/pages/mobile/MobileDashboard.vue
git commit -m "feat(web): 移动端 Dashboard 嵌入同步状态组件"
```

---

## 任务 5：「同步记录」页删除内联进度面板（保留按钮）

**文件：**
- 修改：`app/src/pages/workplace/RecordTable.vue`

- [ ] **步骤 1：删除内联面板块**
删除以下整段（即 `<!-- 同步进度面板（仅运行中显示） -->` 注释 + 紧随其后的 `<a-card v-if="syncStatus.running" ...> ... </a-card>` 整块）：
```html
    <!-- 同步进度面板（仅运行中显示） -->
    <a-card v-if="syncStatus.running" size="small" class="sync-progress-card" style="margin-bottom:12px;">
      <template #title>
        同步进行中 · 已运行 {{ syncStatus.elapsedSec }} 秒
      </template>
      <div v-for="t in syncStatus.types" :key="t.type" style="margin-bottom:4px;">
        <b>{{ t.name }}</b>
        · 本轮已下载 {{ t.downloaded }} 条
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
**保留**「立即同步 / 停止」按钮、`syncStatus`/`isStopping` ref、`fetchSyncStatus` 轮询、`onMounted/onBeforeUnmount`——它们仍驱动按钮互斥。不要动 script 部分。

> 说明：记录页保留自己的轮询是有意的（按钮互斥需要）。这与新组件各自独立轮询并存——同一时刻只有一个路由挂载，无并发问题。

- [ ] **步骤 2：Commit**
```bash
git add app/src/pages/workplace/RecordTable.vue
git commit -m "refactor(web): 同步记录页移除内联进度面板（移至独立同步状态页），保留按钮"
```

---

## 任务 6：文档 + 全量验证 + 推送

**文件：**
- 修改：`tests/README.md`

- [ ] **步骤 1：更新 README 的 SyncRunState 覆盖描述**
在 `tests/README.md` 的 `SyncRunState` 行末尾（`日志环 50 条上限淘汰最旧` 之后）追加：
`、批次结束冻结 lastRun 每类型明细+结束时间·新批次开始清残留类型但保留上轮 lastRun 直至本批次结束覆盖`

- [ ] **步骤 2：全量构建 + 测试**
`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`（预期 0 错误）
`DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`（预期 188 通过）

- [ ] **步骤 3：（如本机 pnpm 可用）前端构建自检**
`cd app && pnpm install --no-frozen-lockfile && pnpm exec vite build`，确认通过。
若本机前端工具链不全，跳过，由 CI 的 Docker 构建验证（推送后）。

- [ ] **步骤 4：Commit + 推送（触发 CI 构建镜像验证前端）**
```bash
git add tests/README.md
git commit -m "docs(tests): SyncRunStateTests 追加 lastRun 覆盖"
git push
```

---

## 实现后人工验证清单（部署新镜像后）
- 桌面端左侧出现「同步状态」菜单，点进去：空闲时显示"当前空闲"+上次同步每类型摘要（若有）。
- 点页内「立即同步」→ 切到运行态、详细进度实时刷新；「停止」可中止；按钮互斥正确。
- 「同步记录」页不再有内联进度面板，但「立即同步/停止」按钮仍在、互斥正常。
- 移动端 `/mobile` 顶部出现同步状态块，可看进度 + 停止。
- 一轮同步结束后回到「同步状态」页（空闲），显示该轮每类型下载/失败明细 + 结束时间。

## 自检（已在编写后执行）
- 规格每节均有对应任务（组件/桌面菜单/移动端/后端 lastRun/记录页瘦身/测试）✅
- 无占位符；所有代码步骤含完整代码 ✅
- 类型一致：`lastRun.endedAt`/`lastRun.types[]` 与后端 `LastRunSnapshot`（`endedAt`/`types`）+ `TypeProgressSnapshot`（`type/name/downloaded/failed/cookieName`）camelCase 一致；前端读取字段一一对应 ✅
- `RegisterFinish` 签名变更已覆盖所有调用点（基类 1 处 + 测试 4 处）✅
