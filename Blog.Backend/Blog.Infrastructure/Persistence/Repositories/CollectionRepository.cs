using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using Microsoft.EntityFrameworkCore;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class CollectionRepository : BaseRepository<Collection>, ICollectionRepository
    {
        public CollectionRepository(BlogDbContext context) : base(context)
        {
        }

        public async Task<List<Collection>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return [];

            return await _context.Collections
                .Where(c => list.Contains(c.Id))
                .ToListAsync(cancellationToken);
        }

        public async Task<Collection?> GetWithPostsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Collections
                .Include(c => c.PostLinks)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        }

        public async Task<bool> ExistsBySlugAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default)
        {
            var normalized = slug.Trim().ToLowerInvariant();
            return await _context.Collections
                .AnyAsync(c => c.Slug == normalized && (excludeId == null || c.Id != excludeId), cancellationToken);
        }
    }
}
