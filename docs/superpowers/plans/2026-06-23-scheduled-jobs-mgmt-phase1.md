# 定时任务管理 · 第一期（列表 + 详情）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development 逐任务实现。步骤用复选框（`- [ ]`）跟踪。

**目标：** 把 `/syncstatus` 页从"每类型进度面板"换成"定时任务列表 + 详情"：每个任务显示周期/下次执行/状态/最近结果，点行看详情，可单类型「立即执行」，顶部保留全局立即同步/停止。

**架构：** `DouyinQuartzJobService` 新增 `GetJobsOverviewAsync()` 从 Quartz 调度器读取每个任务的触发器信息（周期/下次执行/状态）；`ConfigController` 的新接口 `SyncJobs` 把它与 `SyncRunState` 的每类型最近结果合并后返回。周期描述抽成纯函数单测。前端把页面改成 a-table + 抽屉，轮询刷新。

**技术栈：** .NET 8 / Quartz / xUnit；Vue 3 + Ant Design Vue。

**规格：** `docs/superpowers/specs/2026-06-23-scheduled-jobs-mgmt-phase1-design.md`

**测试命令：** `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`；构建 `dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`。

---

## 文件结构
| 文件 | 职责 | 动作 |
|---|---|---|
| `utils/SyncJobScheduleDescriber.cs` | 纯函数：把触发器原始值描述为人类可读周期 | 创建 |
| `tests/dy.net.Tests/SyncJobScheduleDescriberTests.cs` | 描述函数特征化测试 | 创建 |
| `model/dto/SyncJobOverview.cs` | 任务总览 DTO | 创建 |
| `service/DouyinQuartzJobService.cs` | `GetJobsOverviewAsync()` 读 Quartz 组装调度信息 | 改 |
| `Controllers/ConfigController.cs` | `GET SyncJobs` 合并 SyncRunState 最近结果 | 改 |
| `app/src/store/coreapi.ts` | `SyncJobs()` | 改 |
| `app/src/pages/syncstatus/index.vue` | 改为任务管理表格 + 抽屉 | 改（整体重写） |
| `app/src/router/routes.ts` | `/syncstatus` 菜单名 同步状态 → 定时任务 | 改 |
| `tests/README.md` | 加描述函数覆盖条目 | 改 |

> 不动：`SyncStatusPanel.vue`（移动端仍用）、`MobileDashboard.vue`、`SyncRunState.cs`。

---

## 任务 1：周期描述纯函数（TDD）

**文件：** 创建 `utils/SyncJobScheduleDescriber.cs`、`tests/dy.net.Tests/SyncJobScheduleDescriberTests.cs`

- [ ] **步骤 1：创建 `utils/SyncJobScheduleDescriber.cs`**
```csharp
namespace dy.net.utils
{
    /// <summary>
    /// 把已从 Quartz 触发器提取出的原始调度值，描述为人类可读的周期文案。
    /// 纯函数（不依赖 Quartz 类型），便于单测；触发器字段的提取留在调用方（service）。
    /// </summary>
    public static class SyncJobScheduleDescriber
    {
        /// <param name="isCron">是否 Cron 触发器</param>
        /// <param name="cronExpr">Cron 表达式（isCron 时有效）</param>
        /// <param name="simpleIntervalMinutes">SimpleTrigger 的间隔分钟数（非 cron 时有效）</param>
        public static string Describe(bool isCron, string cronExpr, int? simpleIntervalMinutes)
        {
            if (isCron && !string.IsNullOrWhiteSpace(cronExpr))
                return $"Cron: {cronExpr}";
            if (simpleIntervalMinutes.HasValue && simpleIntervalMinutes.Value > 0)
                return $"每 {simpleIntervalMinutes.Value} 分钟";
            return "自定义";
        }
    }
}
```

