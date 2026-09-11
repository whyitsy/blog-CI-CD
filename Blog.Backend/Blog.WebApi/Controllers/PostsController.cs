using Blog.Application.Common;
using Blog.Application.Services.Post;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blog.WebApi.Controllers
{
    /// <summary>文章：列表 / 详情 / 归档 / 搜索 / 管理</summary>
    [ApiController]
    [Route("api/posts")]
    public class PostsController : ControllerBase
    {
        private readonly IPostService _posts;
        private readonly Blog.Application.Interfaces.ICurrentUser _currentUser;

        public PostsController(IPostService posts, Blog.Application.Interfaces.ICurrentUser currentUser)
        {
            _posts = posts;
            _currentUser = currentUser;
        }

        /// <summary>
        /// 分页文章列表（支持 categoryId / tagId / collectionId / authorId / keyword 组合过滤）。
        /// keyword 走中文全文检索。
        /// includeUnpublished=true 时必须已登录，且 Author 角色只能看到自己创建的文章
        /// （数据级过滤在 PostService 内完成，见草稿权限保护 Q7）。
        /// </summary>
        [HttpGet]
        public async Task<ApiResponse<PagedResult<PostCardDto>>> GetPaged(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12,
            [FromQuery] Guid? categoryId = null,
            [FromQuery] Guid? tagId = null,
            [FromQuery] string? keyword = null,
            [FromQuery] bool includeUnpublished = false,
            [FromQuery] Guid? collectionId = null,
            [FromQuery] Guid? authorId = null,
            [FromQuery] bool mine = false,
            CancellationToken cancellationToken = default)
        {
            var query = new PostQueryRequest
            {
                Page = page,
                PageSize = pageSize,
                CategoryId = categoryId,
                TagId = tagId,
                Keyword = keyword,
                IncludeUnpublished = includeUnpublished,
                CollectionId = collectionId,
                AuthorId = authorId,
                // mine=true 时只看自己创建的（作者工作区）。服务层会校验与当前登录者一致。
                OwnedByUserId = mine ? _currentUser.UserId : null,
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

        /// <summary>
        /// 文章详情（**不计数**）：供管理端/编辑器取数据用。
        /// 与 GET /api/posts/{id} 的区别是**不会**让浏览量 +1，
        /// 避免「后台点一次编辑就 +1」污染统计（见 docs/10-决策记录.md §3 / Q12）。
        /// 需要登录（编辑器场景），且未发布文章仍受草稿权限保护。
        /// </summary>
        [HttpGet("{id:guid}/readonly")]
        [Authorize(Policy = "ContentWriter")]
        public async Task<ApiResponse<PostDetailDto>> GetDetailReadonly(Guid id, CancellationToken cancellationToken)
        {
            var detail = await _posts.GetDetailReadonlyAsync(id, cancellationToken)
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
        [Authorize(Policy = "ContentWriter")]
        public async Task<ApiResponse<PostDetailDto>> Create([FromBody] CreatePostRequest request, CancellationToken cancellationToken)
        {
            var created = await _posts.CreateAsync(request, cancellationToken);
            return ApiResponse<PostDetailDto>.Ok(created);
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = "ContentWriter")]
        public async Task<ApiResponse<PostDetailDto>> Update(Guid id, [FromBody] UpdatePostRequest request, CancellationToken cancellationToken)
        {
            var updated = await _posts.UpdateAsync(id, request, cancellationToken);
            return ApiResponse<PostDetailDto>.Ok(updated);
        }

        /// <summary>发布 / 下架，需携带当前版本号</summary>
        [HttpPost("{id:guid}/publish")]
        [Authorize(Policy = "ContentWriter")]
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
        [Authorize(Policy = "ContentWriter")]
        public async Task<ApiResponse<object?>> Delete(Guid id, [FromQuery] int version, CancellationToken cancellationToken)
        {
            await _posts.DeleteAsync(id, version, cancellationToken);
            return ApiResponse.Ok();
        }
    }
}
