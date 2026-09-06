using Blog.Application.Common;
using Blog.Application.Services.Tag;
using Microsoft.AspNetCore.Mvc;

namespace Blog.WebApi.Controllers
{
    /// <summary>标签：按钮墙 + 管理</summary>
    [ApiController]
    [Route("api/tags")]
    public class TagsController : ControllerBase
    {
        private readonly ITagService _tags;

        public TagsController(ITagService tags)
        {
            _tags = tags;
        }

        /// <summary>全部标签（含已发布文章数，用于标签页按钮墙）</summary>
        [HttpGet]
        public async Task<ApiResponse<List<TagDto>>> GetAll(CancellationToken cancellationToken)
        {
            var items = await _tags.GetAllAsync(cancellationToken);
            return ApiResponse<List<TagDto>>.Ok(items);
        }

        [HttpPost]
        public async Task<ApiResponse<TagDto>> Create([FromBody] CreateTagRequest request, CancellationToken cancellationToken)
        {
            var created = await _tags.CreateAsync(request, cancellationToken);
            return ApiResponse<TagDto>.Ok(created);
        }

        [HttpPut("{id:guid}")]
        public async Task<ApiResponse<TagDto>> Update(Guid id, [FromBody] UpdateTagRequest request, CancellationToken cancellationToken)
        {
            var updated = await _tags.UpdateAsync(id, request, cancellationToken);
            return ApiResponse<TagDto>.Ok(updated);
        }

        [HttpDelete("{id:guid}")]
        public async Task<ApiResponse<object?>> Delete(Guid id, [FromQuery] int version, CancellationToken cancellationToken)
        {
            await _tags.DeleteAsync(id, version, cancellationToken);
            return ApiResponse.Ok();
        }
    }
}
