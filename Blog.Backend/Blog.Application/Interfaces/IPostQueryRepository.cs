using Blog.Application.Common;
using Blog.Application.Services.Post;

namespace Blog.Application.Interfaces
{
    /// <summary>
    /// 文章只读查询仓储（返回 DTO 投影，避免列表页加载全文内容）
    /// </summary>
    public interface IPostQueryRepository
    {
        Task<PagedResult<PostCardDto>> GetPagedAsync(PostQueryRequest query, CancellationToken cancellationToken = default);

        Task<PostDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>归档时间轴：按年月倒序分组，每组内按发布时间倒序</summary>
        Task<List<ArchiveGroupDto>> GetArchivesAsync(CancellationToken cancellationToken = default);
    }
}
