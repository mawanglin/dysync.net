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

        /// <summary>
        /// 从 DouyinBasicSyncJob.CreateSaveFolder 抽出的纯路径构造逻辑（无 I/O）。
        /// 行为逐字保留：item.Desc/AwemeId 经 SanitizeLinuxFileName 清洗为子目录名，
        /// 返回两条候选路径——primary（cookie.SavePath/子目录）与 collisionResolved
        /// （撞名时的 cookie.SavePath/子目录_AwemeId）。
        /// 目录存在性判断与 Directory.CreateDirectory 的 I/O 编排留在 job 内。
        /// 原方法 config/followed/cate 参数在 base body 中未引用，故 helper 签名不带这三项。
        /// 两条候选一并求值；SanitizeLinuxFileName 与 Path.Combine 均为纯函数，
        /// 提前求值与原方法 else 分支的延迟求值可观察行为等价。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static (string primary, string collisionResolved) BuildVideoSaveFolderCandidates(DouyinCookie cookie, Aweme item)
        {
            var subFolder = DouyinFileNameHelper.SanitizeLinuxFileName(item.Desc, item.AwemeId, true);
            return (
                Path.Combine(cookie.SavePath, subFolder),
                Path.Combine(cookie.SavePath, subFolder + "_" + item.AwemeId)
            );
        }

        /// <summary>
        /// 从 DouyinBasicSyncJob.DownVideoCover(Aweme,...) 抽出的纯封面 URL 选取逻辑（无 I/O）。
        /// 行为逐字保留：cate 非 null → MixInfo → Video（LastOrDefault）→ Music 三级兜底；
        /// cate == null → Video（FirstOrDefault），空白则回落 Images[0].DynamicVideo.Cover。
        /// 注意 cate 分支对 item.Video/Cover 无 ?. 空安全（与非-cate 分支不对称）——既有行为，
        /// 逐字保留；分支内的中文注释与代码实际顺序不符，亦逐字保留不修。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static string PickCoverUrl(DouyinCollectCate cate, Aweme item)
        {
            // 定义封面URL变量
            string coverUrl;

            // 按照优先级获取封面URL
            if (cate is not null)
            {
                // cate不为空时：优先MixInfo封面 → 其次Music高清封面 → 最后Video封面
                coverUrl = item.MixInfo?.CoverUrl?.UrlList?.FirstOrDefault()
                           ?? item.Video.Cover.UrlList?.LastOrDefault()
                           ?? item.Music?.CoverHd?.UrlList?.FirstOrDefault();
            }
            else
            {
                // cate为空时：只取Video封面
                coverUrl = item.Video?.Cover?.UrlList?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(coverUrl))
                {
                    coverUrl = item.Images?.FirstOrDefault()?.DynamicVideo?.Cover?.UrlList?.FirstOrDefault();
                }
            }
            return coverUrl;
        }

        /// <summary>
        /// 从 DouyinBasicSyncJob.DownVideoCover(string,...) 抽出的纯海报路径派生逻辑（无 I/O）。
        /// 行为逐字保留：dy_mix/dy_series → 同目录 "poster.jpg"；其余 → "{无后缀原名}-poster.jpg"。
        /// 抽象属性 VideoType 提升为 videoType 入参。File.Exists / DownloadAsync 的 I/O 留在 job。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static string BuildCoverPosterPath(VideoTypeEnum videoType, string savePath)
        {
            string directoryPath = Path.GetDirectoryName(savePath); // 获取文件所在目录，
            string newFileName = "poster.jpg";
            if (videoType != VideoTypeEnum.dy_mix && videoType != VideoTypeEnum.dy_series)
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(savePath); // 获取无后缀的原文件名，
                newFileName = $"{fileNameWithoutExt}-poster.jpg"; // 拼接新文件名，
            }

            var coverSavePath = Path.Combine(directoryPath, newFileName);
            return coverSavePath;
        }

        /// <summary>
        /// 从 DouyinBasicSyncJob.AutoDistinct 抽出的纯优先级去重判定（无 I/O）。
        /// 行为逐字保留：priorityLevels 为空 → 默认最高优先级 {Id=1,Sort=1}（即 dy_favorite）；
        /// 否则取 Sort 最小者为最高优先级。判定见方法体四层嵌套 if/else。
        /// 抽象属性 VideoType 提升为 currentType 入参。
        /// 注意 priorityLevels 为 null 时 .Any() 会抛 NRE（与原 priLevs.Any() 逐字一致）——
        /// 既有行为，不加守卫。JsonConvert 反序列化、DeleteOldViedo/DeleteById 的 I/O 留在 job。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static DuplicateVideoAction ResolveDuplicateVideoAction(
            VideoTypeEnum currentType,
            VideoTypeEnum exitVideoType,
            List<PriorityLevelDto> priorityLevels)
        {
            // 4. 处理优先级：获取「最高优先级」（Sort 越小优先级越高）
            PriorityLevelDto maxPriority = null;
            if (priorityLevels.Any())
            {
                // 前端已配置优先级：取 Sort 最小的（1最高）
                maxPriority = priorityLevels.OrderBy(x => x.Sort).FirstOrDefault();
            }
            else
            {
                // 前端未配置：使用默认优先级（喜欢 > 收藏 > 关注）
                maxPriority = new PriorityLevelDto { Id = 1, Sort = 1, Name = "喜欢的" }; // 默认「喜欢的视频」最高
            }

            // 5. 转换为当前上下文的视频类型
            var maxPriorityType = (VideoTypeEnum)maxPriority.Id; // 配置的最高优先级类型

            // 7. 优先级逻辑判断（核心）
            if (currentType == maxPriorityType)
            {
                // 情况1：当前要下载的是「最高优先级」视频
                if (exitVideoType == currentType)
                {
                    // 已存在同优先级视频 → 跳过下载（避免重复）
                    return DuplicateVideoAction.SkipDownload;
                }
                else
                {
                    // 已存在「低优先级」视频 → 替换（删除旧文件，继续下载新的最高优先级视频）
                    return DuplicateVideoAction.ReplaceExisting;
                }
            }
            else
            {
                // 情况2：当前要下载的是「非最高优先级」视频
                if (exitVideoType == maxPriorityType)
                {
                    // 已存在「最高优先级」视频 → 跳过（不替换最高优先级）
                    return DuplicateVideoAction.SkipDownload;
                }
                else
                {
                    // 已存在「其他非最高优先级」视频 → 比较两者优先级
                    var currentSort = priorityLevels.FirstOrDefault(x => x.Id == (int)currentType)?.Sort ?? int.MaxValue;
                    var exitSort = priorityLevels.FirstOrDefault(x => x.Id == (int)exitVideoType)?.Sort ?? int.MaxValue;

                    if (currentSort < exitSort)
                    {
                        // 当前类型优先级更高 → 替换旧视频
                        return DuplicateVideoAction.ReplaceExisting;
                    }
                    else
                    {
                        // 当前类型优先级更低或相等 → 跳过
                        return DuplicateVideoAction.SkipDownload;
                    }
                }
            }
        }

        /// <summary>
        /// 从 DouyinBasicSyncJob.DownAuthorAvatar 抽出的纯头像 URL 选取逻辑（无 I/O）。
        /// 行为逐字保留：优先高清 AvatarLarger，回落 AvatarThumb，各取 UrlList 首个。
        /// 注意对 item.Author 无 ?. 空安全——原代码 Author==null 守卫先跑，调用方（job 薄壳）
        /// 保留该守卫并负责只在 Author 非 null 时调用；逐字保留不补守卫。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static string PickAuthorAvatarUrl(Aweme item)
        {
            // 优先获取高清头像
            return item.Author.AvatarLarger?.UrlList?.FirstOrDefault() ?? item.Author.AvatarThumb?.UrlList?.FirstOrDefault();
        }

        /// <summary>
        /// 从 DouyinBasicSyncJob.ProcessVideoList 抽出的纯动态视频 URL 构建逻辑（无 I/O）。
        /// 行为逐字保留：遍历 item.Images → 每个 DynamicVideo.BitRate → 取 PlayAddr.UrlList
        /// 中首个以 https://www.douyin.com/aweme/v1/play 打头的 URL，命中则构造
        /// DouyinMergeVideoDto { Path, Height, Width } 入列；Images 为 null/空时返回空 list。
        /// 调用方（job 薄壳）保留 config.DownDynamicVideo 开关，仅在开关开启时调用本方法。
        /// PlayAddr.Height/Width 为非空 int，?? 1920 / ?? 1080 兜底为不可达死代码，逐字保留不删。
        /// 由特征化测试 SyncDecisionHelperTests 锁定当前行为。
        /// </summary>
        public static List<DouyinMergeVideoDto> BuildDynamicVideoUrls(Aweme item)
        {
            List<DouyinMergeVideoDto> dynamicVideoUrls = new List<DouyinMergeVideoDto>();
            // 当需要下载动态视频时，获取其他URL
            if (item.Images != null && item.Images.Count > 0)
            {
                foreach (var img in item.Images)
                {
                    if (img.DynamicVideo?.BitRate?.Count > 0)
                    {
                        foreach (var btv in img.DynamicVideo.BitRate)
                        {
                            var targetUrl = btv.PlayAddr?.UrlList?.FirstOrDefault(x => x.StartsWith("https://www.douyin.com/aweme/v1/play"));
                            if (targetUrl != null)
                            {
                                var height = btv.PlayAddr?.Height ?? 1920;
                                var width = btv.PlayAddr?.Width ?? 1080;
                                DouyinMergeVideoDto info = new DouyinMergeVideoDto
                                {
                                    Path = targetUrl,
                                    Height = height,
                                    Width = width
                                };
                                dynamicVideoUrls.Add(info);
                            }
                        }
                    }
                }
            }
            return dynamicVideoUrls;
        }
    }
}
