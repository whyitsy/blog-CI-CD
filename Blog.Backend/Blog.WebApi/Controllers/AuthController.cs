using Blog.Application.Common;
using Blog.Application.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.WebApi.Controllers
{
    /// <summary>
    /// 认证：管理员与作者共用同一个登录端点，登录后由前端按返回的 Role 决定去向。
    ///
    /// **没有注册端点**：按 T1 决策，作者账号由管理员在 /api/users 创建，
    /// 管理员账号由已有管理员创建（见 docs/01-项目初始化与配置.md §2.2）。
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _auth;

        public AuthController(IAuthService auth)
        {
            _auth = auth;
        }

        /// <summary>
        /// 登录。不限定角色，Admin 与 Author 共用（登录入口不承担权限边界，
        /// 权限由 AdminOnly / ContentWriter 授权策略在具体接口上强制）。
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ApiResponse<LoginResponse>> Login(
            [FromBody] LoginRequest request, CancellationToken cancellationToken)
        {
            var result = await _auth.LoginAsync(request, cancellationToken);
            return ApiResponse<LoginResponse>.Ok(result);
        }

        /// <summary>当前登录用户</summary>
        [HttpGet("me")]
        [Authorize]
        public async Task<ApiResponse<CurrentUserDto>> Me(CancellationToken cancellationToken)
        {
            var result = await _auth.GetCurrentAsync(cancellationToken);
            return ApiResponse<CurrentUserDto>.Ok(result);
        }

        /// <summary>
        /// 注销：提升账号 TokenVersion，使该账号**所有**旧 token 立即失效。
        /// 因为 JWT 本身无法主动失效（见 docs/08-开发知识点.md §15.3）。
        /// </summary>
        [HttpPost("logout")]
        [Authorize]
        public async Task<ApiResponse<object?>> Logout(CancellationToken cancellationToken)
        {
            await _auth.LogoutAsync(cancellationToken);
            return ApiResponse.Ok();
        }
    }
}
