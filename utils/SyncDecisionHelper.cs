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

        /// <summary>
        /// 从 DouyinBasicSyncJob.CreateVideoEntity 抽出的纯字段映射。
        /// 行为逐字保留：VideoType→videoType；IdGener.GetLong()→id；DateTime.Now→syncTime。
        /// 不含 DynamicVideos 赋值与 NfoFileGenerator 文件写入（那段 if/else 留在 job）。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static DouyinVideo BuildVideoEntity(VideoTypeEnum videoType, AppConfig config,
            DouyinCookie cookie, Aweme item, VideoBitRate bitRate, string savePath,
            string coverSavePath, string avatorPath, string id, DateTime syncTime,
            DouyinCollectCate cate)
        {
            // 获取视频标签
            var (tag1, tag2, tag3) = GetVideoTags(item);
            var video = new DouyinVideo
            {
                ViedoType = videoType,
                AwemeId = item.AwemeId,
                Author = item.Author?.Nickname,
                AuthorId = item.Author?.Uid,
                AuthorAvatar = avatorPath,
                AuthorAvatarUrl = item.Author.AvatarLarger?.UrlList?.FirstOrDefault() ?? item.Author.AvatarThumb?.UrlList?.FirstOrDefault(),
                CreateTime = DateTimeUtil.Convert10BitTimestamp(item.CreateTime),
                VideoTitle = string.IsNullOrWhiteSpace(item.Desc) ? $"{item.Author?.Nickname}-{item.CreateTime}" : item.Desc,
                //VideoTitleSimplify = VideoType == VideoTypeEnum.dy_follows? GetVideoSimplifyTitle(item):string.Empty,
                Id = id,
                Resolution = $"{bitRate.PlayAddr.Width}×{bitRate.PlayAddr.Height}",
                FileSize = bitRate.PlayAddr.DataSize ?? 0,
                FileHash = bitRate.PlayAddr.FileHash,
                Tag1 = tag1,
                Tag2 = tag2,
                Tag3 = tag3,
                VideoUrl = bitRate.PlayAddr.UrlList?.FirstOrDefault(),
                VideoCoverUrl = item.Video.Cover.UrlList?.FirstOrDefault(),
                VideoSavePath = savePath,
                VideoCoverSavePath = coverSavePath,
                SyncTime = syncTime,
                DyUserId = item.AuthorUserId == 0 ? item.Author?.Uid : item.AuthorUserId.ToString(),
                CookieId = cookie.Id,
                OnlyImgOrOnlyMp3 = string.IsNullOrWhiteSpace(savePath) && !config.DownImageVideo && (config.DownImage || config.DownMp3),
                CateId = cate?.Id,
                CateXId = cate?.XId,
            };
            if (cate != null && cate.CateType != VideoTypeEnum.dy_custom_collect)
            {
                video.VideoTitle = (string.IsNullOrWhiteSpace(item.Desc) ? cate.Name : $"[{cate.Name}]" + "_" + item.Desc) + "_" + item.MixInfo.Statis.CurrentEpisode;
            }
            return video;
        }
    }
}
