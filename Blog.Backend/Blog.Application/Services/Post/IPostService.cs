using Blog.Application.Common;

namespace Blog.Application.Services.Post
{
    public interface IPostService
    {
        Task<PagedResult<PostCardDto>> GetPagedAsync(PostQueryRequest query, CancellationToken cancellationToken = default);

        /// <summary>详情（**计数**）：浏览量 +1。用于公开文章页</summary>
        Task<PostDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 详情（**不计数**）：管理端/编辑页取数据用。
        /// 避免「后台取 version 查看一次就 +1 浏览量」的数据污染（Q12）。
        /// </summary>
        Task<PostDetailDto?> GetDetailReadonlyAsync(Guid id, CancellationToken cancellationToken = default);

        Task<List<ArchiveGroupDto>> GetArchivesAsync(CancellationToken cancellationToken = default);

        Task<PostDetailDto> CreateAsync(CreatePostRequest request, CancellationToken cancellationToken = default);

        Task<PostDetailDto> UpdateAsync(Guid id, UpdatePostRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default);

        Task<PostDetailDto> PublishAsync(Guid id, int version, bool publish, CancellationToken cancellationToken = default);
    }
}
