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

        /// <summary>
        /// 从 DouyinBasicSyncJob.GetBestMatchedVideoUrl 抽出的纯码流选择逻辑。
        /// 行为逐字保留：encoder=265 时优先 H.265，无则回退 H.264；
        /// 否则只挑 H.264。二者均按 BitRateValue 降序取首；
        /// 只考虑 PlayAddr.UrlList 非空（非 null 且 Any()）的码流。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static VideoBitRate PickBestVideoBitRate(Aweme item, AppConfig config)
        {
            VideoBitRate v;
            if (config.VideoEncoder.HasValue && config.VideoEncoder.Value == 265)
            {
                v = item.Video.BitRate.Where(v => v.IsH265 == 1 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                                .OrderByDescending(v => v.BitRateValue)
                                .FirstOrDefault();
                v ??= item.Video.BitRate.Where(v => v.IsH265 == 0 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                                .OrderByDescending(v => v.BitRateValue)
                                .FirstOrDefault();
            }
            else
            {
                v = item.Video.BitRate.Where(v => v.IsH265 == 0 && v.PlayAddr?.UrlList != null && v.PlayAddr.UrlList.Any())
                                  .OrderByDescending(v => v.BitRateValue)
                                  .FirstOrDefault();
            }
            return v;
        }

        /// <summary>
        /// 从 DouyinBasicSyncJob.GetVideoFileName 抽出的纯文件名构造逻辑。
        /// 行为逐字保留：cate=custom_collect 用 BitRate.Format（或 mp4 兜底）；
        /// videoType=dy_series/dy_mix 且 MixInfo?.Statis?.CurrentEpisode 链非 null
        /// → "S01E{D2}.mp4"；其余 → "{AwemeId}.mp4"。
        /// 原方法 cookie/config 参数在 base body 中未引用，故 helper 签名不带这两项。
        /// 抽象属性 VideoType 提升为 videoType 入参。
        /// 「TryParse 失败」分支在当前 model（MixStatis.CurrentEpisode 为 int）下不可达；
        /// 保留原代码不删，但不为其编写特征化测试。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static string BuildVideoFileName(VideoTypeEnum videoType, Aweme item, DouyinCollectCate cate)
        {
            if (cate != null && cate.CateType == VideoTypeEnum.dy_custom_collect)
            {
                if (item.Video != null && item.Video.BitRate != null)
                    return $"{item.AwemeId}.{item.Video.BitRate.FirstOrDefault().Format}";
                return $"{item.AwemeId}.mp4";
            }
            else
            {
                if ((videoType == VideoTypeEnum.dy_series || videoType == VideoTypeEnum.dy_mix) && item.MixInfo?.Statis?.CurrentEpisode != null)
                {
                    // 第一步：将 CurrentEpisode 转换为整数（兼容字符串/数字类型）
                    if (int.TryParse(item.MixInfo.Statis.CurrentEpisode.ToString(), out int episodeNum))
                    {
                        // 第二步：格式化数字，确保 1-9 补 0，10+ 保持原样
                        string episodeStr = episodeNum.ToString("D2");
                        return $"S01E{episodeStr}.mp4";
                    }
                    // 容错：如果转换失败，使用原始值（避免程序报错）
                    return $"S01E{item.MixInfo.Statis.CurrentEpisode}.mp4";
                }
                return $"{item.AwemeId}.mp4";
            }
        }
    }
}
