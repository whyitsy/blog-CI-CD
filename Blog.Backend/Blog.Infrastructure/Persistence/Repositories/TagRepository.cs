using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using Microsoft.EntityFrameworkCore;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class TagRepository : BaseRepository<Tag>, ITagRepository
    {
        public TagRepository(BlogDbContext context) : base(context)
        {
        }

        public async Task<List<Tag>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            var idList = ids.Distinct().ToList();
            return await _context.Tags
                .Where(t => idList.Contains(t.Id))
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
        {
            return await _context.Tags.AnyAsync(
                t => t.Name == name && (!excludeId.HasValue || t.Id != excludeId.Value), cancellationToken);
        }

        public async Task<int> CountPostsAsync(Guid tagId, CancellationToken cancellationToken = default)
        {
            return await _context.Tags
                .Where(t => t.Id == tagId)
                .SelectMany(t => t.Posts)
                .CountAsync(p => p.PublishedAt != null, cancellationToken);
        }
    }
}
