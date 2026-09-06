using Blog.Application.Common;

namespace Blog.Application.Services.Post
{
    public interface IPostService
    {
        Task<PagedResult<PostCardDto>> GetPagedAsync(PostQueryRequest query, CancellationToken cancellationToken = default);
        Task<PostDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);
        Task<List<ArchiveGroupDto>> GetArchivesAsync(CancellationToken cancellationToken = default);
        Task<PostDetailDto> CreateAsync(CreatePostRequest request, CancellationToken cancellationToken = default);
        Task<PostDetailDto> UpdateAsync(Guid id, UpdatePostRequest request, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default);
        Task<PostDetailDto> PublishAsync(Guid id, int version, bool publish, CancellationToken cancellationToken = default);
    }
}