- [ ] **步骤 2：创建测试 `tests/dy.net.Tests/SyncJobScheduleDescriberTests.cs`**
```csharp
using dy.net.utils;

namespace dy.net.Tests
{
    public class SyncJobScheduleDescriberTests
    {
        [Theory]
        [InlineData(false, null, 30, "每 30 分钟")]
        [InlineData(false, null, 60, "每 60 分钟")]
        [InlineData(true, "0 0/30 * * * ?", null, "Cron: 0 0/30 * * * ?")]
        [InlineData(false, null, null, "自定义")]
        [InlineData(true, null, null, "自定义")]          // 声称 cron 但无表达式 → 自定义
        [InlineData(false, null, 0, "自定义")]            // 间隔 0 视为无效
        public void Describe_locks_current_behavior(bool isCron, string cronExpr, int? minutes, string expected)
        {
            Assert.Equal(expected, SyncJobScheduleDescriber.Describe(isCron, cronExpr, minutes));
        }
    }
}
```

- [ ] **步骤 3：构建 + 测试**
`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`（0 错误）
`DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/dy.net.Tests/dy.net.Tests.csproj -nologo`（全绿，188 + 6 用例同属 1 个 Theory → 通过数 +6）

- [ ] **步骤 4：Commit**
```bash
git add utils/SyncJobScheduleDescriber.cs tests/dy.net.Tests/SyncJobScheduleDescriberTests.cs
git commit -m "feat(sched): 周期描述纯函数 SyncJobScheduleDescriber + 测试"
```

---

## 任务 2：后端任务总览（DTO + Service + 接口）

**文件：** 创建 `model/dto/SyncJobOverview.cs`；改 `service/DouyinQuartzJobService.cs`、`Controllers/ConfigController.cs`

- [ ] **步骤 1：创建 DTO `model/dto/SyncJobOverview.cs`**
```csharp
using System;
using System.Text.Json.Serialization;

namespace dy.net.model.dto
{
    /// <summary>定时任务总览（调度信息 + 最近一轮结果合并）。</summary>
    public class SyncJobOverview
    {
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("scheduled")] public bool Scheduled { get; set; }
        [JsonPropertyName("scheduleDesc")] public string ScheduleDesc { get; set; }
        [JsonPropertyName("nextFireTime")] public DateTime? NextFireTime { get; set; }
        [JsonPropertyName("prevFireTime")] public DateTime? PrevFireTime { get; set; }
        [JsonPropertyName("triggerState")] public string TriggerState { get; set; }
        // 以下由 ConfigController 合并 SyncRunState 后填充
        [JsonPropertyName("running")] public bool Running { get; set; }
        [JsonPropertyName("downloaded")] public int Downloaded { get; set; }
        [JsonPropertyName("failed")] public int Failed { get; set; }
        [JsonPropertyName("currentTitle")] public string CurrentTitle { get; set; }
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
    }
}
```

- [ ] **步骤 2：`DouyinQuartzJobService` 新增 `GetJobsOverviewAsync()`**

在类内新增方法（`using dy.net.utils;` 已存在；`Quartz` 已 using）：
```csharp
        /// <summary>
        /// 读取所有可管理周期任务的调度总览（不含运行结果——由调用方合并 SyncRunState）。
        /// 排除一次性任务 dy_followuser_once。
        /// </summary>
        public async Task<List<SyncJobOverview>> GetJobsOverviewAsync()
        {
            var result = new List<SyncJobOverview>();
            var scheduler = await _schedulerFactory.GetScheduler();

            foreach (var kv in JobConfigs)
            {
                if (kv.Key == VideoTypeEnum.dy_followuser_once) continue;

                var cfg = kv.Value;
                var triggerKey = new TriggerKey(cfg.TriggerKey, DefaultJobGroup);
                var overview = new SyncJobOverview
                {
                    Type = kv.Key.ToString(),
                    Name = kv.Key.GetDesc(),
                    Scheduled = false,
                    ScheduleDesc = "未启用",
                    TriggerState = "未启用"
                };

                var trigger = await scheduler.GetTrigger(triggerKey);
                if (trigger != null)
                {
                    overview.Scheduled = true;
                    overview.NextFireTime = trigger.GetNextFireTimeUtc()?.LocalDateTime;
                    overview.PrevFireTime = trigger.GetPreviousFireTimeUtc()?.LocalDateTime;

                    bool isCron = trigger is ICronTrigger;
                    string cronExpr = (trigger as ICronTrigger)?.CronExpressionString;
                    int? simpleMinutes = trigger is ISimpleTrigger st
                        ? (int)st.RepeatInterval.TotalMinutes
                        : (int?)null;
                    overview.ScheduleDesc = SyncJobScheduleDescriber.Describe(isCron, cronExpr, simpleMinutes);

                    var state = await scheduler.GetTriggerState(triggerKey);
                    overview.TriggerState = state.ToString();   // Normal/Paused/...
                }

                result.Add(overview);
            }
            return result;
        }
```

