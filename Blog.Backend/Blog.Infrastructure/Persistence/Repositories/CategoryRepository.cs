using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using Microsoft.EntityFrameworkCore;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class CategoryRepository : BaseRepository<Category>, ICategoryRepository
    {
        public CategoryRepository(BlogDbContext context) : base(context)
        {
        }

        public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
        {
            return await _context.Categories.AnyAsync(
                c => c.Name == name && (!excludeId.HasValue || c.Id != excludeId.Value), cancellationToken);
        }

        public async Task<int> CountPostsAsync(Guid categoryId, CancellationToken cancellationToken = default)
        {
            return await _context.Categories
                .Where(c => c.Id == categoryId)
                .SelectMany(c => c.Posts)
                .CountAsync(p => p.PublishedAt != null, cancellationToken);
        }
    }
}
