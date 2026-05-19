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
    }
}
