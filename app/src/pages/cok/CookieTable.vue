<script lang="ts" setup>
import { getBase64 } from '@/utils/file';
import { FormInstance } from 'ant-design-vue';
import { reactive, ref, onMounted, UnwrapRef, watch, nextTick } from 'vue';
import dayjs from 'dayjs';
import { Dayjs } from 'dayjs';
import {
  EditFilled,
  DeleteFilled,
  SearchOutlined,
  PlusOutlined,
  ExclamationCircleOutlined,
  StopOutlined,
  ClockCircleOutlined,
  DeleteOutlined,
} from '@ant-design/icons-vue';
import { useApiStore } from '@/store';
import { message } from 'ant-design-vue';

import { StarOutlined, StarFilled, StarTwoTone } from '@ant-design/icons-vue';
const columns = ref([]);
columns.value = [
  {
    title: 'Cookie名称',
    dataIndex: 'userName',
    width: 180,
  },
  { title: 'Cookie状态', dataIndex: 'statusMsg' },
  { title: '收藏路径', dataIndex: 'savePath' },
  { title: '喜欢路径', dataIndex: 'favSavePath' },
  { title: '博主路径', dataIndex: 'upSavePath' },
  { title: '状态', dataIndex: 'status', width: 180 },
  { title: '操作', dataIndex: 'edit', width: 200 },
];

interface UpSecUserIdItem {
  uper?: string;
  uid?: string;
  syncAll: boolean;
}

type DataItem = {
  id?: string;
  userName?: string;
  cookies?: string;
  savePath?: string;
  favSavePath?: string;
  secUserId?: string;
  status?: number;
  _isNew?: boolean;
  upSecUserIdsJson?: UpSecUserIdItem[];
  upSecUserIds?: string;
  upSavePath?: string;
  useCollectFolder?: boolean;
  downMix?: boolean;
  downSeries?: boolean;
  mixPath?: string;
  seriesPath?: string;
  downCollect?: boolean;
  downFavorite?: boolean;
  downFollowd?: boolean;
};

const loading = ref(false);
const datas: UnwrapRef<DataItem[]> = reactive([]);
const pagination = ref({
  current: 1,
  defaultPageSize: 10,
  total: 0,
  showTotal: () => `共 ${0} 条`,
});

interface QuaryParam {
  pageIndex: number;
  pageSize: number;
}
const quaryData: UnwrapRef<QuaryParam> = reactive({
  pageIndex: 0,
  pageSize: 20,
});

const GetRecords = () => {
  loading.value = true;
  quaryData.pageIndex = pagination.value.current;
  quaryData.pageSize = pagination.value.defaultPageSize;
  useApiStore()
    .CookiePageList(quaryData)
    .then((res) => {
      loading.value = false;
      if (res.code === 0) {
        dataSource.value = res.data.data;
        pagination.value.current = res.data.pageIndex;
        pagination.value.defaultPageSize = res.data.pageSize;
        pagination.value.total = res.data.total;
        pagination.value.showTotal = () => `共 ${res.data.total} 条`;
      }
    });
};

function addNew() {
  showModal.value = true;
  form._isNew = true;
}

const showModal = ref(false);

const newCookie = (cookie?: DataItem) => {
  if (!cookie) {
    cookie = { _isNew: true };
  }
  cookie.userName = undefined;
  cookie.cookies = undefined;
  cookie.savePath = undefined;
  cookie.favSavePath = undefined;
  cookie.secUserId = undefined;
  cookie.status = 0;
  cookie.id = '0';
  cookie.upSecUserIdsJson = undefined;
  cookie.upSavePath = undefined;
  cookie.useCollectFolder = false;
  cookie.downMix = false;
  cookie.downSeries = false;
  cookie.mixPath = undefined;
  cookie.seriesPath = undefined;
  cookie.downCollect = false;
  cookie.downFavorite = false;
  cookie.downFollowd = false;
  return cookie;
};

