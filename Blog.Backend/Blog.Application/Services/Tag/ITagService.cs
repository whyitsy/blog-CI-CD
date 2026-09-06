using Blog.Application.Common;
using Blog.Application.Services.Tag;

namespace Blog.Application.Services.Tag
{
    public interface ITagService
    {
        Task<List<TagDto>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<TagDto> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken = default);
        Task<TagDto> UpdateAsync(Guid id, UpdateTagRequest request, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default);
    }
}
