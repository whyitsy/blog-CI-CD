using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Services.Author;
using Microsoft.AspNetCore.Mvc;

namespace Blog.WebApi.Controllers
{
    /// <summary>作者信息（当前无认证，单人博客：用于博主资料读写）</summary>
    [ApiController]
    [Route("api/authors")]
    public class AuthorsController : ControllerBase
    {
        private readonly IAuthorService _authors;

        public AuthorsController(IAuthorService authors)
        {
            _authors = authors;
        }

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

        /// <summary>更新博主资料（乐观锁：必须携带当前 version，冲突返回 4090）</summary>
        [HttpPut("{id:guid}")]
        public async Task<ApiResponse<AuthorDto>> Update(Guid id, [FromBody] UpdateAuthorRequest request, CancellationToken cancellationToken)
        {
            var updated = await _authors.UpdateAsync(id, request, cancellationToken);
            return ApiResponse<AuthorDto>.Ok(updated);
        }
    }
}
