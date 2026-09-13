using Blog.Domain.Entities;

namespace Blog.Domain.IRepository
{
    public interface ICategoryRepository : IBaseRepository<Category>
    {
        Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);
        Task<int> CountPostsAsync(Guid categoryId, CancellationToken cancellationToken = default);
    }
}
