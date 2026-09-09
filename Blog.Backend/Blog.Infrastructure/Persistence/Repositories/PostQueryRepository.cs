using Blog.Application.Common;
using Blog.Application.Interfaces;
using Blog.Application.Services.Post;
using Blog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Blog.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// 文章只读查询：直接投影为 DTO，列表页不加载 Content 全文
    /// </summary>
    public class PostQueryRepository : IPostQueryRepository
    {
        private readonly BlogDbContext _context;

        public PostQueryRepository(BlogDbContext context)
        {
            _context = context;
        }

        public async Task<PagedResult<PostCardDto>> GetPagedAsync(PostQueryRequest query, CancellationToken cancellationToken = default)
        {
            var published = _context.Posts.AsNoTracking().Where(p => !p.IsDeleted);
            if (!query.IncludeUnpublished)
            {
                published = published.Where(p => p.PublishedAt != null);
            }
            var q = ApplyFilters(published, query);

            var total = await q.CountAsync(cancellationToken);

            var items = await q
                .OrderByDescending(p => p.PublishedAt)
                .ThenByDescending(p => p.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(p => new PostCardDto(
                    p.Id,
                    p.Title,
                    p.Summary,
                    p.CoverImage,
                    p.CategoryId,
                    p.Category != null ? p.Category.Name : null,
                    p.Tags.Select(t => new TagBriefDto(t.Id, t.Name)).ToList(),
                    p.PublishedAt,
                    p.ViewCount))
                .ToListAsync(cancellationToken);

            return PagedResult<PostCardDto>.Create(items, query.Page, query.PageSize, total);
        }

        public async Task<PostDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Posts
                .AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new PostDetailDto(
                    p.Id,
                    p.Title,
                    p.Content,
                    p.Summary,
                    p.CoverImage,
                    p.CategoryId,
                    p.Category != null ? p.Category.Name : null,
                    p.Tags.Select(t => new TagBriefDto(t.Id, t.Name)).ToList(),
                    p.AuthorId,
                    p.Author != null ? p.Author.Name : null,
                    p.Author != null ? p.Author.Avatar : null,
                    p.PublishedAt,
                    p.UpdatedAt,
                    p.ViewCount,
                    p.WordCount,
                    p.Version))
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<List<ArchiveGroupDto>> GetArchivesAsync(CancellationToken cancellationToken = default)
        {
            var published = await _context.Posts
                .AsNoTracking()
                .Where(p => p.PublishedAt != null)
                .OrderByDescending(p => p.PublishedAt)
                .Select(p => new { p.Id, p.Title, PublishedAt = p.PublishedAt!.Value })
                .ToListAsync(cancellationToken);

            return published
                .GroupBy(p => new { p.PublishedAt.Year, p.PublishedAt.Month })
                .OrderByDescending(g => g.Key.Year)
                .ThenByDescending(g => g.Key.Month)
                .Select(g => new ArchiveGroupDto(
                    g.Key.Year,
                    g.Key.Month,
                    g.Select(p => new ArchiveItemDto(p.Id, p.Title, p.PublishedAt)).ToList()))
                .ToList();
        }

        private static IQueryable<Post> ApplyFilters(IQueryable<Post> source, PostQueryRequest query)
        {
            if (query.CategoryId.HasValue)
                source = source.Where(p => p.CategoryId == query.CategoryId.Value);

            if (query.TagId.HasValue)
                source = source.Where(p => p.Tags.Any(t => t.Id == query.TagId.Value));

            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                // 模糊匹配：标题 / 正文 / 分类名 / 标签名（ILIKE 语义，PostgreSQL 默认区分大小写，故统一小写比较）
                var kw = query.Keyword.Trim().ToLowerInvariant();
                source = source.Where(p =>
                    p.Title.ToLower().Contains(kw) ||
                    p.Content.ToLower().Contains(kw) ||
                    (p.Category != null && p.Category.Name.ToLower().Contains(kw)) ||
                    p.Tags.Any(t => t.Name.ToLower().Contains(kw)));
            }

            return source;
        }
    }
}
