using Blog.Application.Common;
using Blog.Application.Interfaces;
using Blog.Application.Services.Category;
using Blog.Application.Services.Tag;
using Blog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Blog.Infrastructure.Persistence.Repositories
{
    /// <summary>分类/标签的只读查询（含已发布文章数统计）</summary>
    public class TaxonomyQueryRepository : ICategoryQueryRepository, ITagQueryRepository
    {
        private readonly BlogDbContext _context;

        public TaxonomyQueryRepository(BlogDbContext context)
        {
            _context = context;
        }

        async Task<List<CategoryDto>> ICategoryQueryRepository.GetAllWithPostCountAsync(CancellationToken cancellationToken)
        {
            return await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryDto(
                    c.Id,
                    c.Name,
                    c.Posts.Count(p => p.PublishedAt != null),
                    c.Version))
                .ToListAsync(cancellationToken);
        }

        async Task<List<TagDto>> ITagQueryRepository.GetAllWithPostCountAsync(CancellationToken cancellationToken)
        {
            return await _context.Tags
                .AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new TagDto(
                    t.Id,
                    t.Name,
                    t.Posts.Count(p => p.PublishedAt != null),
                    t.Version))
                .ToListAsync(cancellationToken);
        }
    }
}