const copyObject = (target: any, source?: any) => {
  if (!source) {
    return target;
  }
  Object.keys(target).forEach((key) => (target[key] = source[key]));
};

const form = reactive<DataItem>(newCookie());

function reset() {
  return newCookie(form);
}

function cancel() {
  showModal.value = false;
  reset();
}

const formModel = ref<FormInstance>();

const formLoading = ref(false);

function submit() {
  formLoading.value = true;

  formModel.value
    ?.validateFields()
    .then((resData: DataItem) => {
      if (form._isNew) {
      } else {
        copyObject(editRecord.value, resData);
      }
      useApiStore()
        .UpdateConfig(resData)
        .then((res) => {
          loading.value = false;
          if (res.code === 0) {
            showModal.value = false;
            message.success('保存成功，同步任务将在5-10秒后重新启动...');
            reset();
            GetRecords();
          } else {
            message.error('保存失败' + res.message);
          }
        });
    })
    .catch((e) => {
      console.error(e);
    })
    .finally(() => {
      formLoading.value = false;
    });
}

const editRecord = ref<DataItem>();

import { Modal } from 'ant-design-vue';

const deleted = (id: string) => {
  Modal.confirm({
    title: '确认删除',
    content: '确定要删除这条记录吗？此操作不可撤销。',
    okText: '确认',
    cancelText: '取消',
    onOk: () => {
      useApiStore()
        .deleteCookie(id)
        .then((res) => {
          loading.value = false;
          if (res.code === 0) {
            showModal.value = false;
            reset();
            GetRecords();
          }
        });
    },
    onCancel: () => {
      console.log('已取消删除');
    },
  });
};

function edit(record: DataItem) {
  cookieId.value = record.id;
  editRecord.value = record;
  console.log(record);
  copyObject(form, record);
  showModal.value = true;
}

const switchSyncStatus = (record: DataItem) => {
  const statusText = record.status === 1 ? '开启' : '停止';
  const title = `确认${statusText}同步`;
  const content = `确定要${statusText}【${record.userName || '该'}】Cookie的同步任务吗？`;

  Modal.confirm({
    title,
    content,
    okText: '确认',
    cancelText: '取消',
    onOk: () => {
      loading.value = true;
      useApiStore()
        .SwitchCookieStatus({
          id: record.id,
          status: record.status,
        })
        .then((res) => {
          loading.value = false;
          if (res.code === 0) {
            message.success(`${statusText}同步成功`);
            GetRecords();
          } else {
            message.error(`${statusText}同步失败：${res.message || '未知错误'}`);
            record.status = record.status === 1 ? 0 : 1;
          }
        })
        .catch((err) => {
          loading.value = false;
          console.error('切换同步状态失败：', err);
          message.error('切换同步状态失败，请稍后重试');
          record.status = record.status === 1 ? 0 : 1;
        });
    },
    onCancel: () => {
      console.log(`已取消${statusText}同步`);
      record.status = record.status === 1 ? 0 : 1;
    },
  });
};
const StatusDict = {
  0: '同步已停止',
  1: '同步已开启',
};

const dataSource = ref(datas);

const addRow = () => {
  if (!form.upSecUserIdsJson) {
    form.upSecUserIdsJson = [];
  }
  form.upSecUserIdsJson.push({ uper: '', uid: '', syncAll: false });
};
const removeRow = (index: number) => {
  if (form.upSecUserIdsJson) {
    form.upSecUserIdsJson.splice(index, 1);
  }
};
const rowCount = 10;

onMounted(() => {
  GetRecords();
});

const showDrawer = ref(false);
type DrawerType = 'collect' | 'mix' | 'series';

const cookieId = ref('');
const cateType = ref(5);

const drawerType = ref<DrawerType>('collect');
const drawerScrollRef = ref<HTMLDivElement | null>(null);
const drawerDataList = ref<DrawerItem[]>([]);
const drawerPagination = reactive({
  current: 1,
  pageSize: 10,
  total: 0,
  loading: false,
  hasMore: true,
});

