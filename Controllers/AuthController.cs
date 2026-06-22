using ClockSnowFlake;
using dy.net.model.dto;
using dy.net.service;
using dy.net.utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace dy.net.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IWebHostEnvironment webHostEnvironment;

        private readonly AdminUserService _userService;
        public AuthController(AdminUserService userService, IWebHostEnvironment webHostEnvironment)
        {
            _userService = userService;
            this.webHostEnvironment = webHostEnvironment;
        }


        /// <summary>
        /// 修改密码
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        [Authorize]
        [AllowWhenPasswordChangeRequired]
        [HttpPost]
        public async Task<IActionResult> UpdatePwd(UpdatePwdRequest user)
        {
            var (code, erro) = await _userService.UpdatePwd(user);
            return ApiResult.Success("", erro);
        }


        /// <summary>
        /// 获取头像
        /// </summary>
        /// <returns></returns>
        [AllowWhenPasswordChangeRequired]
        [HttpGet]
        public async Task<IActionResult> GetUserAvatar()
        {
            var user = await _userService.GetUser();
            return ApiResult.Success(new { user?.Avatar, user?.Id, user?.UserName });
        }




        /// <summary>
        /// 登录获取token
        /// </summary>
        /// <param name="loginUserInfo"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Login(LoginRequest loginUserInfo)
        {
            if (loginUserInfo == null)
            {
                return ApiResult.Fail("参数不能为空");
            }
            else
            {
                var user = await _userService.GetUser(loginUserInfo.UserName);

                if (user == null)
                {
                    return ApiResult.Fail("用户名或密码不正确");
                }
                else
                {
                    if (PasswordUtil.Verify(user.Password, loginUserInfo.Password))
                    {
                        // 登录成功且仍是历史 MD5 → 透明升级为 PBKDF2
                        if (PasswordUtil.IsLegacyMd5(user.Password))
                        {
                            user.Password = PasswordUtil.Hash(loginUserInfo.Password);
                            await _userService.UpdateUser(user);
                        }
                        var tokenString = GenerateJwtToken(user.UserName, user.MustChangePwd);
                        // mustChangePwd：默认凭据首登时为 true，前端据此提示立即改密；
                        // 同时 token 内置 must_change_pwd claim，服务端门控（PasswordChangeGateMiddleware）
                        // 会拦截改密以外的一切端点，确保不仅是前端软提示。
                        // expires 与 token 实际有效期（7 天）对齐，避免前端按 24h 误判。
                        return Ok(new { code = 0, erro = "", token = tokenString, expires = 7 * 24 * 60 * 60 * 1000, data = user.UserName, mustChangePwd = user.MustChangePwd });
                    }
                    else
                    {
                        return ApiResult.Fail("用户名或密码不正确");
                    }
                }
            }
        }


        private static string GenerateJwtToken(string username, bool mustChangePwd = false)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
             };
            // 默认凭据未改密的会话写入门控 claim，由 PasswordChangeGateMiddleware 强制拦截。
            if (mustChangePwd)
                claims.Add(new Claim(PasswordChangeClaim.Type, PasswordChangeClaim.TrueValue));
            var key = new SymmetricSecurityKey(JwtKeyProvider.GetKeyBytes());
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expires = DateTime.Now.AddDays(7);

            var token = new JwtSecurityToken(
                issuer: JwtKeyProvider.Issuer,
                audience: JwtKeyProvider.Audience,
                claims: claims,
                expires: expires,
                signingCredentials: credentials
            );

            var jwtToken = new JwtSecurityTokenHandler().WriteToken(token);
            return jwtToken;
        }
    }
}
