using Blog.Application.Interfaces;
using Blog.Application.Services.Collection;
using Microsoft.EntityFrameworkCore;

namespace Blog.Infrastructure.Persistence.Repositories
{
    /// <summary>专栏只读查询：直接投影为 DTO</summary>
    public class CollectionQueryRepository : ICollectionQueryRepository
    {
        private readonly BlogDbContext _context;

        public CollectionQueryRepository(BlogDbContext context)
        {
            _context = context;
        }

        public async Task<List<CollectionDto>> GetAllAsync(bool includeUnpublished, CancellationToken cancellationToken = default)
        {
            var source = _context.Collections.AsNoTracking().Where(c => !c.IsDeleted);
            if (!includeUnpublished)
                source = source.Where(c => c.IsPublished);

            return await source
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.CreatedAt)
                .Select(c => new CollectionDto(
                    c.Id,
                    c.Title,
                    c.Slug,
                    c.Description,
                    c.CoverImage,
                    c.SortOrder,
                    c.IsPublished,
                    // 只统计已发布文章（未发布的不该出现在前台的计数里）
                    c.PostLinks.Count(l => !l.Post!.IsDeleted && l.Post.PublishedAt != null),
                    c.Version))
                .ToListAsync(cancellationToken);
        }

        public async Task<CollectionDetailDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
        {
            var normalized = slug.Trim().ToLowerInvariant();
            var id = await _context.Collections
                .AsNoTracking()
                .Where(c => !c.IsDeleted && c.Slug == normalized)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return id is null ? null : await GetByIdAsync(id.Value, cancellationToken);
        }

        public async Task<CollectionDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Collections
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new CollectionDetailDto(
                    c.Id,
                    c.Title,
                    c.Slug,
                    c.Description,
                    c.CoverImage,
                    c.SortOrder,
                    c.IsPublished,
                    c.PostLinks.Count(l => !l.Post!.IsDeleted && l.Post.PublishedAt != null),
                    c.Version,
                    c.PostLinks
                        .Where(l => !l.Post!.IsDeleted && l.Post.PublishedAt != null)
                        .OrderBy(l => l.SortOrder)
                        .Select(l => new CollectionPostItemDto(
                            l.PostId,
                            l.Post!.Title,
                            l.Post.Summary,
                            l.Post.CoverImage,
                            l.Post.PublishedAt,
                            l.Post.ViewCount,
                            l.SortOrder))
                        .ToList()))
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
