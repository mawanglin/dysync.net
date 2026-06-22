using dy.net.model.dto;
using dy.net.model.entity;
using dy.net.model.response;
using dy.net.utils;

namespace dy.net.Tests
{
    // Golden-master / characterization: pins SyncDecisionHelper's CURRENT behavior
    // (logic moved verbatim out of DouyinBasicSyncJob). A failure here means the
    // extraction changed behavior — fix the helper, never weaken the assertion.
    public class SyncDecisionHelperTests
    {
        // ---- GetNextCursor ----

        [Fact]
        public void GetNextCursor_prefers_Cursor()
            => Assert.Equal("c1", SyncDecisionHelper.GetNextCursor(
                new DouyinVideoInfoResponse { Cursor = "c1", MaxCursor = "m1" }));

        [Fact]
        public void GetNextCursor_falls_back_to_MaxCursor_when_Cursor_null()
            => Assert.Equal("m1", SyncDecisionHelper.GetNextCursor(
                new DouyinVideoInfoResponse { Cursor = null, MaxCursor = "m1" }));

        [Fact]
        public void GetNextCursor_returns_zero_when_both_null()
            => Assert.Equal("0", SyncDecisionHelper.GetNextCursor(
                new DouyinVideoInfoResponse { Cursor = null, MaxCursor = null }));

        [Fact]
        public void GetNextCursor_returns_zero_when_data_null()
            => Assert.Equal("0", SyncDecisionHelper.GetNextCursor(null));

        // ---- IsAwemeValid ----

        [Fact]
        public void IsAwemeValid_false_when_item_null()
            => Assert.False(SyncDecisionHelper.IsAwemeValid(null));

        [Fact]
        public void IsAwemeValid_false_when_video_null()
            => Assert.False(SyncDecisionHelper.IsAwemeValid(new Aweme { Video = null }));

        [Fact]
        public void IsAwemeValid_false_when_bitrate_null()
            => Assert.False(SyncDecisionHelper.IsAwemeValid(
                new Aweme { Video = new Video { BitRate = null } }));

        [Fact]
        public void IsAwemeValid_true_when_all_present()
            => Assert.True(SyncDecisionHelper.IsAwemeValid(
                new Aweme { Video = new Video { BitRate = new List<VideoBitRate>() } }));

        // ---- GetVideoTags ----

        [Fact]
        public void GetVideoTags_picks_level_1_2_3()
        {
            var item = new Aweme
            {
                VideoTags = new List<VideoTagItem>
                {
                    new VideoTagItem { Level = 1, TagName = "一级" },
                    new VideoTagItem { Level = 2, TagName = "二级" },
                    new VideoTagItem { Level = 3, TagName = "三级" },
                }
            };
            Assert.Equal(("一级", "二级", "三级"), SyncDecisionHelper.GetVideoTags(item));
        }

        [Fact]
        public void GetVideoTags_missing_level_yields_null_for_that_level()
        {
            var item = new Aweme
            {
                VideoTags = new List<VideoTagItem>
                {
                    new VideoTagItem { Level = 1, TagName = "一级" },
                    new VideoTagItem { Level = 3, TagName = "三级" },
                }
            };
            Assert.Equal(("一级", (string)null, "三级"), SyncDecisionHelper.GetVideoTags(item));
        }

        [Fact]
        public void GetVideoTags_null_list_yields_all_null()
            => Assert.Equal(((string)null, (string)null, (string)null),
                SyncDecisionHelper.GetVideoTags(new Aweme { VideoTags = null }));

        // ---- IsSyncLimitReached ----

        private static DouyinCookie Cookie() => new DouyinCookie { UserName = "u" };

        [Fact]
        public void IsSyncLimitReached_noncustom_cate_over_30_returns_true()
        {
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_mix, Cookie(), new AppConfig(), 30,
                new DouyinCollectCate { CateType = VideoTypeEnum.dy_mix }, null);
            Assert.True(r);
        }

