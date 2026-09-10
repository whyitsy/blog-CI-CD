using Blog.Application.Common;
using Blog.Application.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.WebApi.Controllers
{
    /// <summary>
    /// 账号管理。**整个控制器仅管理员可访问**。
    ///
    /// 这是作者账号的唯一创建入口（T1：作者不开放自助注册）。
    /// 响应 DTO 不含任何凭据字段。
    /// </summary>
    [ApiController]
    [Route("api/users")]
    [Authorize(Policy = "AdminOnly")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _users;

        public UsersController(IUserService users)
        {
            _users = users;
        }

        [HttpGet]
        public async Task<ApiResponse<List<UserDto>>> GetAll(CancellationToken cancellationToken)
        {
            var items = await _users.GetAllAsync(cancellationToken);
            return ApiResponse<List<UserDto>>.Ok(items);
        }

        [HttpGet("{id:guid}")]
        public async Task<ApiResponse<UserDto>> GetById(Guid id, CancellationToken cancellationToken)
        {
            var item = await _users.GetByIdAsync(id, cancellationToken);
            return ApiResponse<UserDto>.Ok(item);
        }

        /// <summary>创建账号。role=Author 时需指定关联的 authorId，即「给某位作者开通登录」</summary>
        [HttpPost]
        public async Task<ApiResponse<UserDto>> Create(
            [FromBody] CreateUserRequest request, CancellationToken cancellationToken)
        {
            var created = await _users.CreateAsync(request, cancellationToken);
            return ApiResponse<UserDto>.Ok(created);
        }

        /// <summary>更新角色 / 关联作者 / 启用状态（乐观锁）</summary>
        [HttpPut("{id:guid}")]
        public async Task<ApiResponse<UserDto>> Update(
            Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
        {
            var updated = await _users.UpdateAsync(id, request, cancellationToken);
            return ApiResponse<UserDto>.Ok(updated);
        }

        /// <summary>重置密码。会提升 TokenVersion，使该账号旧 token 立即失效</summary>
        [HttpPost("{id:guid}/reset-password")]
        public async Task<ApiResponse<UserDto>> ResetPassword(
            Guid id, [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
        {
            var updated = await _users.ResetPasswordAsync(id, request, cancellationToken);
            return ApiResponse<UserDto>.Ok(updated);
        }

        /// <summary>停用账号（不物理删除，保留审计线索）</summary>
        [HttpPost("{id:guid}/disable")]
        public async Task<ApiResponse<object?>> Disable(
            Guid id, [FromQuery] int version, CancellationToken cancellationToken)
        {
            await _users.DisableAsync(id, version, cancellationToken);
            return ApiResponse.Ok();
        }
    }
}
