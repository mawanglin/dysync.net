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
    }
}
