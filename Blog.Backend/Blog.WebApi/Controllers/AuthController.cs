using Blog.Application.Common;
using Blog.Application.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.WebApi.Controllers
{
    /// <summary>
    /// 认证：作者与管理员分两个登录端点。
    ///
    /// **没有注册端点**：按 T1 决策，作者账号由管理员在 /api/users 创建，
    /// 管理员账号由已有管理员创建（见 docs/tech.md §2.3）。
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

        /// <summary>作者登录。仅 Author 角色可通过</summary>
        [HttpPost("author/login")]
        [AllowAnonymous]
        public async Task<ApiResponse<LoginResponse>> AuthorLogin(
            [FromBody] LoginRequest request, CancellationToken cancellationToken)
        {
            var result = await _auth.AuthorLoginAsync(request, cancellationToken);
            return ApiResponse<LoginResponse>.Ok(result);
        }

        /// <summary>管理员登录。仅 Admin 角色可通过</summary>
        [HttpPost("admin/login")]
        [AllowAnonymous]
        public async Task<ApiResponse<LoginResponse>> AdminLogin(
            [FromBody] LoginRequest request, CancellationToken cancellationToken)
        {
            var result = await _auth.AdminLoginAsync(request, cancellationToken);
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
        /// 因为 JWT 本身无法主动失效（见 docs/tech.md §2.5）。
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
