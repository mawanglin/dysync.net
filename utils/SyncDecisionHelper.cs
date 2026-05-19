using dy.net.model.dto;
using dy.net.model.entity;
using dy.net.model.response;
using Serilog;

namespace dy.net.utils
{
    /// <summary>
    /// 从 DouyinBasicSyncJob 抽出的无 I/O 纯决策逻辑。
    /// 行为逐字保留，仅把原抽象属性 VideoType 提升为入参。
    /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
    /// </summary>
    public static class SyncDecisionHelper
    {
        public static string GetNextCursor(DouyinVideoInfoResponse data)
        {
            return data?.Cursor ?? (data?.MaxCursor ?? "0");
        }

        public static bool IsAwemeValid(Aweme item)
            => item != null && item.Video != null && item.Video.BitRate != null;

        public static (string tag1, string tag2, string tag3) GetVideoTags(Aweme item)
        {
            var tags = item.VideoTags;
            return (
                tags?.FirstOrDefault(x => x.Level == 1)?.TagName,
                tags?.FirstOrDefault(x => x.Level == 2)?.TagName,
                tags?.FirstOrDefault(x => x.Level == 3)?.TagName
            );
        }

        public static bool IsSyncLimitReached(VideoTypeEnum videoType, DouyinCookie cookie,
            AppConfig config, int syncCount, DouyinCollectCate cate, DouyinFollowed followed)
        {
            if (cate != null && cate.CateType != VideoTypeEnum.dy_custom_collect)
            {
                if (syncCount >= 30)
                {
                    Log.Debug($"[{cookie.UserName}][{videoType.GetDesc()}]:本次同步数量{syncCount}，等下次任务继续同步");
                    return true;
                }
            }
            else
            {
                if (syncCount >= config.BatchCount)
                {
                    Log.Debug($"[{cookie.UserName}][{videoType.GetDesc()}]:本次同步数量{syncCount}，已达配置上限{config.BatchCount}，等下次任务继续同步");
                    return true;
                }
            }
            if (videoType == VideoTypeEnum.dy_collects || videoType == VideoTypeEnum.dy_favorite)
                return config.OnlySyncNew;

            return videoType == VideoTypeEnum.dy_follows && !followed.FullSync;
        }
    }
}
