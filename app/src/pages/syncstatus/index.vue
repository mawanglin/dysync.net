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
