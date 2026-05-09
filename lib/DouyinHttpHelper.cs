using System;
using System.Net.Http;
using System.Threading.Tasks;
using dy.net;

namespace dy.sync.lib
{
    /// <summary>
    /// 来源于反编译 lib/dy.sync.lib.dll（v1.0.0.0），原 DLL 中 URL 经异或混淆。
    /// 此处明文化并将原硬编码地址迁移到 appsettings.json：dockerTagsBaseUrl
    /// 用于查询 Docker 镜像可用 tag 列表。
    /// </summary>
    public class DouyinHttpHelper
    {
        /// <summary>
        /// appsettings.json 中需配置该键，指向 Docker tag 查询服务的基础 URL，必须以 '/' 结尾。
        /// </summary>
        private const string ConfigKey = "dockerTagsBaseUrl";

        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// 查询指定 Docker tag 的可用版本列表
        /// </summary>
        /// <param name="tag">tag 名称（通常来自 appsettings 的 tagName）</param>
        public static Task<HttpResponseMessage> GetTenImage(string tag)
        {
            var baseUrl = Appsettings.Get(ConfigKey);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException(
                    $"appsettings.json 缺少必需的配置项 \"{ConfigKey}\"，无法查询 Docker tag 列表。");
            }
            return _httpClient.GetAsync(baseUrl + tag);
        }
    }
}