interface DrawerItem {
  id: string;
  name: string;
  saveFolder: string;
  sync: boolean;
  coverUrl: string;
  cookieId: string;
  xId: string;
  total: number;
}

const openCollectFolderSetModal = () => {
  drawerType.value = 'collect';
  cateType.value = 5;
  openCommonDrawer();
};

const openMixDownSetModal = () => {
  drawerType.value = 'mix';
  cateType.value = 6;
  openCommonDrawer();
};

const openSeriesDownSetModal = () => {
  drawerType.value = 'series';
  cateType.value = 7;
  openCommonDrawer();
};

const openCommonDrawer = () => {
  drawerDataList.value = [];
  drawerPagination.current = 1;
  drawerPagination.total = 0;
  drawerPagination.hasMore = true;
  showDrawer.value = true;

  nextTick()
    .then(() => {
      loadDrawerData();
      bindDrawerScrollEvent();
    })
    .catch(() => {});
};

const bindDrawerScrollEvent = () => {
  const scrollContainer = drawerScrollRef.value;
  if (!scrollContainer) {
    setTimeout(() => {
      bindDrawerScrollEvent();
    }, 100);
    return;
  }

  scrollContainer.removeEventListener('scroll', handleDrawerScroll);
  scrollContainer.addEventListener('scroll', handleDrawerScroll);

  setTimeout(() => {
    handleDrawerScroll();
  }, 200);
};

const debounce = (func: Function, delay = 100) => {
  let timeoutId: any;
  return (...args: any[]) => {
    clearTimeout(timeoutId);
    timeoutId = setTimeout(() => func.apply(this, args), delay);
  };
};

const handleDrawerScroll = debounce(() => {
  const scrollContainer = drawerScrollRef.value;
  if (!scrollContainer) return;
  if (drawerPagination.loading || !drawerPagination.hasMore) return;

  const { scrollTop, scrollHeight, clientHeight } = scrollContainer;
  const isBottom = scrollTop + clientHeight + 50 >= scrollHeight;

  if (isBottom) {
    console.log('✅ 触底加载触发');
    drawerPagination.current += 1;
    loadDrawerData();
  }
}, 100);

const loadDrawerData = () => {
  if (drawerPagination.loading || !drawerPagination.hasMore) {
    console.log('🚫 阻止重复加载');
    return;
  }

  drawerPagination.loading = true;
  useApiStore()
    .CatePageList({
      cookieId: cookieId.value,
      cateType: cateType.value,
      pageIndex: drawerPagination.current,
      pageSize: drawerPagination.pageSize,
    })
    .then((res) => {
      if (res.code === 0) {
        const newData = res.data.data || [];
        drawerDataList.value = [...drawerDataList.value, ...newData];

        drawerPagination.total = res.data.total || 0;
        drawerPagination.hasMore = drawerDataList.value.length < drawerPagination.total;

        setTimeout(() => {
          handleDrawerScroll();
        }, 100);
      } else {
        message.error(res.message || '加载失败');
      }
    })
    .catch(() => {
      message.error('网络异常，加载失败');
    })
    .finally(() => {
      drawerPagination.loading = false;
    });
};

const getDrawerTypeName = () => {
  switch (drawerType.value) {
    case 'collect':
      return '收藏夹';
    case 'mix':
      return '合集';
    case 'series':
      return '短剧';
    default:
      return '';
  }
};

const toggleDrawerItemSync = (item: DrawerItem, index: number) => {
  if (drawerDataList.value[index]) {
    drawerDataList.value[index].sync = !item.sync;
    console.log(`切换${getDrawerTypeName()}【${item.name}】的同步状态为：${!item.sync}`);
  }
};

const closeDrawer = () => {
  showDrawer.value = false;
  const scrollContainer = drawerScrollRef.value;
  if (scrollContainer) {
    scrollContainer.removeEventListener('scroll', handleDrawerScroll);
  }
};

