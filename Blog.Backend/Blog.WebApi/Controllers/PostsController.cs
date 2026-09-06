using Blog.Application.Common;
using Blog.Application.Services.Post;
using Microsoft.AspNetCore.Mvc;

namespace Blog.WebApi.Controllers
{
    /// <summary>文章：列表 / 详情 / 归档 / 搜索 / 管理</summary>
    [ApiController]
    [Route("api/posts")]
    public class PostsController : ControllerBase
    {
        private readonly IPostService _posts;

        public PostsController(IPostService posts)
        {
            _posts = posts;
        }

        /// <summary>分页文章列表（支持 categoryId / tagId / keyword 组合过滤，keyword 为模糊搜索）</summary>
        [HttpGet]
        public async Task<ApiResponse<PagedResult<PostCardDto>>> GetPaged(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12,
            [FromQuery] Guid? categoryId = null,
            [FromQuery] Guid? tagId = null,
            [FromQuery] string? keyword = null,
            CancellationToken cancellationToken = default)
        {
            var query = new PostQueryRequest
            {
                Page = page,
                PageSize = pageSize,
                CategoryId = categoryId,
                TagId = tagId,
                Keyword = keyword
            };
            var result = await _posts.GetPagedAsync(query, cancellationToken);
            return ApiResponse<PagedResult<PostCardDto>>.Ok(result);
        }

        /// <summary>文章详情（自动累计浏览次数）</summary>
        [HttpGet("{id:guid}")]
        public async Task<ApiResponse<PostDetailDto>> GetDetail(Guid id, CancellationToken cancellationToken)
        {
            var detail = await _posts.GetDetailAsync(id, cancellationToken)
                ?? throw new Blog.Application.Common.Exceptions.BusinessException("文章不存在", ErrorCodes.NotFound);
            return ApiResponse<PostDetailDto>.Ok(detail);
        }

        /// <summary>归档：按年月分组的时间轴数据</summary>
        [HttpGet("archives")]
        public async Task<ApiResponse<List<ArchiveGroupDto>>> GetArchives(CancellationToken cancellationToken)
        {
            var archives = await _posts.GetArchivesAsync(cancellationToken);
            return ApiResponse<List<ArchiveGroupDto>>.Ok(archives);
        }

        /// <summary>搜索接口（keyword 模糊匹配标题 / 摘要 / 内容）</summary>
        [HttpGet("search")]
        public async Task<ApiResponse<PagedResult<PostCardDto>>> Search(
            [FromQuery] string keyword,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12,
            CancellationToken cancellationToken = default)
        {
            var query = new PostQueryRequest { Page = page, PageSize = pageSize, Keyword = keyword };
            var result = await _posts.GetPagedAsync(query, cancellationToken);
            return ApiResponse<PagedResult<PostCardDto>>.Ok(result);
        }

        [HttpPost]
        public async Task<ApiResponse<PostDetailDto>> Create([FromBody] CreatePostRequest request, CancellationToken cancellationToken)
        {
            var created = await _posts.CreateAsync(request, cancellationToken);
            return ApiResponse<PostDetailDto>.Ok(created);
        }

        [HttpPut("{id:guid}")]
        public async Task<ApiResponse<PostDetailDto>> Update(Guid id, [FromBody] UpdatePostRequest request, CancellationToken cancellationToken)
        {
            var updated = await _posts.UpdateAsync(id, request, cancellationToken);
            return ApiResponse<PostDetailDto>.Ok(updated);
        }

        /// <summary>发布 / 下架，需携带当前版本号</summary>
        [HttpPost("{id:guid}/publish")]
        public async Task<ApiResponse<PostDetailDto>> Publish(
            Guid id,
            [FromQuery] int version,
            [FromQuery] bool publish = true,
            CancellationToken cancellationToken = default)
        {
            var updated = await _posts.PublishAsync(id, version, publish, cancellationToken);
            return ApiResponse<PostDetailDto>.Ok(updated);
        }

        [HttpDelete("{id:guid}")]
        public async Task<ApiResponse<object?>> Delete(Guid id, [FromQuery] int version, CancellationToken cancellationToken)
        {
            await _posts.DeleteAsync(id, version, cancellationToken);
            return ApiResponse.Ok();
        }
    }
}
