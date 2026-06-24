using ClockSnowFlake;
using dy.net.extension;
using dy.net.model.dto;
using dy.net.model.entity;
using dy.net.service;
using dy.net.utils;
using jzc.http.lib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace dy.net.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ConfigController : ControllerBase
    {
        private readonly DouyinCookieService dyCookieService;

        private readonly DouyinCommonService commonService;
        private readonly DouyinQuartzJobService quartzJobService;
        private readonly DouyinFollowService douyinFollowService;
        private readonly DouyinCookieService douyinCookieService;
        private readonly DouyinHttpClientService httpClientService;
        private readonly SyncRunState syncRunState;



        public ConfigController(DouyinCookieService dyCookieService, DouyinCommonService commonService, DouyinQuartzJobService quartzJobService, DouyinFollowService douyinFollowService, DouyinCookieService douyinCookieService, DouyinHttpClientService httpClientService, SyncRunState syncRunState)
        {
            this.dyCookieService = dyCookieService;
            this.commonService = commonService;
            this.quartzJobService = quartzJobService;
            this.douyinFollowService = douyinFollowService;
            this.douyinCookieService = douyinCookieService;
            this.httpClientService = httpClientService;
            this.syncRunState = syncRunState;
        }


        /// <summary>
        /// 导出配置
        /// </summary>
        /// <returns></returns>
        [HttpGet("exportConf")]
        public async Task<IActionResult> ExportConf()
        {

            // 1. 组装导出数据
            var dto = new AppConfigImportDto()
            {
                follows = await douyinFollowService.GetHandFollows(),
                conf = commonService.GetConfig(),
                cookies = await douyinCookieService.GetAllAsync()
            };

            return ApiResult.Success(dto);

        }


        [HttpPost("importConf")]
        public async Task<IActionResult> ImportConf(AppConfigImportDto dto)
        {
            if (dto == null)
                return ApiResult.Fail("json数据为空");

            await HandleFollowsImport(dto.follows);
            await HandleConfigImport(dto.conf);
            await HandleCookiesImport(dto.cookies);

            return ApiResult.Success();
        }

        private async Task HandleFollowsImport(List<DouyinFollowed> follows)
        {
            if (follows?.Count > 0)
            {
                var added = await douyinFollowService.AddHandFollows(follows);
                if (added)
                    Serilog.Log.Debug("关注列表导入成功");
            }
        }

        private async Task HandleConfigImport(AppConfig conf)
        {
            if (conf == null) return;

            if (conf.BatchCount > 30)
            {
                Serilog.Log.Debug("对不起，为了项目能长久稳定运行，还是最大不要超过30吧。。。");
                conf.BatchCount = 30;
            }

            var updated = await commonService.UpdateConfig(conf);
            if (updated)
                Serilog.Log.Debug("系统配置导入成功");
        }

        private async Task HandleCookiesImport(List<DouyinCookie> cookies)
        {
            if (cookies?.Count > 0)
            {
                var imported = await douyinCookieService.ImportCookies(cookies);
                if (imported)
                    Serilog.Log.Debug("抖音Cookie配置导入成功");
            }
        }
        /// <summary>
        /// 分页查询
        /// </summary>
        /// <returns>分页结果</returns>
        [HttpPost("paged")]
        public async Task<IActionResult> GetPagedAsync(
           PageRequestDto dto)
        {
            var (list, totalCount) = await dyCookieService.GetPagedAsync(dto.PageIndex, dto.PageSize);

            return ApiResult.Success(new
            {
                data = list,
                total = totalCount,
                pageIndex = dto.PageIndex,
                pageSize = dto.PageSize
            });
        }


        /// <summary>
        /// 查询所有用户Cookie
        /// </summary>
        /// <returns></returns>
        [HttpGet("list")]
        public async Task<IActionResult> GetAllList()
        {
            var follows = await douyinFollowService.GetGroupByCookieAsync();
            return ApiResult.Success(follows);
        }

        /// <summary>
        /// 是否已经初始化了
        /// </summary>
        /// <returns></returns>
        [HttpGet("isInit")]
        [AllowAnonymous]
        public async Task<IActionResult> IsInit()
        {
            var init = await dyCookieService.IsInit();
            return ApiResult.Success(init);
        }


        /// <summary>
        /// 配合工具自动设置cookie
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [HttpPost("FastResetCookie")]
        [AllowAnonymous]
        public async Task<IActionResult> FastResetCookie([FromBody] DouyinCookieResetDto dto)
        {
            // 来源门控：匿名 cookie 重置仅允许内网/本机（dy.cookie.exe 工具走 LAN），
            // 拒绝公网匿名按 id 覆盖 cookie 的攻击面。
            if (!IsLocalToolRequest())
                return StatusCode(403, new { code = -1, erro = "仅允许内网/本机访问该工具端点" });

            var cookieValid = await httpClientService.CheckCookie(new DouyinCookie { Cookies = dto.cookie });
            if (!cookieValid)
                return ApiResult.Fail("Cookie无效或已过期，请按照文档提示重新获取有效Cookie，不要使用插件获取cookie");
            var result = await dyCookieService.FastResetCookie(dto.id, dto.cookie);
         
            return result ? ApiResult.Success() : ApiResult.Fail("cookie设置失败");
        }

        /// <summary>
        /// 配合工具自动设置cookie,获取当前设置的所有Cookie
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet("Cookies")]
        public async Task<IActionResult> GetAllCookies()
        {
            // 来源门控：匿名枚举所有 cookie 的 id/name/status 仅允许内网/本机。
            if (!IsLocalToolRequest())
                return StatusCode(403, new { code = -1, erro = "仅允许内网/本机访问该工具端点" });

            var cookies = await dyCookieService.GetAllAsync();
            if (cookies != null)
                return ApiResult.Success(cookies.Select(x => new { id= x.Id,name= x.UserName, status=x.StatusMsg }));
            return ApiResult.Success();
        }

        /// <summary>
        /// 请求是否来自内网/本机（匿名工具端点的来源门控）。
        /// 出现任一反向代理转发头即视为非直连工具请求并拒绝，堵住「同机代理后 RemoteIpAddress
        /// 恒为 loopback/LAN」导致的门控绕过。
        /// </summary>
        private bool IsLocalToolRequest()
        {
            var headers = HttpContext.Request.Headers;
            bool hasForwardedHeaders =
                headers.ContainsKey("X-Forwarded-For")
                || headers.ContainsKey("X-Real-IP")
                || headers.ContainsKey("Forwarded")
                || headers.ContainsKey("X-Forwarded-Host");
            return NetworkGuard.IsLocalToolRequest(HttpContext.Connection.RemoteIpAddress, hasForwardedHeaders);
        }


        /// <summary>
        /// 非docker初始化
        /// </summary>
        [HttpPost("deskinit")]
        [AllowAnonymous]
        public async Task<IActionResult> DeskInitAsync([FromBody] DouyinCookie dyUserCookies)
        {
            // 0. 未初始化门控：deskinit 仅用于首次安装配置首个 Cookie。
            //    系统一旦完成初始化（已存在 Cookie），匿名调用即被拒绝，
            //    后续 Cookie 管理须经鉴权的 update 端点。堵住「随时匿名注入 cookie/路径」的攻击面。
            if (await dyCookieService.IsInit())
                return ApiResult.Fail("系统已初始化，禁止匿名配置；请登录后在设置中管理 Cookie");

            // 1. 基础赋值
            dyUserCookies.Id = IdGener.GetLong().ToString();

            // 2. 路径权限校验
            var (Success, Message) = ValidatePaths(dyUserCookies);
            if (!Success)
                return ApiResult.Fail(Message);

            RemoveCookieLineString(dyUserCookies);

            // 3. Cookie 有效性校验
            var cookieValid = await httpClientService.CheckCookie(dyUserCookies);
            if (!cookieValid)
                return ApiResult.Fail("Cookie无效或已过期，请按照文档提示重新获取有效Cookie，不要使用插件获取cookie");

            dyUserCookies.StatusCode = 0;
            dyUserCookies.StatusMsg = "正常";
            // 4. 保存到数据库
            var saved = await dyCookieService.Add(dyUserCookies);
            return saved ? ApiResult.Success() : ApiResult.Fail("添加失败");
        }

        private static (bool Success, string Message) ValidatePaths(DouyinCookie cookie)
        {
            var pathsToCheck = new Dictionary<string, string>
    {
        { "收藏存储路径", cookie.SavePath },
        { "喜欢视频存储路径", cookie.FavSavePath },
        { "上传视频存储路径", cookie.UpSavePath },
        // { "图片存储路径", cookie.ImgSavePath } // 可随时启用
    };

            foreach (var (label, path) in pathsToCheck)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    if (!DouyinFileUtils.HasDirectoryReadWritePermission(path))
                    {
                        return (false, $"请在飞牛应用设置里面将 {path} 添加读写权限（{label}）");
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(cookie.SavePath))
            {
                return (false, "收藏存储路径不能为空");
            }

            return (true, string.Empty);
        }


        /// <summary>
        /// 快速开启或停止
        /// </summary>
        [HttpPost("switch")]
        public async Task<IActionResult> SwitchAsync([FromBody] DouyinCookieSwitchDto dto)
        {
            var result = await dyCookieService.Switch(dto);
            if (result)
            {
                ReStartJob();
                return ApiResult.Success();
            }
            return ApiResult.Fail("添加失败");
        }
        /// <summary>
        /// 更新用户Cookie
        /// </summary>
        [HttpPost("update")]
        public async Task<IActionResult> AddOrUpdateAsync([FromBody] DouyinCookie dyUserCookies)
        {
            RemoveCookieLineString(dyUserCookies);

            var checkCk = await httpClientService.CheckCookie(dyUserCookies);
            if (!checkCk)
            {
                return ApiResult.Fail("Cookie无效或已过期，请按照文档提示重新获取有效Cookie，不要使用插件获取cookie");
            }
            dyUserCookies.StatusCode = 0;
            dyUserCookies.StatusMsg = "正常";
            if (dyUserCookies.Id == "0")
            {
                dyUserCookies.Id = IdGener.GetLong().ToString();
                var result = await dyCookieService.Add(dyUserCookies);
                if (result)
                {
                    ReStartJob();
                    return ApiResult.Success();
                }
                return ApiResult.Fail("添加失败");
            }
            else
            {
                var result = await dyCookieService.UpdateCookieAsync(dyUserCookies);
                if (result)
                {
                    ReStartJob();
                    return ApiResult.Success();
                }
                return ApiResult.Fail("更新失败");
            }

        }

        private static void RemoveCookieLineString(DouyinCookie dyUserCookies)
        {
            if (dyUserCookies != null && !string.IsNullOrWhiteSpace(dyUserCookies.Cookies))
            {
                var s = dyUserCookies.Cookies.Replace("\\r\\n", "\r\n");
                var ss = s.Trim(new char[] { '\r', '\n' });
                dyUserCookies.Cookies = ss;
            }
        }

        /// <summary>
        /// 批量删除用户Cookie
        /// </summary>
        [HttpGet("delete")]
        public async Task<IActionResult> DeleteAsync(string id)
        {
            var count = await dyCookieService.DeleteByIdsAsync(new List<string> { id });
            if (count > 0)
            {
                ReStartJob();
            }
            return ApiResult.Success(count);
        }

        [HttpGet("GetConfig")]
        public IActionResult GetConfig()
        {
            var data = commonService.GetConfig();
            return ApiResult.Success(data);
        }


        [HttpPost("UpdateConfig")]
        public async Task<IActionResult> UpdateConfig(AppConfig config)
        {
            var update = await commonService.UpdateConfig(config);
            if (update)
            {
                if (config.OnlySyncNew)
                {
                    Serilog.Log.Debug("仅同步新视频配置已生效,后续所有类型的视频同步将只会读取最近一页约20条数据");
                }

                ReStartJob();
            }
            return ApiResult.Success(update);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet("ExecuteJobNow")]
        //[Authorize]
        public async Task<IActionResult> ExecuteJobNow()
        {
            var config = commonService.GetConfig();
            if (config != null)
                await quartzJobService.InitOrReStartAllJobs(config.Cron.ToString());
            return ApiResult.Success();
        }

        /// <summary>
        /// 手动立即触发同步任务一次（无需等待定时任务）。
        /// 不传 type：触发全部视频下载类任务（收藏/喜欢/关注作品/自定义收藏夹/合集/短剧）。
        /// 传 type（如 dy_collects/dy_favorite/dy_follows/dy_mix/dy_series/dy_custom_collect）：仅触发该类型。
        /// 注意：仅能触发“当前已调度（已启用）”的任务；若对应类型未启用会返回失败提示。
        /// </summary>
        [HttpGet("TriggerSyncNow")]
        public async Task<IActionResult> TriggerSyncNow(string type = null)
        {
            // 后端互斥校验：已有同步在执行（含定时触发）则拒绝再次触发
            if (!syncRunState.TryBeginManualTrigger(DateTime.Now))
                return ApiResult.Fail("已有同步任务正在执行，请先停止或等待完成");

            if (!string.IsNullOrWhiteSpace(type))
            {
                if (!Enum.TryParse<VideoTypeEnum>(type, true, out var vt))
                    return ApiResult.Fail($"未知的同步类型: {type}");

                var ok = await quartzJobService.TriggerJobNowAsync(vt);
                return ok
                    ? ApiResult.Success(new { triggered = 1, type = vt.ToString() }, "已触发同步任务")
                    : ApiResult.Fail("该类型任务未在调度中（可能未启用或未满足启用条件），无法触发");
            }

            var count = await quartzJobService.TriggerAllVideoSyncNowAsync();
            return count > 0
                ? ApiResult.Success(new { triggered = count }, $"已触发 {count} 个同步任务")
                : ApiResult.Fail("没有可触发的已启用同步任务，请先在配置页开启对应的下载开关并保存");
        }


        /// <summary>
        /// 停止当前正在执行的同步（下完当前视频后中止本轮）。无任务执行时返回失败。
        /// </summary>
        [HttpGet("StopSyncNow")]
        public IActionResult StopSyncNow()
        {
            var stopped = syncRunState.RequestStop();
            return stopped
                ? ApiResult.Success(new { stopped = true }, "已发出停止指令，正在结束当前视频后中止")
                : ApiResult.Fail("当前没有正在执行的同步任务");
        }

        /// <summary>
        /// 查询当前同步执行情况（前端轮询：驱动按钮互斥 + 进度面板）。
        /// </summary>
        [HttpGet("SyncStatus")]
        public IActionResult SyncStatus()
        {
            return ApiResult.Success(syncRunState.GetSnapshot(DateTime.Now));
        }

        /// <summary>
        /// 定时任务总览：调度信息（Quartz）+ 每类型最近一轮结果（SyncRunState）。
        /// </summary>
        [HttpGet("SyncJobs")]
        public async Task<IActionResult> SyncJobs()
        {
            var jobs = await quartzJobService.GetJobsOverviewAsync();
            var snap = syncRunState.GetSnapshot(DateTime.Now);
            foreach (var job in jobs)
            {
                var t = snap.Types.FirstOrDefault(x => x.Type == job.Type);
                if (t != null)
                {
                    job.Running = t.Running;
                    job.Downloaded = t.Downloaded;
                    job.Failed = t.Failed;
                    job.CurrentTitle = t.CurrentTitle;
                    job.EndedAt = t.EndedAt;
                }
            }
            return ApiResult.Success(jobs);
        }

        /// <summary>修改某任务的执行周期（间隔分钟或 cron），即时生效且重启不丢。</summary>
        [HttpPost("UpdateJobSchedule")]
        public async Task<IActionResult> UpdateJobSchedule([FromBody] UpdateJobScheduleDto dto)
        {
            if (dto == null || !Enum.TryParse<VideoTypeEnum>(dto.Type, true, out var vt))
                return ApiResult.Fail("参数无效");
            var (ok, error) = await quartzJobService.UpdateJobScheduleAsync(vt, dto.ScheduleType, dto.Expression);
            return ok ? ApiResult.Success(new { updated = true }, "周期已更新") : ApiResult.Fail(error);
        }

        private void ReStartJob()
        {
            var config = commonService.GetConfig();
            if (config != null)
                quartzJobService.InitOrReStartAllJobs(config.Cron.ToString());
            //避免前端等待
        }
        /// <summary>
        /// 镜像标签
        /// </summary>
        /// <returns></returns>
        [HttpGet("mytag")]
        public async Task<IActionResult> GetMyTag()
        {
            var deploy = Appsettings.Get("deploy");
            var tag = Appsettings.Get("tagName");
            tag = deploy == "fn" ? "fn_" + Appsettings.Get("fnVersion") : tag;
            deploy = deploy == "fn" ? "fnos" : "docker";
            return ApiResult.Success(new { tag, deploy });
        }

        [AllowAnonymous]
        [HttpGet("checktag")]
        public async Task<IActionResult> CheckTag()
        {

            var deploy = Appsettings.Get("deploy");
            if (string.IsNullOrWhiteSpace(deploy))
            {
                return await GetDockerTagVersions();
            }
            else
            {
                if (deploy == "fn")//飞牛
                {
                    return ApiResult.Success(new List<string> { "fn_" + Appsettings.Get("fnVersion") });
                }
                else
                {
                    return await GetDockerTagVersions();
                }
            }


        }

        private static async Task<IActionResult> GetDockerTagVersions()
        {
            var data = await JianZhiChuHttpHelper.GetTenImage(Appsettings.Get("tagName"));
            if (data.IsSuccessStatusCode)
            {
                var content = await data.Content.ReadAsStringAsync();

                var tagData = JsonConvert.DeserializeObject<DouyinApiResponse<List<string>>>(content);
                if (tagData != null && tagData.Data != null && tagData.Data.Count > 0)
                    return ApiResult.Success(tagData.Data);
                return ApiResult.Fail();
            }
            else
            {
                return ApiResult.Fail("请求失败");
            }
        }

        /// <summary>
        /// 查询mp3目录下有没有音频文件
        /// </summary>
        /// <returns></returns>
        [HttpGet("mp3List")]
        public async Task<IActionResult> GetExistMps()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "mp3");

            if (!string.IsNullOrWhiteSpace(ServiceExtension.FnDataFolder))
            {
                path = ServiceExtension.FnDataFolder;
            }

            if (Directory.Exists(path))
            {
                var allowedExtensions = new HashSet<string> { ".mp3", ".wav" };

                var customMusics = Directory.GetFiles(path)
                 .Where(filePath =>
                     allowedExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant()) &&
                     Path.GetFileNameWithoutExtension(filePath) != "silent_10")
                 .ToList();

                var fileNames = customMusics.Select(f => new { filename = Path.GetFileName(f) }).Where(x => x.filename != "silent_10.mp3").ToList();
                return ApiResult.Success(fileNames);
            }
            else
            {
                return ApiResult.Fail("没有找到默认音频文件");
            }
        }

        /// <summary>
        /// 播放音频流
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet("getmp3")]
        public async Task<IActionResult> GetMp3([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name)
            {
                return BadRequest("非法文件名");
            }
            var path = Path.Combine(AppContext.BaseDirectory, "mp3", name);
            if (!string.IsNullOrWhiteSpace(ServiceExtension.FnDataFolder))
            {
                path = Path.Combine(ServiceExtension.FnDataFolder, name);
            }

            if (System.IO.File.Exists(path))
            {
                //返回mp3文件流
                var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
                return new FileStreamResult(fileStream, "audio/mpeg")
                {
                    FileDownloadName = name
                };
            }
            else
            {
                return ApiResult.Fail("文件不存在");
            }
        }
    }
}