const saveDrawerData = () => {
  if (drawerDataList.value.length === 0) {
    message.info('暂无需要保存的配置数据');
    return;
  }
  drawerPagination.loading = true;
  useApiStore()
    .BatchSaveCate(drawerDataList.value)
    .then((res) => {
      if (res.code === 0) {
        showDrawer.value = false;
        message.success('保存成功');
      } else {
        message.error(res.message);
      }
    })
    .finally(() => {
      drawerPagination.loading = false;
    });
};

const switchdownCollect = (e: any) => {
  if (!e) form.useCollectFolder = e;
};
</script>

<template>
  <a-modal :title="form._isNew ? '新增' : '编辑'" v-model:visible="showModal" @ok="submit" @cancel="cancel" width="100%" wrap-class-name="full-modal">
    <a-form ref="formModel" :model="form" :labelCol="{ span: 3 }" :wrapperCol="{ span: 20 }">
      <a-form-item label="Cookie名称" required name="userName">
        <a-input v-model:value="form.userName" />
      </a-form-item>
      <a-form-item label="id" required name="id" v-show="false">
        <a-input v-model:value="form.id" />
      </a-form-item>
      <a-form-item label="Cookie值" name="cookies">
        <a-textarea v-model:value="form.cookies" :rows="rowCount" />
      </a-form-item>

      <a-form-item label="我的secUserId" name="secUserId">
        <div style="display: flex; align-items: center; gap: 6px;">
          <a-input v-model:value="form.secUserId" style="flex: 1;" placeholder="" />
          <a-tooltip title="如果要同步“我喜欢”的视频和关注列表时，必填！！！">
            <ExclamationCircleOutlined style="color: #faad14;font-size: 16px;" />
          </a-tooltip>
        </div>
      </a-form-item>

      <a-form-item label="下载收藏视频" name="downCollect">
        <div style="display: flex; align-items: center; gap: 12px;">
          <div class="form-item-div">
            <a-switch v-model:checked="form.downCollect" @change="switchdownCollect" :checked-value="true" :un-checked-value="false" size="default" />
            <a-form-item-rest v-if="form.downCollect">
              <a-form-item name="savePath" noStyle>
                <a-input v-model:value="form.savePath" placeholder='请输入容器路径' class="form-item-div-input" />
              </a-form-item>
            </a-form-item-rest>
          </div>
          <a-alert message="开启后自动下载默认收藏夹视频，记得填写映射路径（容器内部路径）" type="info" size="small" style="flex: 1; margin-bottom: 0;" />
        </div>
      </a-form-item>

      <a-form-item v-if="form.savePath && form.savePath.length>0" label="自定义收藏夹" name="useCollectFolder">
        <div style="display: flex; align-items: center; gap: 12px;">
          <div class="form-item-div">
            <a-switch v-model:checked="form.useCollectFolder" :checked-value="true" :un-checked-value="false" size="default" />
            <a-form-item-rest v-if="form.useCollectFolder">
              <a-input v-model:value="form.savePath" :disabled="form.useCollectFolder&&form.downCollect" placeholder="" class="form-item-div-input" />
              <a-button @click="openCollectFolderSetModal" shape="circle" type="dashed" style="margin-left:5px;" v-if="form.useCollectFolder">
                <star-outlined />
              </a-button>
            </a-form-item-rest>
          </div>
          <a-alert message="开启后自动下载自定义分类后的收藏夹，开启后不在下载默认收藏夹视频，存储路径与默认收藏夹存储路径一致" :type="form.useCollectFolder?'error':'info'" size="small" style="flex: 1; margin-bottom: 0;" />
        </div>
      </a-form-item>

      <a-form-item label="下载喜欢视频" name="downFavorite">
        <div style="display: flex; align-items: center; gap: 12px;">
          <div class="form-item-div">
            <a-switch v-model:checked="form.downFavorite" :checked-value="true" :un-checked-value="false" size="default" />
            <a-form-item-rest v-if="form.downFavorite">
              <a-form-item name="favSavePath" noStyle>
                <a-input v-model:value="form.favSavePath" placeholder='请输入容器路径' class="form-item-div-input" />
              </a-form-item>
              <a-button shape="circle" @click="()=>{message.success('别点了，这只是为了好看的😄')}" type="dashed" style="margin-left:5px;">
                <like-outlined />
              </a-button>
            </a-form-item-rest>
          </div>
          <a-alert message="开启后自动下载喜欢（点赞）的视频，记得填写映射路径（容器内部路径）" type="info" size="small" style="flex: 1; margin-bottom: 0;" />
        </div>
      </a-form-item>

      <a-form-item label="下载关注视频" name="downFollowd">
        <div style="display: flex; align-items: center; gap: 12px;">
          <div class="form-item-div">
            <a-switch v-model:checked="form.downFollowd" :checked-value="true" :un-checked-value="false" size="default" />
            <a-form-item-rest v-if="form.downFollowd">
              <a-form-item name="upSavePath" noStyle>
                <a-input v-model:value="form.upSavePath" placeholder='请输入容器路径' class="form-item-div-input" />
              </a-form-item>
              <a-button shape="circle" @click="()=>{message.success('别点了，这只是为了好看的😄')}" type="dashed" style="margin-left:5px;">
                <heart-outlined />
              </a-button>
            </a-form-item-rest>
          </div>
          <a-alert message="开启后自动下载关注的博主视频，记得填写映射路径（容器内部路径）" type="info" size="small" style="flex: 1; margin-bottom: 0;" />
        </div>
      </a-form-item>

      <a-form-item label="下载合集视频" name="downMix">
        <div style="display: flex; align-items: center; gap: 12px;">
          <div class="form-item-div">
            <a-switch v-model:checked="form.downMix" :checked-value="true" :un-checked-value="false" size="default" />
            <a-form-item-rest v-if="form.downMix">
              <a-form-item name="mixPath" noStyle>
                <a-input v-model:value="form.mixPath" class="form-item-div-input" placeholder='默认使用收藏夹路径' />
              </a-form-item>
              <a-button @click="openMixDownSetModal" shape="circle" type="dashed" style="margin-left:5px;">
                <gift-outlined />
              </a-button>
            </a-form-item-rest>
          </div>
          <a-alert message="开启后自动下载收藏的合集视频（还需要开启合集同步开关，不填目录径默认存储到收藏目录，设置后记得docker里面加映射，注意：付费视频下载后无法播放）" :type="form.downMix?'error':'info'" size="small" style="flex: 1; margin-bottom: 0;" />
        </div>
      </a-form-item>

      <a-form-item label="下载短剧视频" name="downSeries">
        <div style="display: flex; align-items: center; gap: 12px;">
          <div class="form-item-div">
            <a-switch v-model:checked="form.downSeries" :checked-value="true" :un-checked-value="false" size="default" />
            <a-form-item-rest v-if="form.downSeries">
              <a-form-item name="seriesPath" noStyle>
                <a-input v-model:value="form.seriesPath" placeholder='默认使用收藏夹路径' class="form-item-div-input" />
              </a-form-item>
              <a-button @click="openSeriesDownSetModal" shape="circle" type="dashed" style="margin-left:5px;">
                <fire-outlined />
              </a-button>
            </a-form-item-rest>
          </div>
          <a-alert message="开启后自动下载收藏的短剧视频（还需要开启短剧同步开关，不填目录径默认存储到收藏目录，设置后记得docker里面加映射，注意：付费视频下载后无法播放）" :type="form.downSeries?'error':'info'" size="small" style="flex: 1; margin-bottom: 0;" />
        </div>
      </a-form-item>

      <a-form-item label="任务同步状态" name="status">
        <div style="display: flex; align-items: center; gap: 8px;">
          <a-switch v-model:checked="form.status" :checked-value="1" :un-checked-value="0" size="default" />
          <span>{{ form.status === 1 ? '' : '' }}</span>
        </div>
      </a-form-item>
    </a-form>
  </a-modal>

  <a-drawer :title="getDrawerTypeName()+'配置'" v-model:visible="showDrawer" placement="right" width="800px" :z-index="10010" :mask-z-index="10009" @close="closeDrawer" class="common-drawer">
    <template #extra>
      <a-button type="primary" :loading="drawerPagination.loading" @click="saveDrawerData" class="drawer-save-btn">
        <template #icon>
          <SaveOutlined />
        </template>
        保存
      </a-button>
    </template>

    <div ref="drawerScrollRef" class="drawer-scroll-container">
      <div v-if="drawerPagination.loading && drawerDataList.length === 0" class="drawer-loading">
        <Spin size="large" />
      </div>

      <a-card v-else :bordered="false" class="drawer-card-container grid-container">
        <a-card-grid v-for="(item, index) in drawerDataList" :key="item.id" class="drawer-card-grid">
          <div class="grid-cover vertical-cover" v-if="drawerType!='collect'">
            <a-image :preview="false" :src="item.coverUrl" fit="cover" />
          </div>

          <div class="grid-item horizontal-item name">
            <label class="drawer-label">名称：</label>
            <span>{{ item.name || '未命名' }}</span>
          </div>

          <div class="grid-item horizontal-item save-folder">
            <label class="drawer-label">保存：</label>
            <a-input v-model:value="item.saveFolder" size="small" placeholder="默认用名称作文件夹" class="save-path-input" />
          </div>
          <div class="grid-item horizontal-item name">
            <label class="drawer-label">集数：</label>
            <span>{{ item.total || '0' }}</span>
          </div>

          <div class="grid-item horizontal-item sync-switch">
            <label class="drawer-label">同步：</label>
            <a-switch :checked="item.sync" @change="() => toggleDrawerItemSync(item, index)" size="small" />
          </div>
        </a-card-grid>
      </a-card>

      <div v-if="!drawerPagination.hasMore && drawerDataList.length > 0" class="no-more-data">
      </div>

      <div v-if="drawerPagination.loading && drawerDataList.length > 0" class="loading-more">
        <Spin size="small" />
        <span>加载中...</span>
      </div>
    </div>
  </a-drawer>

  <a-table v-bind="$attrs" :columns="columns" :dataSource="dataSource" :pagination="false">
    <template #title>
      <div class="flex justify-end pr-4">
        <a-button type="primary" @click="GetRecords()" :loading="formLoading" class="mr-2">
          <template #icon>
            <SearchOutlined />
          </template>
          查询
        </a-button>

        <a-button type="primary" @click="addNew" :loading="formLoading">
          <template #icon>
            <PlusOutlined />
          </template>
          新增
        </a-button>
      </div>
    </template>
    <template #bodyCell="{ column, text, record }">
      <template v-if="column.dataIndex === 'status'">
        <div style="display: flex; align-items: center; gap: 8px;">
          <a-switch v-model:checked="record.status" :checked-value="1" :un-checked-value="0" size="small" :disabled="loading" @change="() => switchSyncStatus(record)" />
          <span :style="{
    fontSize: '12px',
    color: record.status === 1 ? '#52c41a' : '#ff4d4f'
  }">
            {{ StatusDict[record.status] }}
          </span>
        </div>
      </template>
      <template v-else-if="column.dataIndex === 'edit'">
        <a-button :disabled="showModal || loading" type="link" @click="edit(record)">
          <template #icon>
            <EditFilled />
          </template>
          编辑
        </a-button>

        <a-button :disabled="loading" type="link" @click="deleted(record.id)" danger>
          <template #icon>
            <DeleteOutlined />
          </template>
          删除
        </a-button>
      </template>
      <div v-else class="text-subtext">
        {{ text }}
      </div>
    </template>
  </a-table>