        [Fact]
        public void IsSyncLimitReached_noncustom_cate_under_30_returns_false()
        {
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_mix, Cookie(), new AppConfig { BatchCount = 18 }, 29,
                new DouyinCollectCate { CateType = VideoTypeEnum.dy_mix }, null);
            Assert.False(r);
        }

        [Fact]
        public void IsSyncLimitReached_null_cate_over_batchcount_returns_true()
        {
            // videoType irrelevant here; null cate -> else (BatchCount) branch
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_favorite, Cookie(),
                new AppConfig { BatchCount = 18 }, 18, null, null);
            Assert.True(r);
        }

        [Fact]
        public void IsSyncLimitReached_custom_cate_over_batchcount_returns_true()
        {
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_custom_collect, Cookie(),
                new AppConfig { BatchCount = 5 }, 5,
                new DouyinCollectCate { CateType = VideoTypeEnum.dy_custom_collect }, null);
            Assert.True(r);
        }

        [Fact]
        public void IsSyncLimitReached_under_limit_collects_returns_OnlySyncNew_true()
        {
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_collects, Cookie(),
                new AppConfig { BatchCount = 18, OnlySyncNew = true }, 0, null, null);
            Assert.True(r);
        }

        [Fact]
        public void IsSyncLimitReached_under_limit_favorite_returns_OnlySyncNew_false()
        {
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_favorite, Cookie(),
                new AppConfig { BatchCount = 18, OnlySyncNew = false }, 0, null, null);
            Assert.False(r);
        }

        [Fact]
        public void IsSyncLimitReached_follows_not_full_sync_returns_true()
            => Assert.True(SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_follows, Cookie(), new AppConfig { BatchCount = 18 }, 0,
                null, new DouyinFollowed { FullSync = false }));

        [Fact]
        public void IsSyncLimitReached_follows_full_sync_returns_false()
            => Assert.False(SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_follows, Cookie(), new AppConfig { BatchCount = 18 }, 0,
                null, new DouyinFollowed { FullSync = true }));

        [Fact]
        public void IsSyncLimitReached_under_limit_mix_short_circuits_to_false()
        {
            // videoType != dy_collects/dy_favorite and != dy_follows:
            // expression `videoType == dy_follows && !followed.FullSync` short-circuits
            // on the first term, returns false, never dereferences followed (null safe).
            var r = SyncDecisionHelper.IsSyncLimitReached(
                VideoTypeEnum.dy_mix, Cookie(), new AppConfig { BatchCount = 18 }, 0,
                null, null);
            Assert.False(r);
        }

        // ---- BuildVideoEntity (golden-master: pins CreateVideoEntity's pure mapping) ----

        private static readonly DateTime FixedSync = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Local);
        private const string FixedId = "ID-1";

        private static DouyinCookie Ck() => new DouyinCookie { Id = "ck1", UserName = "u" };

        private static Aweme StdAweme() => new Aweme
        {
            AwemeId = "aw1",
            AuthorUserId = 0,
            CreateTime = 1700000000,
            Desc = "hello",
            Author = new Author
            {
                Nickname = "nick",
                Uid = "uid9",
                AvatarLarger = new ImageInfo { UrlList = new List<string> { "large.jpg" } },
                AvatarThumb = new ImageInfo { UrlList = new List<string> { "thumb.jpg" } },
            },
            Video = new Video { Cover = new ImageInfo { UrlList = new List<string> { "cover.jpg" } } },
            VideoTags = new List<VideoTagItem>
            {
                new VideoTagItem { Level = 1, TagName = "t1" },
                new VideoTagItem { Level = 2, TagName = "t2" },
                new VideoTagItem { Level = 3, TagName = "t3" },
            },
            MixInfo = new MixInfo { Statis = new MixStatis { CurrentEpisode = 7 } },
        };

        private static VideoBitRate StdBitRate() => new VideoBitRate
        {
            PlayAddr = new PlayAddr
            {
                Width = 1920,
                Height = 1080,
                DataSize = 12345,
                FileHash = "fh",
                UrlList = new List<string> { "v1.mp4", "v2.mp4" },
            }
        };

        [Fact]
        public void BuildVideoEntity_maps_core_fields()
        {
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, new AppConfig(), Ck(), StdAweme(), StdBitRate(),
                "save.mp4", "cover-save.jpg", "av-path", FixedId, FixedSync, null);

            Assert.Equal(VideoTypeEnum.dy_collects, v.ViedoType);
            Assert.Equal("aw1", v.AwemeId);
            Assert.Equal("nick", v.Author);
            Assert.Equal("uid9", v.AuthorId);
            Assert.Equal("av-path", v.AuthorAvatar);
            Assert.Equal("large.jpg", v.AuthorAvatarUrl);
            Assert.Equal(DateTimeUtil.Convert10BitTimestamp(1700000000), v.CreateTime);
            Assert.Equal("hello", v.VideoTitle);
            Assert.Equal("ID-1", v.Id);
            Assert.Equal("1920×1080", v.Resolution);
            Assert.Equal(12345L, v.FileSize);
            Assert.Equal("fh", v.FileHash);
            Assert.Equal("t1", v.Tag1);
            Assert.Equal("t2", v.Tag2);
            Assert.Equal("t3", v.Tag3);
            Assert.Equal("v1.mp4", v.VideoUrl);
            Assert.Equal("cover.jpg", v.VideoCoverUrl);
            Assert.Equal("save.mp4", v.VideoSavePath);
            Assert.Equal("cover-save.jpg", v.VideoCoverSavePath);
            Assert.Equal(FixedSync, v.SyncTime);
            Assert.Equal("uid9", v.DyUserId);
            Assert.Equal("ck1", v.CookieId);
            Assert.False(v.OnlyImgOrOnlyMp3);
            Assert.Null(v.CateId);
            Assert.Null(v.CateXId);
        }

        [Fact]
        public void BuildVideoEntity_title_falls_back_to_nickname_dash_createtime_when_desc_blank()
        {
            var a = StdAweme();
            a.Desc = "   ";
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, new AppConfig(), Ck(), a, StdBitRate(),
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.Equal("nick-1700000000", v.VideoTitle);
        }

        [Fact]
        public void BuildVideoEntity_cate_noncustom_overrides_title_with_desc()
        {
            var cate = new DouyinCollectCate
            {
                Id = "cid",
                XId = "cxid",
                Name = "MyCate",
                CateType = VideoTypeEnum.dy_mix,
            };
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_mix, new AppConfig(), Ck(), StdAweme(), StdBitRate(),
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, cate);
            Assert.Equal("[MyCate]_hello_7", v.VideoTitle);
            Assert.Equal("cid", v.CateId);
            Assert.Equal("cxid", v.CateXId);
        }

        [Fact]
        public void BuildVideoEntity_cate_noncustom_overrides_title_when_desc_blank()
        {
            var a = StdAweme();
            a.Desc = null;
            var cate = new DouyinCollectCate
            {
                Id = "cid",
                XId = "cxid",
                Name = "MyCate",
                CateType = VideoTypeEnum.dy_series,
            };
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_series, new AppConfig(), Ck(), a, StdBitRate(),
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, cate);
            Assert.Equal("MyCate_7", v.VideoTitle);
            Assert.Equal("cid", v.CateId);
            Assert.Equal("cxid", v.CateXId);
        }

        [Fact]
        public void BuildVideoEntity_OnlyImgOrOnlyMp3_true_when_savepath_blank_and_downmp3()
        {
            var cfg = new AppConfig { DownImageVideo = false, DownImage = false, DownMp3 = true };
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, cfg, Ck(), StdAweme(), StdBitRate(),
                "", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.True(v.OnlyImgOrOnlyMp3);
        }

        [Fact]
        public void BuildVideoEntity_OnlyImgOrOnlyMp3_false_when_downimagevideo_true()
        {
            var cfg = new AppConfig { DownImageVideo = true, DownImage = true, DownMp3 = true };
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, cfg, Ck(), StdAweme(), StdBitRate(),
                "", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.False(v.OnlyImgOrOnlyMp3);
        }

        [Fact]
        public void BuildVideoEntity_DyUserId_uses_AuthorUserId_when_nonzero()
        {
            var a = StdAweme();
            a.AuthorUserId = 42;
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, new AppConfig(), Ck(), a, StdBitRate(),
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.Equal("42", v.DyUserId);
        }

        [Fact]
        public void BuildVideoEntity_AuthorAvatarUrl_falls_back_to_thumb_when_larger_null()
        {
            var a = StdAweme();
            a.Author.AvatarLarger = null;
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, new AppConfig(), Ck(), a, StdBitRate(),
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.Equal("thumb.jpg", v.AuthorAvatarUrl);
        }

        [Fact]
        public void BuildVideoEntity_FileSize_zero_when_datasize_null()
        {
            var br = StdBitRate();
            br.PlayAddr.DataSize = null;
            var v = SyncDecisionHelper.BuildVideoEntity(
                VideoTypeEnum.dy_collects, new AppConfig(), Ck(), StdAweme(), br,
                "save.mp4", "cs.jpg", "ap", FixedId, FixedSync, null);
            Assert.Equal(0L, v.FileSize);
        }

        // ---- PickBestVideoBitRate ----

        // pin: current behavior, not aspirational

        private static VideoBitRate Br(int bitRateValue, int isH265, List<string> urlList)
            => new VideoBitRate
            {
                BitRateValue = bitRateValue,
                IsH265 = isH265,
                PlayAddr = new PlayAddr { UrlList = urlList },
            };

        private static VideoBitRate BrNoPlayAddr(int bitRateValue, int isH265)
            => new VideoBitRate { BitRateValue = bitRateValue, IsH265 = isH265, PlayAddr = null };

        private static Aweme AwemeWith(params VideoBitRate[] bitRates)
            => new Aweme { Video = new Video { BitRate = bitRates.ToList() } };

        [Fact]
        public void PickBestVideoBitRate_H265Preferred_PicksHighestH265()
        {
            var h265Lo = Br(1000, 1, new List<string> { "a" });
            var h265Hi = Br(5000, 1, new List<string> { "a" });
            var h264Hi = Br(9000, 0, new List<string> { "a" });
            var item = AwemeWith(h265Lo, h265Hi, h264Hi);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = 265 });
            Assert.Same(h265Hi, picked);
        }

        [Fact]
        public void PickBestVideoBitRate_H265Preferred_FallsBackToH264_WhenNoPlayableH265()
        {
            var h265Empty = Br(8000, 1, new List<string>());
            var h265Null = BrNoPlayAddr(7000, 1);
            var h264Lo = Br(2000, 0, new List<string> { "a" });
            var h264Hi = Br(6000, 0, new List<string> { "a" });
            var item = AwemeWith(h265Empty, h265Null, h264Lo, h264Hi);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = 265 });
            Assert.Same(h264Hi, picked);
        }

        [Fact]
        public void PickBestVideoBitRate_H265Preferred_ReturnsNull_WhenNothingPlayable()
        {
            var h265Empty = Br(5000, 1, new List<string>());
            var h264NullUrl = Br(4000, 0, null);
            var h264NoPlayAddr = BrNoPlayAddr(3000, 0);
            var item = AwemeWith(h265Empty, h264NullUrl, h264NoPlayAddr);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = 265 });
            Assert.Null(picked);
        }

        [Fact]
        public void PickBestVideoBitRate_DefaultEncoder_PicksHighestH264()
        {
            var h265Hi = Br(9000, 1, new List<string> { "a" });
            var h264Lo = Br(2000, 0, new List<string> { "a" });
            var h264Hi = Br(6000, 0, new List<string> { "a" });
            var item = AwemeWith(h265Hi, h264Lo, h264Hi);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = null });
            Assert.Same(h264Hi, picked);
        }

        [Fact]
        public void PickBestVideoBitRate_DefaultEncoder_NeverReturnsH265()
        {
            var h265Hi = Br(9000, 1, new List<string> { "a" });
            var h264Hi = Br(6000, 0, new List<string> { "a" });
            var item = AwemeWith(h265Hi, h264Hi);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = null });
            Assert.Same(h264Hi, picked);
        }

        [Fact]
        public void PickBestVideoBitRate_EncoderNot265_PicksH264Only()
        {
            // VideoEncoder.HasValue=true 但 != 265 → 走 else 分支（同默认）
            var h265Hi = Br(9000, 1, new List<string> { "a" });
            var h264Hi = Br(6000, 0, new List<string> { "a" });
            var item = AwemeWith(h265Hi, h264Hi);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = 264 });
            Assert.Same(h264Hi, picked);
        }

        [Fact]
        public void PickBestVideoBitRate_SkipsBitRatesWithNullOrEmptyUrlList()
        {
            // 故意让"不可播放"的码率更高，验证它们被过滤而不是入选。
            var h264Empty = Br(9999, 0, new List<string>());
            var h264NullList = Br(8888, 0, null);
            var h264NoPlayAddr = BrNoPlayAddr(7777, 0);
            var h264Playable = Br(100, 0, new List<string> { "x" });
            var item = AwemeWith(h264Empty, h264NullList, h264NoPlayAddr, h264Playable);
            var picked = SyncDecisionHelper.PickBestVideoBitRate(
                item, new AppConfig { VideoEncoder = null });
            Assert.Same(h264Playable, picked);
        }

        // ---- BuildVideoFileName ----

        // pin: current behavior, not aspirational

        private static Aweme AwemeWithId(string awemeId)
            => new Aweme { AwemeId = awemeId };

        private static Aweme AwemeWithEpisode(string awemeId, int episode)
            => new Aweme
            {
                AwemeId = awemeId,
                MixInfo = new MixInfo { Statis = new MixStatis { CurrentEpisode = episode } },
            };

        private static Aweme AwemeWithBitRateFormat(string awemeId, string format)
            => new Aweme
            {
                AwemeId = awemeId,
                Video = new Video { BitRate = new List<VideoBitRate> { new VideoBitRate { Format = format } } },
            };

        private static Aweme AwemeWithVideoNull(string awemeId)
            => new Aweme { AwemeId = awemeId, Video = null };

        private static Aweme AwemeWithBitRateNull(string awemeId)
            => new Aweme { AwemeId = awemeId, Video = new Video { BitRate = null } };

        private static DouyinCollectCate Cate(VideoTypeEnum cateType)
            => new DouyinCollectCate { CateType = cateType };

        private static Aweme AwemeWithBitRatePlayAddr(string awemeId, string format, int height, int width)
            => new Aweme
            {
                AwemeId = awemeId,
                Video = new Video
                {
                    BitRate = new List<VideoBitRate>
                    {
                        new VideoBitRate
                        {
                            Format = format,
                            PlayAddr = new PlayAddr { FileHash = "hash", Height = height, Width = width },
                        },
                    },
                },
            };

        private static AppConfig EmptyTemplateConfig()
            => new AppConfig { FullFollowedTitleTemplate = null };

        [Fact]
        public void BuildVideoFileName_CustomCollect_WithBitRate_UsesFormat()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_custom_collect,
                AwemeWithBitRateFormat("123", "webm"),
                Cate(VideoTypeEnum.dy_custom_collect),
                EmptyTemplateConfig());
            Assert.Equal("123.webm", name);
        }

        [Fact]
        public void BuildVideoFileName_CustomCollect_VideoNull_Mp4Fallback()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_custom_collect,
                AwemeWithVideoNull("123"),
                Cate(VideoTypeEnum.dy_custom_collect),
                EmptyTemplateConfig());
            Assert.Equal("123.mp4", name);
        }

        [Fact]
        public void BuildVideoFileName_CustomCollect_BitRateNull_Mp4Fallback()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_custom_collect,
                AwemeWithBitRateNull("123"),
                Cate(VideoTypeEnum.dy_custom_collect),
                EmptyTemplateConfig());
            Assert.Equal("123.mp4", name);
        }

        [Fact]
        public void BuildVideoFileName_Series_NumericEpisode_S01E_D2_Padded()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_series,
                AwemeWithEpisode("123", 5),
                cate: null,
                EmptyTemplateConfig());
            Assert.Equal("S01E05.mp4", name);
        }

        [Fact]
        public void BuildVideoFileName_Mix_NumericEpisode_S01E_D2_NotPadded()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_mix,
                AwemeWithEpisode("123", 12),
                cate: null,
                EmptyTemplateConfig());
            Assert.Equal("S01E12.mp4", name);
        }

        [Fact]
        public void BuildVideoFileName_DefaultBranch_VideoNull_AwemeIdMp4_WhenTemplateEmpty()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_follows,
                AwemeWithId("123"),
                cate: null,
                EmptyTemplateConfig());
            Assert.Equal("123.mp4", name);
        }

        [Fact]
        public void BuildVideoFileName_CateNonCustomCollect_FollowsDefaultBranch()
        {
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_collects,
                AwemeWithId("123"),
                Cate(VideoTypeEnum.dy_collects),
                EmptyTemplateConfig());
            Assert.Equal("123.mp4", name);
        }

        [Fact]
        public void BuildVideoFileName_DefaultBranch_EmptyTemplate_UsesBitRateFormat()
        {
            // pin master 行为：默认分支后缀取 BitRate.Format，而非固定 mp4
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_follows,
                AwemeWithBitRatePlayAddr("123", "webm", 1080, 1920),
                cate: null,
                EmptyTemplateConfig());
            Assert.Equal("123.webm", name);
        }

        [Fact]
        public void BuildVideoFileName_DefaultBranch_WithTemplate_UsesGeneratedName()
        {
            // pin master 行为：FullFollowedTitleTemplate 非空时用 VideoTitleGenerator 生成主名
            var item = AwemeWithBitRatePlayAddr("123", "webm", 1080, 1920);
            item.Desc = "标题";
            item.Author = new Author { Nickname = "作者" };
            var config = new AppConfig { FullFollowedTitleTemplate = "vid_{id}" };
            var name = SyncDecisionHelper.BuildVideoFileName(
                VideoTypeEnum.dy_follows, item, cate: null, config);
            Assert.Equal("vid_123.webm", name);
        }

        // ---- BuildVideoSaveFolderCandidates ----

        // pin: current behavior, not aspirational

        private static DouyinCookie FolderCookie(string savePath)
            => new DouyinCookie { SavePath = savePath };

        private static Aweme FolderAweme(string desc, string awemeId)
            => new Aweme { Desc = desc, AwemeId = awemeId };

        [Fact]
        public void BuildVideoSaveFolderCandidates_Primary_CombinesSavePathWithSanitizedDesc()
        {
            var (primary, _) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(
                FolderCookie("/data"),
                FolderAweme("我的视频", "123"));

            var expected = Path.Combine(
                "/data",
                DouyinFileNameHelper.SanitizeLinuxFileName("我的视频", "123", true));
            Assert.Equal(expected, primary);
        }

        [Fact]
        public void BuildVideoSaveFolderCandidates_CollisionResolved_AppendsUnderscoreAwemeId()
        {
            var (_, collisionResolved) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(
                FolderCookie("/data"),
                FolderAweme("我的视频", "123"));

            var subFolder = DouyinFileNameHelper.SanitizeLinuxFileName("我的视频", "123", true);
            var expected = Path.Combine("/data", subFolder + "_" + "123");
            Assert.Equal(expected, collisionResolved);
        }

        [Fact]
        public void BuildVideoSaveFolderCandidates_BlankDesc_FallsBackToAwemeIdAsSubFolder()
        {
            var (primary, collisionResolved) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(
                FolderCookie("/data"),
                FolderAweme("", "123"));

            var subFolder = DouyinFileNameHelper.SanitizeLinuxFileName("", "123", true);
            Assert.Equal(Path.Combine("/data", subFolder), primary);
            Assert.Equal(Path.Combine("/data", subFolder + "_" + "123"), collisionResolved);
        }

        [Fact]
        public void BuildVideoSaveFolderCandidates_IllegalChars_SanitizedIntoSubFolder()
        {
            var (primary, _) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(
                FolderCookie("/data"),
                FolderAweme("a/b:c", "123"));

            var expected = Path.Combine(
                "/data",
                DouyinFileNameHelper.SanitizeLinuxFileName("a/b:c", "123", true));
            Assert.Equal(expected, primary);
        }

        [Fact]
        public void BuildVideoSaveFolderCandidates_BothCandidates_ShareSameSanitizedSubFolder()
        {
            var (primary, collisionResolved) = SyncDecisionHelper.BuildVideoSaveFolderCandidates(
                FolderCookie("/data"),
                FolderAweme("我的视频", "123"));

            // collisionResolved 恰为 primary 词根 + "_" + AwemeId（仅后缀不同，词根一致）
            Assert.Equal(primary + "_" + "123", collisionResolved);
        }

        // ---- PickCoverUrl ----

        // pin: current behavior, not aspirational

        private static ImageInfo CoverImg(params string[] urls)
            => new ImageInfo { UrlList = urls.ToList() };

        [Fact]
        public void PickCoverUrl_Cate_MixInfoCover_TakesMixInfoFirst()
        {
            var item = new Aweme
            {
                MixInfo = new MixInfo { CoverUrl = CoverImg("m1", "m2") },
                Video = new Video { Cover = CoverImg("v1") },
            };
            var url = SyncDecisionHelper.PickCoverUrl(new DouyinCollectCate(), item);
            Assert.Equal("m1", url);
        }

        [Fact]
        public void PickCoverUrl_Cate_NoMixInfo_TakesVideoCoverLast()
        {
            var item = new Aweme
            {
                MixInfo = null,
                Video = new Video { Cover = CoverImg("v1", "v2") },
            };
            var url = SyncDecisionHelper.PickCoverUrl(new DouyinCollectCate(), item);
            Assert.Equal("v2", url);
        }

        [Fact]
        public void PickCoverUrl_Cate_MixNullAndVideoCoverUrlsNull_TakesMusicCoverHd()
        {
            var item = new Aweme
            {
                MixInfo = null,
                Video = new Video { Cover = new ImageInfo { UrlList = null } },
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var url = SyncDecisionHelper.PickCoverUrl(new DouyinCollectCate(), item);
            Assert.Equal("mu1", url);
        }

        [Fact]
        public void PickCoverUrl_NoCate_TakesVideoCoverFirst()
        {
            var item = new Aweme
            {
                Video = new Video { Cover = CoverImg("v1", "v2") },
            };
            var url = SyncDecisionHelper.PickCoverUrl(cate: null, item);
            Assert.Equal("v1", url);
        }

        [Fact]
        public void PickCoverUrl_NoCate_BlankVideoCover_FallsBackToImages()
        {
            var item = new Aweme
            {
                Video = new Video { Cover = CoverImg("") },
                Images = new List<ImageItemInfo>
                {
                    new ImageItemInfo { DynamicVideo = new Video { Cover = CoverImg("img1") } },
                },
            };
            var url = SyncDecisionHelper.PickCoverUrl(cate: null, item);
            Assert.Equal("img1", url);
        }

        [Fact]
        public void PickCoverUrl_NoCate_AllNull_ReturnsNull()
        {
            var item = new Aweme { Video = null, Images = null };
            var url = SyncDecisionHelper.PickCoverUrl(cate: null, item);
            Assert.Null(url);
        }

        // ---- BuildCoverPosterPath ----

        // pin: current behavior, not aspirational

        [Fact]
        public void BuildCoverPosterPath_Mix_UsesPlainPosterJpg()
        {
            var path = SyncDecisionHelper.BuildCoverPosterPath(VideoTypeEnum.dy_mix, "/data/v/123.mp4");
            var expected = Path.Combine(Path.GetDirectoryName("/data/v/123.mp4"), "poster.jpg");
            Assert.Equal(expected, path);
        }

        [Fact]
        public void BuildCoverPosterPath_Series_UsesPlainPosterJpg()
        {
            var path = SyncDecisionHelper.BuildCoverPosterPath(VideoTypeEnum.dy_series, "/data/v/123.mp4");
            var expected = Path.Combine(Path.GetDirectoryName("/data/v/123.mp4"), "poster.jpg");
            Assert.Equal(expected, path);
        }

        [Fact]
        public void BuildCoverPosterPath_OtherType_UsesFileNamePrefixedPoster()
        {
            var path = SyncDecisionHelper.BuildCoverPosterPath(VideoTypeEnum.dy_collects, "/data/v/123.mp4");
            var dir = Path.GetDirectoryName("/data/v/123.mp4");
            var nameNoExt = Path.GetFileNameWithoutExtension("/data/v/123.mp4");
            var expected = Path.Combine(dir, $"{nameNoExt}-poster.jpg");
            Assert.Equal(expected, path);
        }

        // ---- ResolveDuplicateVideoAction ----
        // pin: current behavior, not aspirational

        private static List<PriorityLevelDto> Levels(params (int id, int sort)[] entries)
            => entries.Select(e => new PriorityLevelDto { Id = e.id, Sort = e.sort }).ToList();

        [Fact]
        public void ResolveDuplicateVideoAction_EmptyLevels_CurrentFavoriteExitFavorite_Skips()
        {
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_favorite, VideoTypeEnum.dy_favorite, new List<PriorityLevelDto>());
            Assert.Equal(DuplicateVideoAction.SkipDownload, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_EmptyLevels_CurrentFavoriteExitCollects_Replaces()
        {
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_favorite, VideoTypeEnum.dy_collects, new List<PriorityLevelDto>());
            Assert.Equal(DuplicateVideoAction.ReplaceExisting, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_EmptyLevels_CurrentCollectsExitFavorite_Skips()
        {
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_collects, VideoTypeEnum.dy_favorite, new List<PriorityLevelDto>());
            Assert.Equal(DuplicateVideoAction.SkipDownload, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_ConfiguredHighestIsCurrent_ExitLower_Replaces()
        {
            // maxPriority = Sort-min = {Id=3} → dy_follows; current == max, exit != current
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_follows, VideoTypeEnum.dy_collects, Levels((3, 1), (2, 2)));
            Assert.Equal(DuplicateVideoAction.ReplaceExisting, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_NeitherIsHighest_CurrentSortSmaller_Replaces()
        {
            // max = {Id=1}; current dy_collects Sort=2 < exit dy_follows Sort=3
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_collects, VideoTypeEnum.dy_follows, Levels((1, 1), (2, 2), (3, 3)));
            Assert.Equal(DuplicateVideoAction.ReplaceExisting, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_NeitherIsHighest_CurrentSortLarger_Skips()
        {
            // max = {Id=1}; current dy_follows Sort=3 >= exit dy_collects Sort=2
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_follows, VideoTypeEnum.dy_collects, Levels((1, 1), (2, 2), (3, 3)));
            Assert.Equal(DuplicateVideoAction.SkipDownload, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_NeitherIsHighest_EqualSort_Skips()
        {
            // max = {Id=1}; current Sort=5, exit Sort=5; 5 < 5 is false → skip
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_follows, VideoTypeEnum.dy_collects, Levels((1, 1), (2, 5), (3, 5)));
            Assert.Equal(DuplicateVideoAction.SkipDownload, action);
        }

        [Fact]
        public void ResolveDuplicateVideoAction_CurrentTypeMissingFromList_FallsBackToMaxValueSort_Skips()
        {
            // max = {Id=1}; current dy_follows absent → currentSort = int.MaxValue >= exit Sort=2
            var action = SyncDecisionHelper.ResolveDuplicateVideoAction(
                VideoTypeEnum.dy_follows, VideoTypeEnum.dy_collects, Levels((1, 1), (2, 2)));
            Assert.Equal(DuplicateVideoAction.SkipDownload, action);
        }

        // ---- PickAuthorAvatarUrl ----
        // pin: current behavior, not aspirational

        private static ImageInfo AvatarImg(params string[] urls)
            => new ImageInfo { UrlList = urls.ToList() };

        private static Aweme AwemeWithAvatars(ImageInfo larger, ImageInfo thumb)
            => new Aweme { Author = new Author { AvatarLarger = larger, AvatarThumb = thumb } };

        [Fact]
        public void PickAuthorAvatarUrl_AvatarLargerPresent_TakesLargerFirst()
        {
            var item = AwemeWithAvatars(AvatarImg("L1", "L2"), AvatarImg("T1"));
            Assert.Equal("L1", SyncDecisionHelper.PickAuthorAvatarUrl(item));
        }

        [Fact]
        public void PickAuthorAvatarUrl_AvatarLargerNull_FallsBackToThumb()
        {
            var item = AwemeWithAvatars(null, AvatarImg("T1"));
            Assert.Equal("T1", SyncDecisionHelper.PickAuthorAvatarUrl(item));
        }

        [Fact]
        public void PickAuthorAvatarUrl_AvatarLargerEmptyUrlList_FallsBackToThumb()
        {
            // AvatarLarger present but UrlList empty → FirstOrDefault() is null → ?? falls through
            var item = AwemeWithAvatars(AvatarImg(), AvatarImg("T1"));
            Assert.Equal("T1", SyncDecisionHelper.PickAuthorAvatarUrl(item));
        }

        [Fact]
        public void PickAuthorAvatarUrl_BothNull_ReturnsNull()
        {
            var item = AwemeWithAvatars(null, null);
            Assert.Null(SyncDecisionHelper.PickAuthorAvatarUrl(item));
        }

        // ---- BuildDynamicVideoUrls ----
        // pin: current behavior, not aspirational

        private const string PlayPrefix = "https://www.douyin.com/aweme/v1/play";

        private static PlayAddr DynPlayAddr(int height, int width, params string[] urls)
            => new PlayAddr { Height = height, Width = width, UrlList = urls.ToList() };

        private static VideoBitRate DynBitRate(PlayAddr playAddr)
            => new VideoBitRate { PlayAddr = playAddr };

        private static Video DynVideo(params VideoBitRate[] bitRates)
            => new Video { BitRate = bitRates.ToList() };

        private static ImageItemInfo DynImage(Video dynamicVideo)
            => new ImageItemInfo { DynamicVideo = dynamicVideo };

        private static Aweme AwemeWithImages(params ImageItemInfo[] images)
            => new Aweme { Images = images.ToList() };

        [Fact]
        public void BuildDynamicVideoUrls_ImagesNull_ReturnsEmptyList()
        {
            // Images == null → if-guard false → empty list (not null)
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(new Aweme { Images = null });
            Assert.Empty(result);
        }

        [Fact]
        public void BuildDynamicVideoUrls_ImagesEmpty_ReturnsEmptyList()
        {
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(AwemeWithImages());
            Assert.Empty(result);
        }

        [Fact]
        public void BuildDynamicVideoUrls_DynamicVideoNull_ImageContributesNothing()
        {
            // DynamicVideo == null → ?.BitRate?.Count short-circuits → image skipped
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(
                AwemeWithImages(DynImage(null)));
            Assert.Empty(result);
        }

        [Fact]
        public void BuildDynamicVideoUrls_BitRateEmpty_ImageContributesNothing()
        {
            // BitRate present but empty → Count > 0 false → image skipped
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(
                AwemeWithImages(DynImage(DynVideo())));
            Assert.Empty(result);
        }

        [Fact]
        public void BuildDynamicVideoUrls_PlayAddrNull_BitRateContributesNothing()
        {
            // PlayAddr == null → btv.PlayAddr?.UrlList short-circuits → targetUrl null → skipped
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(
                AwemeWithImages(DynImage(DynVideo(DynBitRate(null)))));
            Assert.Empty(result);
        }

        [Fact]
        public void BuildDynamicVideoUrls_MatchingUrl_BuildsDtoWithPathAndPlayAddrDimensions()
        {
            var url = PlayPrefix + "/abc";
            var item = AwemeWithImages(DynImage(DynVideo(
                DynBitRate(DynPlayAddr(1280, 720, url)))));
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(item);
            var dto = Assert.Single(result);
            Assert.Equal(url, dto.Path);
            Assert.Equal(1280, dto.Height);
            Assert.Equal(720, dto.Width);
        }

        [Fact]
        public void BuildDynamicVideoUrls_UrlListMixed_PicksFirstPlayPrefixUrl()
        {
            // FirstOrDefault(predicate): non-prefix URL skipped, first matching URL taken
            var match1 = PlayPrefix + "/1";
            var match2 = PlayPrefix + "/2";
            var item = AwemeWithImages(DynImage(DynVideo(
                DynBitRate(DynPlayAddr(100, 100, "https://other.com/a", match1, match2)))));
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(item);
            var dto = Assert.Single(result);
            Assert.Equal(match1, dto.Path);
        }

        [Fact]
        public void BuildDynamicVideoUrls_MultipleImagesAndBitRates_CollectsEachMatchInEncounterOrder()
        {
            var a = PlayPrefix + "/a";
            var b = PlayPrefix + "/b";
            var c = PlayPrefix + "/c";
            var item = AwemeWithImages(
                DynImage(DynVideo(
                    DynBitRate(DynPlayAddr(10, 10, a)),
                    DynBitRate(DynPlayAddr(20, 20, b)))),
                DynImage(DynVideo(
                    DynBitRate(DynPlayAddr(30, 30, c)))));
            var result = SyncDecisionHelper.BuildDynamicVideoUrls(item);
            Assert.Collection(result,
                d => Assert.Equal(a, d.Path),
                d => Assert.Equal(b, d.Path),
                d => Assert.Equal(c, d.Path));
        }

        // ---- BuildImageUrls ----
        // pin: current behavior, not aspirational

        private static ImageItemInfo ImageUrlItem(int height, int width, params string[] urls)
            => new ImageItemInfo { Height = height, Width = width, UrlList = urls.ToList() };

        private static Aweme AwemeWithImageItems(params ImageItemInfo[] images)
            => new Aweme { Images = images.ToList() };

        [Fact]
        public void BuildImageUrls_ImagesNull_ReturnsNull()
        {
            // item.Images == null → ?. short-circuits → returns null (NOT an empty list)
            var result = SyncDecisionHelper.BuildImageUrls(new Aweme { Images = null });
            Assert.Null(result);
        }

        [Fact]
        public void BuildImageUrls_ImagesEmpty_ReturnsEmptyList()
        {
            // item.Images == [] → chain runs → empty (non-null) list
            var result = SyncDecisionHelper.BuildImageUrls(AwemeWithImageItems());
            Assert.Empty(result);
        }

        [Fact]
        public void BuildImageUrls_UrlListNull_ImageFilteredOut()
        {
            // UrlList == null → first .Where(UrlList != null) drops the image
            var item = AwemeWithImageItems(new ImageItemInfo { Height = 10, Width = 10, UrlList = null });
            var result = SyncDecisionHelper.BuildImageUrls(item);
            Assert.Empty(result);
        }

        [Fact]
        public void BuildImageUrls_UrlListEmpty_ImageFilteredOut()
        {
            // UrlList == [] → first .Where(UrlList.Any()) drops the image
            var item = AwemeWithImageItems(ImageUrlItem(10, 10));
            var result = SyncDecisionHelper.BuildImageUrls(item);
            Assert.Empty(result);
        }

        [Fact]
        public void BuildImageUrls_ValidImage_BuildsDtoWithFirstUrlAndDimensions()
        {
            var item = AwemeWithImageItems(ImageUrlItem(1920, 1080, "u1"));
            var result = SyncDecisionHelper.BuildImageUrls(item);
            var dto = Assert.Single(result);
            Assert.Equal("u1", dto.Path);
            Assert.Equal(1920, dto.Height);
            Assert.Equal(1080, dto.Width);
        }

        [Fact]
        public void BuildImageUrls_MultipleUrls_TakesFirstUrl()
        {
            // .Select takes UrlList.FirstOrDefault()
            var item = AwemeWithImageItems(ImageUrlItem(10, 10, "u1", "u2"));
            var result = SyncDecisionHelper.BuildImageUrls(item);
            var dto = Assert.Single(result);
            Assert.Equal("u1", dto.Path);
        }

        [Fact]
        public void BuildImageUrls_FirstUrlBlank_ImageFilteredOut()
        {
            // first URL is whitespace → Path is blank → second .Where drops the image
            // even though a valid second URL exists (only the first URL is ever considered)
            var item = AwemeWithImageItems(ImageUrlItem(10, 10, "   ", "u2"));
            var result = SyncDecisionHelper.BuildImageUrls(item);
            Assert.Empty(result);
        }

        [Fact]
        public void BuildImageUrls_MultipleImagesMixed_KeepsOnlyValidInEncounterOrder()
        {
            var item = AwemeWithImageItems(
                ImageUrlItem(1, 1, "valid1"),
                new ImageItemInfo { Height = 2, Width = 2, UrlList = null },
                ImageUrlItem(3, 3, "   ", "x"),
                ImageUrlItem(4, 4, "valid2"));
            var result = SyncDecisionHelper.BuildImageUrls(item);
            Assert.Collection(result,
                d => Assert.Equal("valid1", d.Path),
                d => Assert.Equal("valid2", d.Path));
        }

        // ---- CollectAlternateVideoUrls ----
        // pin: current behavior, not aspirational

        private static VideoBitRate AltBitRate(params string[] urls)
            => new VideoBitRate { PlayAddr = new PlayAddr { UrlList = urls.ToList() } };

        private static Aweme AwemeWithBitRates(params VideoBitRate[] bits)
            => new Aweme { Video = new Video { BitRate = bits.ToList() } };

        [Fact]
        public void CollectAlternateVideoUrls_BitRateEmpty_ReturnsEmptyList()
        {
            // empty BitRate → outer foreach runs 0 times → empty non-null list
            var item = AwemeWithBitRates();
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "any");
            Assert.Empty(result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_BitRateContainsNull_SkipsNullEntries()
        {
            // bit == null → first guard continues; the valid bit's "u1" is still collected
            var item = AwemeWithBitRates((VideoBitRate)null, AltBitRate("u1"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "exclude-me");
            Assert.Equal(new[] { "u1" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_PlayAddrNull_SkipsBitRate()
        {
            // payUrls == null → compound guard arm 1 → BitRate skipped
            var item = AwemeWithBitRates(
                new VideoBitRate { PlayAddr = null },
                AltBitRate("u1"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "exclude-me");
            Assert.Equal(new[] { "u1" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_UrlListNull_SkipsBitRate()
        {
            // payUrls.UrlList == null → compound guard arm 2 → BitRate skipped
            var item = AwemeWithBitRates(
                new VideoBitRate { PlayAddr = new PlayAddr { UrlList = null } },
                AltBitRate("u1"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "exclude-me");
            Assert.Equal(new[] { "u1" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_UrlListEmpty_SkipsBitRate()
        {
            // payUrls.UrlList.Count == 0 → compound guard arm 3 → BitRate skipped
            var item = AwemeWithBitRates(AltBitRate(), AltBitRate("u1"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "exclude-me");
            Assert.Equal(new[] { "u1" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_MatchesExcludeUrl_Excluded()
        {
            // payurl == excludeUrl (C# string == is ordinal value equality) → skip;
            // the other URL is kept.
            var item = AwemeWithBitRates(AltBitRate("u1", "u2"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "u1");
            Assert.Equal(new[] { "u2" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_DiffersOnlyInCase_NotExcluded()
        {
            // payurl differs from excludeUrl only by case → C# string == is case-sensitive
            // ordinal, so the URL is NOT excluded — "UrlA" stays in the result even though
            // excludeUrl is "urla".
            var item = AwemeWithBitRates(AltBitRate("UrlA"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "urla");
            Assert.Equal(new[] { "UrlA" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_DuplicateUrlsAcrossBitRates_NotDeduplicated()
        {
            // Two BitRates contain the same URL; neither equals excludeUrl → both copies kept.
            var item = AwemeWithBitRates(AltBitRate("u1"), AltBitRate("u1"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "exclude-me");
            Assert.Equal(new[] { "u1", "u1" }, result);
        }

        [Fact]
        public void CollectAlternateVideoUrls_MultipleBitRatesAndUrls_PreservesEncounterOrder()
        {
            // Outer BitRate order × inner UrlList order: b1.[a, b, skip] then b2.[c, d]
            // → result is [a, b, c, d] (the mid-stream "skip" matches excludeUrl and is dropped).
            var item = AwemeWithBitRates(
                AltBitRate("a", "b", "skip"),
                AltBitRate("c", "d"));
            var result = SyncDecisionHelper.CollectAlternateVideoUrls(item, "skip");
            Assert.Equal(new[] { "a", "b", "c", "d" }, result);
        }

        // ---- PickMergeVideoCoverUrl ----
        // pin: current behavior, not aspirational

        // Reuses existing section-local helpers:
        //   CoverImg(params string[] urls) → ImageInfo (defined in PickCoverUrl section)
        //   Cate(VideoTypeEnum cateType)   → DouyinCollectCate (defined in BuildVideoFileName section)
        // Adds one new helper:

        private static DouyinMergeVideoDto MergeDto(string path)
            => new DouyinMergeVideoDto { Path = path };

        [Fact]
        public void PickMergeVideoCoverUrl_CateNull_UsesNonCateBranch_ReturnsFirstImageUrlPath()
        {
            // cate == null → non-cate branch → reads only imageUrls (MixInfo/Music ignored even if present)
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("img1"), MergeDto("img2") };
            var item = new Aweme();
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(cate: null, item, imageUrls);
            Assert.Equal("img1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateCustomCollect_UsesNonCateBranch_ReturnsFirstImageUrlPath()
        {
            // cate.CateType == dy_custom_collect → still non-cate branch (the explicit carve-out)
            // MixInfo/Music present but MUST be ignored.
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("img1") };
            var item = new Aweme
            {
                MixInfo = new MixInfo { CoverUrl = CoverImg("mix1") },
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_custom_collect), item, imageUrls);
            Assert.Equal("img1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoPresent_PrefersMixInfo()
        {
            // cate-branch arm 1: MixInfo.CoverUrl.UrlList[0] wins; imageUrls/Music ignored.
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("img1") };
            var item = new Aweme
            {
                MixInfo = new MixInfo { CoverUrl = CoverImg("mix1") },
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Equal("mix1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoNull_FallsBackToImageUrls()
        {
            // cate-branch arm 2: MixInfo == null → ?? imageUrls; Music NOT consulted because imageUrls wins first.
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("img1") };
            var item = new Aweme
            {
                MixInfo = null,
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Equal("img1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoUrlListEmpty_FallsBackToImageUrls()
        {
            // cate-branch arm 2 edge: chain non-null all the way to UrlList, but UrlList empty →
            // FirstOrDefault() returns null → ?? imageUrls.
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("img1") };
            var item = new Aweme
            {
                MixInfo = new MixInfo { CoverUrl = CoverImg() },
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Equal("img1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_MixInfoAndImageUrlsEmpty_FallsBackToMusic()
        {
            // cate-branch arm 3: MixInfo null AND imageUrls empty → Music.CoverHd wins.
            var imageUrls = new List<DouyinMergeVideoDto>();
            var item = new Aweme
            {
                MixInfo = null,
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Equal("mu1", result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_AllSourcesNull_ReturnsNull()
        {
            // cate-branch all 3 sources unavailable → final null.
            var imageUrls = new List<DouyinMergeVideoDto>();
            var item = new Aweme { MixInfo = null, Music = null };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Null(result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNull_EmptyImageUrls_ReturnsNull()
        {
            // non-cate branch with empty imageUrls → null (defensive contract; production
            // caller guards at :911 but the helper handles it cleanly via FirstOrDefault()?.Path).
            // MixInfo/Music present but ignored because non-cate branch never consults them.
            var imageUrls = new List<DouyinMergeVideoDto>();
            var item = new Aweme
            {
                MixInfo = new MixInfo { CoverUrl = CoverImg("mix1") },
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(cate: null, item, imageUrls);
            Assert.Null(result);
        }

        [Fact]
        public void PickMergeVideoCoverUrl_CateNonCustomCollect_ImageUrlsHasMultiple_PrefersFirstNotMusic()
        {
            // FirstOrDefault() returns imageUrls[0], NOT a later element; and Music is NOT consulted
            // because imageUrls is non-empty in the ?? chain.
            var imageUrls = new List<DouyinMergeVideoDto> { MergeDto("first"), MergeDto("second") };
            var item = new Aweme
            {
                MixInfo = null,
                Music = new Music { CoverHd = CoverImg("mu1") },
            };
            var result = SyncDecisionHelper.PickMergeVideoCoverUrl(Cate(VideoTypeEnum.dy_mix), item, imageUrls);
            Assert.Equal("first", result);
        }
    }
}
