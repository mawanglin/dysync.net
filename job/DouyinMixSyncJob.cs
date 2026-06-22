using dy.net.model.dto;
using dy.net.model.entity;
using dy.net.model.response;
using dy.net.service;
using dy.net.utils;

namespace dy.net.job
{
    public class DouyinMixSyncJob : DouyinBasicSyncJob
    {
        public DouyinMixSyncJob(DouyinCookieService douyinCookieService, DouyinHttpClientService douyinHttpClientService, DouyinVideoService douyinVideoService, DouyinCommonService douyinCommonService, DouyinFollowService douyinFollowService, DouyinMergeVideoService douyinMergeVideoService, DouyinCollectCateService douyinCollectCateService, SyncRunState syncRunState) : base(douyinCookieService, douyinHttpClientService, douyinVideoService, douyinCommonService, douyinFollowService, douyinMergeVideoService, douyinCollectCateService, syncRunState)
        {
        }


        protected override VideoTypeEnum VideoType => VideoTypeEnum.dy_mix;


        protected override async Task<DouyinVideoInfoResponse> FetchVideoData(DouyinCookie cookie, string cursor, DouyinFollowed followed, DouyinCollectCate cate)
        {
            return await douyinHttpClientService.SyncMixViedosByMixId(cursor, count, cookie.Cookies, cate.XId);
        }

        protected override Task<List<DouyinCookie>> GetSyncCookies()
        {
            return  douyinCookieService.GetOpendCookiesAsync(x => !string.IsNullOrWhiteSpace(x.MixPath));
        }
        protected override string CreateSaveFolder(DouyinCookie cookie, Aweme item, AppConfig config, DouyinFollowed followed, DouyinCollectCate cate)
        {
            if (cate != null)
            {
                if (string.IsNullOrWhiteSpace(cookie.MixPath))
                {
                    var folder = SafeCombine(cookie?.SavePath, "视频存储路径 SavePath", cookie, VideoType.GetDesc(), DouyinFileNameHelper.SanitizeLinuxFileName(cate.SaveFolder, cate.Name, true));
                    if (folder == null) return null;
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    return folder;
                }
                else
                {
                    var folder = SafeCombine(cookie?.MixPath, "合集保存路径 MixPath", cookie, DouyinFileNameHelper.SanitizeLinuxFileName(cate.SaveFolder, cate.Name, true));
                    if (folder == null) return null;
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    return folder;
                }
            }
            else
            {
                return base.CreateSaveFolder(cookie, item, config, followed, cate);
            }
        }

        protected override string GetAuthorAvatarBasePath(DouyinCookie cookie)
        {
            if (string.IsNullOrEmpty(cookie.MixPath))

                return Path.Combine(cookie.SavePath, "author");
            else
                return Path.Combine(cookie.MixPath, "author");
        }
    }
}