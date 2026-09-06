using Blog.Domain.Entities;

namespace Blog.Domain.IRepository
{
    public interface ISiteConfigRepository : IBaseRepository<SiteConfig>
    {
        Task<SiteConfig?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
    }
}