- [ ] **步骤 3：`ConfigController` 新增 `SyncJobs` 接口（合并 SyncRunState 最近结果）**

在 `SyncStatus` 方法之后新增：
```csharp
        /// <summary>
        /// 定时任务总览：调度信息（来自 Quartz）+ 每类型最近一轮结果（来自 SyncRunState）。
        /// </summary>
        [HttpGet("SyncJobs")]
        public async Task<IActionResult> SyncJobs()
        {
            var jobs = await quartzJobService.GetJobsOverviewAsync();
            var snap = syncRunState.GetSnapshot(DateTime.Now);
            foreach (var job in jobs)
            {
                var t = snap.Types.FirstOrDefault(x => x.Type == job.Type);
                if (t != null)
                {
                    job.Running = t.Running;
                    job.Downloaded = t.Downloaded;
                    job.Failed = t.Failed;
                    job.CurrentTitle = t.CurrentTitle;
                    job.EndedAt = t.EndedAt;
                }
            }
            return ApiResult.Success(jobs);
        }
```
（`ConfigController` 已注入 `quartzJobService` 与 `syncRunState`；`System.Linq` 在控制器中可用。`SyncStatusSnapshot.Types` 元素类型 `TypeProgressSnapshot` 有 `Type/Running/Downloaded/Failed/CurrentTitle/EndedAt`。）

- [ ] **步骤 4：构建验证**
`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`（0 错误）。

- [ ] **步骤 5：Commit**
```bash
git add model/dto/SyncJobOverview.cs service/DouyinQuartzJobService.cs Controllers/ConfigController.cs
git commit -m "feat(sched): 后端任务总览 GetJobsOverviewAsync + /api/config/SyncJobs"
```

---

## 任务 3：前端任务管理页 + API + 菜单名

**文件：** 改 `app/src/store/coreapi.ts`、`app/src/pages/syncstatus/index.vue`、`app/src/router/routes.ts`

- [ ] **步骤 1：`coreapi.ts` 新增 `SyncJobs()` 并导出**
在 `SyncStatus` 函数之后新增：
```typescript
  // 定时任务总览
  async function SyncJobs() {
    return http.request<any, Response<any>>('/api/config/SyncJobs', 'get').then(r => {
      return r;
    }).finally(() => {

    });
  }
```
并在 `return { ... }` 导出块里 `SyncStatus,` 之后加一行 `SyncJobs,`。

