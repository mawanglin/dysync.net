<template>
  <a-modal
    :visible="visible"
    title="扫码登录抖音"
    :footer="null"
    :maskClosable="false"
    width="360px"
    @cancel="handleClose"
  >
    <div style="text-align: center; padding: 12px 0;">
      <a-spin v-if="loading" tip="正在启动浏览器..." />

      <template v-else>
        <div v-if="qrImage" style="position: relative; display: inline-block;">
          <img :src="qrImage" alt="登录二维码" style="width: 220px; height: 220px;" />
          <div
            v-if="statusText === 'expired'"
            style="position:absolute;inset:0;background:rgba(255,255,255,.9);display:flex;flex-direction:column;align-items:center;justify-content:center;"
          >
            <span style="margin-bottom:8px;">二维码已失效</span>
            <a-button type="primary" size="small" @click="start">刷新二维码</a-button>
          </div>
        </div>

        <p style="margin-top: 12px; color: #666;">{{ hint }}</p>

        <a-button v-if="statusText === 'error'" type="primary" @click="start">重试</a-button>
      </template>
    </div>
  </a-modal>
</template>

<script lang="ts" setup>
import { ref, watch, onBeforeUnmount } from 'vue';
import { message } from 'ant-design-vue';
import { useApiStore } from '@/store';

const props = defineProps<{ visible: boolean }>();
const emit = defineEmits<{
  (e: 'update:visible', v: boolean): void;
  (e: 'success', data: { cookies: string; secUserId?: string; userName?: string; myUserId?: string }): void;
}>();

const loading = ref(false);
const qrImage = ref('');
const sessionId = ref('');
const statusText = ref('');
let timer: ReturnType<typeof setInterval> | null = null;

const hint = ref('请用抖音 App 扫描二维码并确认登录');

function stopPoll() {
  if (timer) {
    clearInterval(timer);
    timer = null;
  }
}

async function start() {
  stopPoll();
  loading.value = true;
  qrImage.value = '';
  statusText.value = '';
  hint.value = '请用抖音 App 扫描二维码并确认登录';
  try {
    const res = await useApiStore().QrLoginStart();
    if (res.code === 0 && res.data?.sessionId) {
      sessionId.value = res.data.sessionId;
      qrImage.value = res.data.qrImageBase64;
      statusText.value = 'waiting';
      timer = setInterval(poll, 1500);
      console.log('[qrlogin] 已启动轮询 sessionId=', sessionId.value, ' timer=', timer);
    } else {
      statusText.value = 'error';
      hint.value = res.message || '启动失败';
    }
  } catch (e: any) {
    statusText.value = 'error';
    hint.value = e?.data?.erro || e?.message || '启动扫码失败';
  } finally {
    loading.value = false;
  }
}

async function poll() {
  console.log('[qrlogin] poll tick sessionId=', sessionId.value);
  if (!sessionId.value) return;
  try {
    const res = await useApiStore().QrLoginPoll(sessionId.value);
    console.log('[qrlogin] status=', res.data?.status, ' cookies=', res.data?.debug);
    if (res.code !== 0 || !res.data) return;
    const st = res.data.status as string;
    statusText.value = st;
    if (st === 'success') {
      stopPoll();
      message.success('扫码登录成功');
      emit('success', {
        cookies: res.data.cookies,
        secUserId: res.data.secUserId,
        userName: res.data.userName,
        myUserId: res.data.myUserId,
      });
      emit('update:visible', false);
    } else if (st === 'expired') {
      stopPoll();
      hint.value = '二维码已失效，请刷新';
    } else if (st === 'error' || st === 'notfound') {
      stopPoll();
      hint.value = '登录会话异常，请重试';
    }
  } catch (e) {
    console.warn('[qrlogin] poll 异常', e);
  }
}

async function handleClose() {
  stopPoll();
  if (sessionId.value) {
    try { await useApiStore().QrLoginCancel(sessionId.value); } catch { /* ignore */ }
    sessionId.value = '';
  }
  emit('update:visible', false);
}

watch(
  () => props.visible,
  (v) => {
    if (v) start();
    else { stopPoll(); }
  }
);

onBeforeUnmount(() => {
  stopPoll();
});
</script>
