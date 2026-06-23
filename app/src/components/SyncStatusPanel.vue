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
