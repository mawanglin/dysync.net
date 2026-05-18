namespace dy.net.model.dto
{
    /// <summary>GetStatics 的服务端标量聚合结果。</summary>
    public class VideoStaticsScalar
    {
        public int VideoCount { get; set; }
        public int AuthorCount { get; set; }
        public int CategoryCount { get; set; }
        public int FavoriteCount { get; set; }
        public int CollectCount { get; set; }
        public int FollowCount { get; set; }
        public int MixCount { get; set; }
        public int SeriesCount { get; set; }
        public int GraphicVideoCount { get; set; }
        public long TotalSize { get; set; }
        public long FavoriteSize { get; set; }
        public long CollectSize { get; set; }
        public long FollowSize { get; set; }
        public long MixSize { get; set; }
        public long SeriesSize { get; set; }
        public long GraphicSize { get; set; }
    }

    public class AuthorProjection
    {
        public string Author { get; set; }
        public string AuthorAvatarUrl { get; set; }
        public string AuthorId { get; set; }
        public string DyUserId { get; set; }
    }

    public class ChartProjection
    {
        public System.DateTime SyncTime { get; set; }
        public VideoTypeEnum ViedoType { get; set; }
        public string FileHash { get; set; }
    }
}
