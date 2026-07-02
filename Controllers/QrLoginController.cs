using System.Threading.Tasks;
using dy.net.model.dto;
using dy.net.service.qrlogin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace dy.net.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class QrLoginController : ControllerBase
    {
        private readonly DouyinQrLoginService _svc;

        public QrLoginController(DouyinQrLoginService svc)
        {
            _svc = svc;
        }

        public sealed class CancelDto
        {
            public string sessionId { get; set; }
        }

        /// <summary>启动扫码会话，返回二维码。</summary>
        [HttpPost("start")]
        public async Task<IActionResult> Start()
        {
            try
            {
                var r = await _svc.StartAsync();
                Log.Information("扫码start: 生成 sessionId={Sid}", r.SessionId);
                return ApiResult.Success(r);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "启动扫码登录失败");
                return ApiResult.Fail("启动扫码登录失败：浏览器组件异常，请稍后重试");
            }
        }

        /// <summary>轮询扫码状态；成功时返回 cookie 与账号信息。</summary>
        [HttpGet("poll")]
        public async Task<IActionResult> Poll([FromQuery] string sessionId)
        {
            var r = await _svc.PollAsync(sessionId);
            Log.Information("扫码poll: 收到 sessionId={Sid} 返回 status={Status}", sessionId, r.Status);
            return ApiResult.Success(r);
        }

        /// <summary>取消/关闭扫码会话。</summary>
        [HttpPost("cancel")]
        public async Task<IActionResult> Cancel([FromBody] CancelDto dto)
        {
            await _svc.CancelAsync(dto?.sessionId);
            return ApiResult.Success();
        }
    }
}
