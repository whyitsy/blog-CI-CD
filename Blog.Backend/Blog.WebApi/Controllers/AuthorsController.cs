using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Application.Services.Author;
using Blog.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.WebApi.Controllers
{
    /// <summary>
    /// 作者（**内容层**的署名对象，与 Post/Tag/Category 同层，不是登录账号）。
    ///
    /// 与账号的分工见 docs/tech.md §2.2：
    ///   - 作者管理（本控制器）：维护「文章的署名信息」——姓名、头像、简介
    ///   - 账号管理（`/api/users`）：维护「谁能登录」——邮箱、密码、角色
    /// 两者通过可空的 `User.AuthorId` 弱关联，允许存在没有账号的作者。
    /// </summary>
    [ApiController]
    [Route("api/authors")]
    public class AuthorsController : ControllerBase
    {
        private readonly IAuthorService _authors;
        private readonly ICurrentUser _currentUser;

        public AuthorsController(IAuthorService authors, ICurrentUser currentUser)
        {
            _authors = authors;
            _currentUser = currentUser;
        }

        /// <summary>作者列表（公开：文章详情需要展示作者信息）</summary>
        [HttpGet]
        public async Task<ApiResponse<List<AuthorDto>>> GetAll(CancellationToken cancellationToken)
        {
            var items = await _authors.GetAllAsync(cancellationToken);
            return ApiResponse<List<AuthorDto>>.Ok(items);
        }

        [HttpGet("{id:guid}")]
        public async Task<ApiResponse<AuthorDto>> GetById(Guid id, CancellationToken cancellationToken)
        {
            var author = await _authors.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("作者不存在", ErrorCodes.NotFound);

            return ApiResponse<AuthorDto>.Ok(author);
        }

        /// <summary>创建作者（仅管理员）。用于给尚未登录的作者建立署名身份</summary>
        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ApiResponse<AuthorDto>> Create(
            [FromBody] CreateAuthorRequest request, CancellationToken cancellationToken)
        {
            var created = await _authors.CreateAsync(request, cancellationToken);
            return ApiResponse<AuthorDto>.Ok(created);
        }

        /// <summary>
        /// 更新作者资料。
        /// 管理员可改任何人；作者角色只能改**自己的**署名信息
        /// （后端按账号关联的 AuthorId 判定，见 docs/backend.md §6.3）。
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Policy = "ContentWriter")]
        public async Task<ApiResponse<AuthorDto>> Update(
            Guid id, [FromBody] UpdateAuthorRequest request, CancellationToken cancellationToken)
        {
            var isSelf = _currentUser.AuthorId.HasValue && _currentUser.AuthorId.Value == id;

            if (!_currentUser.IsAdmin && !isSelf)
                throw new BusinessException("无权修改他人的作者资料", ErrorCodes.Forbidden);

            var updated = await _authors.UpdateAsync(id, request, cancellationToken);
            return ApiResponse<AuthorDto>.Ok(updated);
        }

        /// <summary>
        /// 删除作者（仅管理员，软删除）。
        /// 其署名文章的 AuthorId 会被置空，文章保留。
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ApiResponse<object?>> Delete(
            Guid id, [FromQuery] int version, CancellationToken cancellationToken)
        {
            await _authors.DeleteAsync(id, version, cancellationToken);
            return ApiResponse.Ok();
        }

        /// <summary>
        /// 当前登录账号的署名身份（作者工作区「个人资料」页用）。
        /// 未关联作者时返回 404 —— 由管理员在「账号管理」里为其关联。
        /// </summary>
        [HttpGet("me")]
        [Authorize(Policy = "ContentWriter")]
        public async Task<ApiResponse<AuthorDto>> GetMine(CancellationToken cancellationToken)
        {
            if (!_currentUser.AuthorId.HasValue)
                throw new BusinessException(
                    "当前账号未关联作者，请联系管理员在「账号管理」中为你关联署名身份",
                    ErrorCodes.NotFound);

            var author = await _authors.GetByIdAsync(_currentUser.AuthorId.Value, cancellationToken)
                ?? throw new BusinessException("作者不存在", ErrorCodes.NotFound);

            return ApiResponse<AuthorDto>.Ok(author);
        }
    }
}
