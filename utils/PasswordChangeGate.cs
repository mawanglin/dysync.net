using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace dy.net.utils
{
    /// <summary>
    /// JWT 内标识「该会话仍使用默认凭据，必须先改密」的 claim 名称与值。
    /// 登录时若 AdminUserInfo.MustChangePwd 为 true 则写入此 claim。
    /// </summary>
    public static class PasswordChangeClaim
    {
        public const string Type = "must_change_pwd";
        public const string TrueValue = "true";
    }

    /// <summary>
    /// 标记「待改密状态下仍可访问」的端点（改密本身及其表单依赖）。
    /// 打在端点上即被 <see cref="PasswordChangeGateMiddleware"/> 放行。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class AllowWhenPasswordChangeRequiredAttribute : Attribute { }

    /// <summary>
    /// 强制首登改密的纯判定逻辑（无 HTTP 依赖，便于特征化测试）。
    /// </summary>
    public static class PasswordChangeGate
    {
        /// <summary>
        /// 是否应拦截该请求。
        /// </summary>
        /// <param name="isAuthenticated">请求是否已认证。</param>
        /// <param name="mustChangePwdClaim">token 中 must_change_pwd claim 的值（无则 null）。</param>
        /// <param name="endpointAllowsDuringChange">命中端点是否带 [AllowWhenPasswordChangeRequired]。</param>
        public static bool ShouldBlock(bool isAuthenticated, string mustChangePwdClaim, bool endpointAllowsDuringChange)
        {
            if (endpointAllowsDuringChange) return false;
            if (!isAuthenticated) return false;
            return string.Equals(mustChangePwdClaim, PasswordChangeClaim.TrueValue, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 服务端强制首登改密门控：携带 must_change_pwd=true 的会话，除被标记的改密相关端点外，
    /// 一律拒绝（HTTP 403）。置于 UseAuthorization 之后、MapControllers 之前。
    /// 改密成功后前端会清除 token 并重登，库中标记已清零，新 token 不再带该 claim，自然解封。
    /// </summary>
    public sealed class PasswordChangeGateMiddleware
    {
        private readonly RequestDelegate _next;

        public PasswordChangeGateMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            var endpoint = context.GetEndpoint();
            bool allows = endpoint?.Metadata.GetMetadata<AllowWhenPasswordChangeRequiredAttribute>() != null;

            bool isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
            string claim = context.User?.FindFirst(PasswordChangeClaim.Type)?.Value;

            if (PasswordChangeGate.ShouldBlock(isAuthenticated, claim, allows))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(
                    "{\"code\":-2,\"erro\":\"请先修改默认密码后再使用\",\"mustChangePwd\":true}");
                return;
            }

            await _next(context);
        }
    }
}