- [ ] **步骤 2：整体重写 `app/src/pages/syncstatus/index.vue` 为任务管理页**
```vue
<template>
  <div class="syncjobs" style="margin-top: 8px;">
    <a-card size="small">
      <template #title>定时任务</template>
      <template #extra>
        <a-button type="primary" ghost size="small" :loading="isTriggering" :disabled="anyRunning" @click="triggerAll">
          <SyncOutlined />立即同步(全部)
        </a-button>
        <a-button danger ghost size="small" :loading="isStopping" :disabled="!anyRunning" @click="stopAll" style="margin-left:8px;">
          <CloseOutlined />停止(全部)
        </a-button>
      </template>

      <a-table :data-source="jobs" :columns="columns" :pagination="false" row-key="type" size="small">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'state'">
            <a-tag v-if="record.running" color="processing">运行中</a-tag>
            <a-tag v-else-if="record.scheduled" color="default">已调度</a-tag>
            <a-tag v-else color="warning">未启用</a-tag>
          </template>
          <template v-else-if="column.key === 'next'">
            {{ record.scheduled ? formatTime(record.nextFireTime) : '—' }}
          </template>
          <template v-else-if="column.key === 'result'">
            <span v-if="record.running">下载 {{ record.downloaded }} · 失败 {{ record.failed }}（当前：{{ record.currentTitle || '—' }}）</span>
            <span v-else-if="record.endedAt">下载 {{ record.downloaded }} · 失败 {{ record.failed }} · {{ formatTime(record.endedAt) }}</span>
            <span v-else style="color:#bbb;">—</span>
          </template>
          <template v-else-if="column.key === 'op'">
            <a-button type="link" size="small" :disabled="anyRunning || !record.scheduled" @click="runOne(record)">立即执行</a-button>
            <a-button type="link" size="small" @click="openDetail(record)">详情</a-button>
          </template>
        </template>
      </a-table>

      <div v-if="recentLogs.length" style="margin-top:12px;max-height:160px;overflow:auto;border-top:1px solid #f0f0f0;padding-top:6px;">
        <div style="color:#888;font-size:12px;margin-bottom:4px;">最近同步日志</div>
        <div v-for="(log, i) in recentLogs" :key="i" style="font-size:12px;color:#666;line-height:1.6;">{{ log.text }}</div>
      </div>
    </a-card>

    <a-drawer :visible="detailOpen" :title="detail ? detail.name + ' · 详情' : '详情'" @close="detailOpen = false" width="380">
      <template v-if="detail">
        <p><b>类型：</b>{{ detail.type }}</p>
        <p><b>周期：</b>{{ detail.scheduleDesc }}</p>
        <p><b>状态：</b>{{ detail.running ? '运行中' : (detail.scheduled ? '已调度（' + detail.triggerState + '）' : '未启用') }}</p>
        <p><b>下次执行：</b>{{ detail.scheduled ? formatTime(detail.nextFireTime) : '—' }}</p>
        <p><b>上次执行：</b>{{ formatTime(detail.prevFireTime) || '—' }}</p>
        <p><b>最近结果：</b>
          <span v-if="detail.running">下载 {{ detail.downloaded }} · 失败 {{ detail.failed }}（运行中）</span>
          <span v-else-if="detail.endedAt">下载 {{ detail.downloaded }} · 失败 {{ detail.failed }} · 完成于 {{ formatTime(detail.endedAt) }}</span>
          <span v-else>—</span>
        </p>
      </template>
    </a-drawer>
  </div>
</template>

<script lang="ts" setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue';
import { useApiStore } from '@/store';
import { message } from 'ant-design-vue';
import { SyncOutlined, CloseOutlined } from '@ant-design/icons-vue';
import { useUnbounded } from '@/utils/useTheme';

useUnbounded();

const columns = [
  { title: '类型', dataIndex: 'name', key: 'name' },
  { title: '周期', dataIndex: 'scheduleDesc', key: 'sched' },
  { title: '下次执行', key: 'next' },
  { title: '状态', key: 'state' },
  { title: '最近结果', key: 'result' },
  { title: '操作', key: 'op' },
];

const jobs = ref<any[]>([]);
const recentLogs = ref<any[]>([]);
const anyRunning = ref(false);
const isTriggering = ref(false);
const isStopping = ref(false);
const detailOpen = ref(false);
const detail = ref<any>(null);
let timer: any = null;

const formatTime = (s: string) => {
  if (!s) return '';
  try { return new Date(s).toLocaleString(); } catch { return s; }
};

const fetchJobs = async () => {
  try {
    const res = await useApiStore().SyncJobs();
    if (res.code === 0 && res.data) jobs.value = res.data;
  } catch (e) { /* 静默 */ }
};

const fetchStatus = async () => {
  try {
    const res = await useApiStore().SyncStatus();
    if (res.code === 0 && res.data) {
      anyRunning.value = !!res.data.running;
      recentLogs.value = res.data.recentLogs || [];
    }
  } catch (e) { /* 静默 */ }
};

const refresh = () => { fetchJobs(); fetchStatus(); };

const triggerAll = () => {
  if (isTriggering.value || anyRunning.value) return;
  isTriggering.value = true;
  useApiStore().TriggerSyncNow()
    .then((res) => {
      if (res.code === 0) message.success(res.message || '已触发同步');
      else message.warning(res.message || '无法触发同步');
      refresh();
    })
    .catch(() => message.error('触发失败，请检查网络'))
    .finally(() => { isTriggering.value = false; });
};

const stopAll = () => {
  if (isStopping.value || !anyRunning.value) return;
  isStopping.value = true;
  useApiStore().StopSyncNow()
    .then((res) => {
      if (res.code === 0) message.success(res.message || '已发出停止指令');
      else message.warning(res.message || '当前没有正在执行的同步任务');
      refresh();
    })
    .catch(() => message.error('停止失败，请检查网络'))
    .finally(() => { isStopping.value = false; });
};

const runOne = (record: any) => {
  if (anyRunning.value) return;
  useApiStore().TriggerSyncNow(record.type)
    .then((res) => {
      if (res.code === 0) message.success(res.message || ('已触发：' + record.name));
      else message.warning(res.message || '无法触发');
      refresh();
    })
    .catch(() => message.error('触发失败，请检查网络'));
};

const openDetail = (record: any) => { detail.value = record; detailOpen.value = true; };

onMounted(() => { refresh(); timer = setInterval(refresh, 3000); });
onBeforeUnmount(() => { if (timer) clearInterval(timer); });
</script>

<style scoped lang="less">
.syncjobs {
}
</style>
```

