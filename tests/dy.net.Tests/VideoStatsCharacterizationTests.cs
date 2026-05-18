using dy.net.model.dto;
using dy.net.model.entity;
using dy.net.repository;
using dy.net.service;

namespace dy.net.Tests
{
    /// <summary>
    /// Pins the CURRENT in-memory aggregation behavior of GetStatics / GetChartData
    /// against a fixed, documented dataset in a real temporary SQLite database.
    /// A later refactor to SQL-side aggregation must keep these green.
    /// </summary>
    public class VideoStatsCharacterizationTests
    {
        private const long GB = 1073741824L;

        private static DouyinVideo V(
            string id, string authorId, string author, long fileSize,
            VideoTypeEnum type, string tag1, int isMerge, string fileHash,
            System.DateTime syncTime)
            => new DouyinVideo
            {
                Id = id,
                AuthorId = authorId,
                Author = author,
                FileSize = fileSize,
                ViedoType = type,
                Tag1 = tag1,
                IsMergeVideo = isMerge,
                FileHash = fileHash,
                SyncTime = syncTime,
                CreateTime = syncTime
            };

        // Fixed dataset: 3 videos, 2 distinct authors, tags {"搞笑", ""},
        // sizes 1/2/3 GB, types collects/favorite/follows, v3 is a merge/graphic.
        private static System.Collections.Generic.List<DouyinVideo> Seed(System.DateTime when)
            => new()
            {
                V("1", "authorA", "作者甲", 1 * GB, VideoTypeEnum.dy_collects, "搞笑", 0, "h1", when),
                V("2", "authorA", "作者甲", 2 * GB, VideoTypeEnum.dy_favorite, "搞笑", 0, "h2", when),
                V("3", "authorB", "作者乙", 3 * GB, VideoTypeEnum.dy_follows,  "",   1, "",   when),
            };

        private static DouyinVideoService MakeService(TestDb t)
            => new DouyinVideoService(
                new DouyinVideoRepository(t.Db),
                new DouyinCookieRepository(t.Db),
                t.Db);

        [Fact]
        public async System.Threading.Tasks.Task GetStatics_locks_current_aggregation()
        {
            using var t = new TestDb();
            await t.Db.Insertable(Seed(new System.DateTime(2026, 5, 10))).ExecuteCommandAsync();

            var dto = await MakeService(t).GetStatics();

            Assert.Equal(3, dto.VideoCount);
            Assert.Equal(2, dto.AuthorCount);
            Assert.Equal(2, dto.CategoryCount);
            Assert.Equal(1, dto.FavoriteCount);
            Assert.Equal(1, dto.CollectCount);
            Assert.Equal(1, dto.FollowCount);
            Assert.Equal(0, dto.MixCount);
            Assert.Equal(0, dto.SeriesCount);
            Assert.Equal(1, dto.GraphicVideoCount);
            Assert.Equal("6.00", dto.VideoSizeTotal);
            Assert.Equal("2.00", dto.VideoFavoriteSize);
            Assert.Equal("1.00", dto.VideoCollectSize);
            Assert.Equal("3.00", dto.VideoFollowSize);
            Assert.Equal("<0.01", dto.VideoMixSize);
            Assert.Equal("<0.01", dto.VideoSeriesSize);
            Assert.Equal("3.00", dto.GraphicVideoSize);
        }

        [Fact]
        public async System.Threading.Tasks.Task GetChartData_locks_current_aggregation()
        {
            using var t = new TestDb();
            // SyncTime must be within the last `day` days for GetChartData to include it.
            var when = System.DateTime.Now.AddDays(-1);
            await t.Db.Insertable(Seed(when)).ExecuteCommandAsync();

            var chart = await MakeService(t).GetChartData(7);

            // All 3 rows share one SyncTime day → one group.
            Assert.Single(chart);
            var g = chart[0];
            Assert.Equal(when.ToString("yyyyMMdd"), g.Date);
            Assert.Equal(1, g.Collect);
            Assert.Equal(1, g.Favorite);
            Assert.Equal(1, g.Follow);
            Assert.Equal(1, g.Graphic); // FileHash null/empty → v3 only
            Assert.Equal(0, g.Mix);
            Assert.Equal(0, g.Series);
        }
    }
}
