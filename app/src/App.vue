<template>
  <ThemeProvider is-root v-bind="themeConfig" :apply-style="false">
    <stepin-view system-name="抖小云" :class="`${contentClass}`" :user="user" :navMode="navigation" :useTabs="useTabs" :themeList="themeList" v-model:show-setting="showSetting" v-model:theme="theme" @themeSelect="configTheme" logo-src="@/assets/logo.png" v-model:collapsed="open">
      <template #headerActions>
        <HeaderActions @showSetting="showSetting = true" />
      </template>
      <template #pageFooter>
        <PageFooter />
      </template>
      <template #themeEditorTab>
        <a-tab-pane tab="其它" key="other">
          <Setting />
        </a-tab-pane>
      </template>

    </stepin-view>
  </ThemeProvider>
  <my-personal ref="personalRef" />
</template>

<script lang="ts" setup>
import { reactive, ref, computed, onMounted, watch, nextTick } from 'vue';
import { message } from 'ant-design-vue';
import { useRouter } from 'vue-router';
import { useAccountStore, useMenuStore, useSettingStore, storeToRefs, useApiStore } from '@/store';
import avatar from '@/assets/avatar.png';
import { PageFooter, HeaderActions } from '@/components/layout';
import Setting from './components/setting';
import { LoginModal } from '@/pages/login';
import { MyPersonal } from '@/pages/personal';
import { configTheme, themeList } from '@/theme';
import { ThemeProvider } from 'stepin';
// logout,profile
const { logout } = useAccountStore();
const showPersonalDrawer = ref<boolean>(false);
const personalRef = ref(null);
const emailRef = ref(null);
const open = ref(false);

const showSetting = ref(false);
const router = useRouter();

// review #1：默认凭据首登后强制提示改密——登录响应回传 mustChangePwd 为 true 时，
// 提示并打开个人设置抽屉（默认停在改密 tab）。软提示，抽屉可关闭；标记由后端在
// 改密成功后清除，重新登录刷新。
const accountStore = useAccountStore();
watch(
  () => accountStore.mustChangePwd,
  (must) => {
    if (must) {
      message.warning('首次登录请立即修改默认密码');
      nextTick(() => personalRef.value?.show(true));
    }
  }
);

// useMenuStore().getMenuList();

const { navigation, useTabs, theme, contentClass } = storeToRefs(useSettingStore());
const themeConfig = computed(() => themeList.find((item) => item.key === theme.value)?.config ?? {});

const user = reactive({
  name: 'douyin',
  avatar: avatar,
  menuList: [
    // { title: '个人中心', key: 'personal', icon: 'UserOutlined', onClick: () => router.push('/profile') },
    // { title: '设置', key: 'setting', icon: 'SettingOutlined', onClick: () => (showSetting.value = true) },
    // { type: 'divider' },
    {
      title: '个人设置',
      key: 'seting',
      icon: 'SmileOutlined',
      onClick: () => {
        personalRef.value.show(true);
      },
    },
    { type: 'divider' },

    // {
    //   title: '邮件通知',
    //   key: 'email',
    //   icon: 'BellOutlined',
    //   onClick: () => {
    //     emailRef.value.showEmail(true);
    //   },
    // },
    { type: 'divider' },
    {
      title: '退出登录',
      key: 'logout',
      icon: 'LogoutOutlined',
      onClick: () => logout().then(() => router.push('/login')),
    },
  ],
});

onMounted(() => {
  useApiStore()
    .apiUserInfo()
    .then((res) => {
      if (res.code === 0) {
        user.name = res.data?.userName;
      }
    });
});
</script>

<style lang="less">
.stepin-view {
  ::-webkit-scrollbar {
    width: 4px;
    height: 4px;
    border-radius: 4px;
    background-color: theme('colors.primary.500');
  }

  ::-webkit-scrollbar-thumb {
    border-radius: 4px;
    background-color: theme('colors.primary.400');

    &:hover {
      background-color: theme('colors.primary.500');
    }
  }

  ::-webkit-scrollbar-track {
    box-shadow: inset 0 0 1px rgba(0, 0, 0, 0);
    border-radius: 4px;
    background: theme('backgroundColor.layout');
  }
  .system-name {
    color: #722ed1 !important;
    margin-left: 0em !important;
  }
  .ant-tabs-extra-content {
    display: none !important;
  }
}

html {
  height: 100vh;
  overflow-y: hidden;
}

body {
  margin: 0;
  height: 100vh;
  overflow-y: hidden;
}
.stepin-img-checkbox {
  @apply transition-transform;
  &:hover {
    @apply scale-105 ~"-translate-y-[2px]";
  }
  img {
    @apply shadow-low rounded-md transition-transform;
  }
}
</style>