</template>

<style scoped lang="less">
.ant-form-item {
  margin-bottom: 10px;
}
.form-item-div {
  width: 300px;
}
.form-item-div-input {
  width: 180px;
  margin-left: 10px;
}

:deep(.ant-input-textarea-input) {
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: rgba(150, 150, 150, 0.2) transparent;
}
:deep(.ant-input-textarea-input)::-webkit-scrollbar {
  width: 6px;
  height: 6px;
}
:deep(.ant-input-textarea-input)::-webkit-scrollbar-track {
  background: transparent;
}
:deep(.ant-input-textarea-input)::-webkit-scrollbar-thumb {
  background: rgba(150, 150, 150, 0.2);
  border-radius: 3px;
}
:deep(.ant-input-textarea-input)::-webkit-scrollbar-thumb:hover {
  background: rgba(150, 150, 150, 0.4);
}
:deep(.ant-input-textarea-input)::-webkit-scrollbar-corner {
  background: transparent;
}

:deep(.ant-input-disabled) {
  background-color: #f5f5f5 !important;
  color: #666 !important;
}

.alert-wrapper {
  flex: 1;
  margin-bottom: 0 !important;
}

.drawer-card-container.grid-container {
  :deep(.ant-card-body) {
    padding: 16px;
    margin: 0;
  }
  display: flex;
  flex-wrap: wrap;
  gap: 20px;
  box-sizing: border-box;
  justify-content: flex-start;
  min-height: 200px;
  padding-left: 5px;
}

