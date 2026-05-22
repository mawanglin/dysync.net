namespace dy.net.model.dto
{
    /// <summary>
    /// 去重决策结果：当 DB 已存在同 AwemeId 视频、且其本地文件也存在时，
    /// 按视频类型优先级判定本次同步应如何处理。
    /// 由 SyncDecisionHelper.ResolveDuplicateVideoAction 产出。
    /// </summary>
    public enum DuplicateVideoAction
    {
        /// <summary>跳过下载——已存在同等或更高优先级的视频。</summary>
        SkipDownload,

        /// <summary>删除旧文件后继续下载——当前类型优先级更高。</summary>
        ReplaceExisting
    }
}