- [ ] **步骤 3：菜单名改为「定时任务」**
在 `app/src/router/routes.ts` 中，把 `/syncstatus` 路由对象的 `name: '同步状态'` 改为 `name: '定时任务'`（其余 meta 不变；图标 `PlayCircleOutlined` 保留）。

- [ ] **步骤 4：前端构建（如本机 pnpm 可用）**
`cd app && pnpm exec vite build`（别动 lockfile/workspace）；不可用则严格自检语法/导入/字段，CI 验证。

- [ ] **步骤 5：Commit（仅这 3 个文件）**
```bash
git add app/src/store/coreapi.ts app/src/pages/syncstatus/index.vue app/src/router/routes.ts
git commit -m "feat(web): /syncstatus 改为定时任务管理页（列表+详情+单类型执行），菜单更名"
```

---

## 任务 4：文档 + 验证 + 推送

**文件：** 改 `tests/README.md`

- [ ] **步骤 1：README 加描述函数覆盖**
在覆盖表新增一行：
```markdown
| `SyncJobScheduleDescriber` | `SyncJobScheduleDescriberTests` | 周期描述：SimpleTrigger 间隔→"每 N 分钟"·Cron→"Cron: {expr}"·无效(无表达式/间隔≤0)→"自定义" |
```

- [ ] **步骤 2：全量构建 + 测试**
`dotnet build dy.net.csproj -nologo -clp:ErrorsOnly`（0 错误）；测试全绿。

- [ ] **步骤 3：Commit + 推送**
```bash
git add tests/README.md
git commit -m "docs(tests): SyncJobScheduleDescriber 覆盖"
git push
```

---

## 实现后人工验证（部署新镜像后）
- 左侧菜单「定时任务」，点进去是任务表格：7 行（收藏/喜欢/关注作品/关注列表/自定义收藏夹/合集/短剧），各显示周期、下次执行、状态、最近结果。
- 未启用的类型显示"未启用"、下次执行"—"。
- 点某行「立即执行」只触发该类型；运行中时按钮禁用。
- 顶部「立即同步(全部)/停止(全部)」互斥可用。
- 点「详情」弹抽屉显示完整调度 + 最近结果。
- 底部显示最近同步日志。

## 自检
- 规格点（列表 7 项含未启用、周期/下次执行/状态、最近结果合并、单类型执行、全局按钮、详情抽屉、日志、菜单更名、纯函数测试）均有任务覆盖 ✅
- 无占位符；代码步骤含完整代码 ✅
- 字段一致：后端 `SyncJobOverview`（type/name/scheduled/scheduleDesc/nextFireTime/prevFireTime/triggerState/running/downloaded/failed/currentTitle/endedAt，camelCase）与前端 record 字段一一对应 ✅
- 合并依赖 `SyncStatusSnapshot.Types` 含全部类型（每类型修复后已是）+ `TypeProgressSnapshot` 字段（Type/Running/Downloaded/Failed/CurrentTitle/EndedAt）✅
- `dy_followuser`(关注列表) 在 SyncRunState 无对应 → running/result 留默认，符合规格 ✅
