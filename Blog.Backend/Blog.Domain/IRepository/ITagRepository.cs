using Blog.Domain.Entities;

namespace Blog.Domain.IRepository
{
    public interface ITagRepository : IBaseRepository<Tag>
    {
        Task<List<Tag>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
        Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);
        Task<int> CountPostsAsync(Guid tagId, CancellationToken cancellationToken = default);
    }
}
