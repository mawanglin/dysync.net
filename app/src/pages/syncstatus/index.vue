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
            <a-button type="link" size="small" @click="openSchedule(record)">改周期</a-button>
            <a-button type="link" size="small" @click="openLogs(record)">执行记录</a-button>
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
  </div>
</template>

<script lang="ts" setup>
import { ref, onMounted, onBeforeUnmount } from 'vue';
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

onMounted(() => { refresh(); timer = setInterval(refresh, 3000); });
onBeforeUnmount(() => { if (timer) clearInterval(timer); });
</script>

<style scoped lang="less">
.syncjobs {
}
</style>