.drawer-card-container.grid-container:has(:only-child) :deep(.drawer-card-grid) {
  width: calc(33.333% - 10.333px) !important;
  min-width: 200px;
}

:deep(.drawer-card-grid) {
  width: calc(33.333% - 10.333px) !important;
  min-width: 200px;
  margin: 5px !important;
  border-radius: 12px !important;
  padding: 16px !important;
  box-sizing: border-box;
  border: 1px solid #f0f0f0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

:deep(.drawer-card-grid .grid-cover.vertical-cover) {
  width: 100% !important;
  padding: 4px;
  box-sizing: border-box;
  border-radius: 8px;
  overflow: hidden;
}

:deep(.drawer-card-grid .vertical-cover .ant-image) {
  width: 100% !important;
  height: auto !important;
  display: block;
}
:deep(.drawer-card-grid .vertical-cover .ant-image-img) {
  width: 100% !important;
  height: 100% !important;
  object-fit: cover !important;
}

:deep(.drawer-card-grid .grid-item.horizontal-item) {
  width: 100% !important;
  display: flex;
  align-items: center;
  margin: 0 !important;
  padding: 4px 0;
}

:deep(.drawer-label) {
  flex-shrink: 0;
  font-size: 12px;
  color: rgba(0, 0, 0, 0.6);
}

:deep(.drawer-card-grid .horizontal-item span) {
  flex: 1;
  font-size: 12px;
  color: rgba(0, 0, 0, 0.88);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
:deep(.drawer-card-grid .horizontal-item .save-path-input) {
  flex: 1;
  width: 100% !important;
  font-size: 12px;
}

:deep(.drawer-card-grid .sync-switch) {
  justify-content: space-between;
}

@media (max-width: 768px) {
  :deep(.drawer-card-grid) {
    width: calc(50% - 10px) !important;
    min-width: 180px;
  }

  .drawer-card-container.grid-container:has(:only-child) :deep(.drawer-card-grid) {
    width: calc(50% - 10px) !important;
    min-width: 180px;
  }
}
@media (max-width: 480px) {
  :deep(.drawer-card-grid) {
    width: 100% !important;
    min-width: 100%;
  }

  .drawer-card-container.grid-container:has(:only-child) :deep(.drawer-card-grid) {
    width: 100% !important;
    min-width: 100%;
  }
}

html.dark-mode .drawer-card-container.grid-container .drawer-card-grid {
  border-color: rgba(142, 140, 140, 0.1) !important;
  background-color: #1a1a2e !important;
  box-shadow: 0 6px 16px rgba(0, 20, 60, 0.4), 0 2px 6px rgba(100, 120, 255, 0.2),
    inset 0 1px 0 rgba(255, 255, 255, 0.05) !important;
  transition: all 0.3s ease-in-out !important;
}

html.dark-mode .drawer-card-container.grid-container .drawer-card-grid:hover {
  box-shadow: 0 8px 24px rgba(0, 30, 80, 0.5), 0 4px 12px rgba(120, 140, 255, 0.35),
    inset 0 1px 0 rgba(255, 255, 255, 0.08) !important;
  transform: translateY(-2px) !important;
}

html.dark-mode .drawer-card-container.grid-container .drawer-card-grid .horizontal-item label {
  color: #ffffff !important;
}

html.dark-mode .drawer-card-container.grid-container .drawer-card-grid .horizontal-item span {
  color: rgba(255, 255, 255, 0.88) !important;
}

html.dark-mode .drawer-card-container.grid-container .drawer-card-grid .ant-input {
  background-color: #2f2f2f !important;
  border-color: #404040 !important;
  color: rgba(255, 255, 255, 0.88) !important;
}
</style>

<style lang="less">
.full-modal {
  .ant-modal {
    max-width: 100%;
    top: 0;
    padding-bottom: 0;
    margin: 0;
  }
  .ant-modal-content {
    display: flex;
    flex-direction: column;
    height: calc(100vh);
  }
  .ant-modal-body {
    flex: 1;
  }
}

.ant-alert {
  box-sizing: border-box;
  margin: 0;
  color: rgba(0, 0, 0, 0.88);
  font-size: 14px;
  line-height: 1.5714285714;
  list-style: none;
  position: relative;
  display: flex;
  align-items: center;
  border-radius: 8px;
}
.ant-alert-small {
  padding: 8px 16px;
  font-size: 12px;
  border-radius: 4px;
}
.ant-alert-info {
  background-color: #e6f4ff;
  border: 1px solid #91caff;
}
.ant-alert-info .ant-alert-message {
  color: #1677ff;
}
.ant-alert-success {
  background-color: #daf1d3;
  border: 1px solid #5bbc51;
}
.ant-alert-success .ant-alert-message {
  color: #228b22;
}

.ant-alert-error {
  background-color: #fff1f0;
  border: 1px solid #ff4d4f;
}
.ant-alert-error .ant-alert-message {
  color: #f5222d;
}

.ant-alert-warning {
  background-color: #fffbe6;
  border: 1px solid #ffe58f;
}
.ant-alert-warning .ant-alert-message {
  color: #faad14;
}
.ant-alert-info .ant-alert-icon {
  color: #1677ff;
}
.ant-alert-warning .ant-alert-icon {
  color: #faad14;
}

.drawer-scroll-container {
  width: 100%;
  height: calc(100vh - 130px) !important;
  min-height: 300px !important;
  overflow-y: auto !important;
  overflow-x: hidden !important;
  padding: 0 8px;
  box-sizing: border-box;
  position: relative;
  z-index: 1;

  &::-webkit-scrollbar {
    width: 6px;
  }
  &::-webkit-scrollbar-track {
    background: transparent;
  }
  &::-webkit-scrollbar-thumb {
    background: rgba(0, 0, 0, 0.15);
    border-radius: 3px;
  }
  html.dark-mode &::-webkit-scrollbar-thumb {
    background: rgba(255, 255, 255, 0.2);
  }
}

.drawer-loading {
  display: flex;
  justify-content: center;
  align-items: center;
  height: 200px;
  width: 100%;
}

.loading-more {
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 16px 0;
  font-size: 12px;
  color: rgba(0, 0, 0, 0.6);

  html.dark-mode & {
    color: rgba(255, 255, 255, 0.6);
  }
}

.no-more-data {
  text-align: center;
  padding: 16px 0;
  font-size: 12px;
  color: rgba(0, 0, 0, 0.45);

  html.dark-mode & {
    color: rgba(255, 255, 255, 0.45);
  }
}
</style>