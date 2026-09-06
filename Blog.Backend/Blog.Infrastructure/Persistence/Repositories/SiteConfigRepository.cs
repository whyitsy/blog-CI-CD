using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using Microsoft.EntityFrameworkCore;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class SiteConfigRepository : BaseRepository<SiteConfig>, ISiteConfigRepository
    {
        public SiteConfigRepository(BlogDbContext context) : base(context)
        {
        }

        public async Task<SiteConfig?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            return await _context.SiteConfigs.FirstOrDefaultAsync(c => c.Key == key, cancellationToken);
        }
    }
}
