using Blog.Application.Common;
using Blog.Application.Interfaces;
using Blog.Application.Services.Site;
using Microsoft.EntityFrameworkCore;

namespace Blog.Infrastructure.Persistence.Repositories
{
    /// <summary>站点配置、社交链接与统计的只读查询</summary>
    public class SiteQueryRepository : ISiteQueryRepository
    {
        private readonly BlogDbContext _context;

        public SiteQueryRepository(BlogDbContext context)
        {
            _context = context;
        }

        public async Task<List<SiteConfigItemDto>> GetAllConfigsAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SiteConfigs
                .AsNoTracking()
                .Select(c => new SiteConfigItemDto(c.Key, c.Value, c.Version))
                .ToListAsync(cancellationToken);
        }

        public async Task<List<SocialLinkDto>> GetVisibleSocialLinksAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SocialLinks
                .AsNoTracking()
                .Where(l => l.IsVisible)
                .OrderBy(l => l.SortOrder)
                .Select(l => new SocialLinkDto(l.Id, l.Name, l.Icon, l.Url, l.SortOrder, l.IsVisible,
                    l.Version))
                .ToListAsync(cancellationToken);
        }

        public async Task<SiteStatsDto> GetStatsAsync(DateTimeOffset? foundingDate, CancellationToken cancellationToken = default)
        {
            var published = _context.Posts.Where(p => p.PublishedAt != null);

            var totalPosts = await published.CountAsync(cancellationToken);
            var totalWords = await published.SumAsync(p => (long)p.WordCount, cancellationToken);
            var totalViews = await published.SumAsync(p => (long)p.ViewCount, cancellationToken);
            var tagCount = await _context.Tags.CountAsync(cancellationToken);
            var categoryCount = await _context.Categories.CountAsync(cancellationToken);

            var siteDays = foundingDate.HasValue
                ? (int)Math.Max(1, Math.Floor((DateTimeOffset.UtcNow.Date - foundingDate.Value.Date).TotalDays) + 1)
                : 0;

            return new SiteStatsDto(siteDays, totalPosts, totalWords, (int)totalViews, tagCount, categoryCount);
        }
    }
}
