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
        /// <summary>中文全文检索配置名（由迁移创建，见 docs/03-后端设计.md §7.4 / T12）</summary>
        private const string ChineseTextSearchConfig = "chinese";

        private readonly BlogDbContext _context;

        public PostQueryRepository(BlogDbContext context)
        {
            _context = context;
        }

        public async Task<PagedResult<PostCardDto>> GetPagedAsync(PostQueryRequest query, CancellationToken cancellationToken = default)
        {
            var source = _context.Posts.AsNoTracking().Where(p => !p.IsDeleted);
            if (!query.IncludeUnpublished)
            {
                source = source.Where(p => p.PublishedAt != null);
            }

            var keyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();

            IQueryable<Post> filtered;
            if (keyword is not null)
            {
                // 中文全文检索。这里用参数化原生 SQL 而不是 EF.Functions.PlainToTsQuery：
                //   1. 可翻译性：在「影子属性 + 参数化 tsquery」形态下，EF Core 会把
                //      PlainToTsQuery 判为客户端求值并抛异常（实测确认）。
                //   2. 用 `Id IN (子查询)` 而不是直接 FromSql 包整表，是为了让外层仍能自由组合
                //      分类/标签/作者等过滤与投影，避免 FromSql 与后续 Where 的组合限制。
                //   3. 参数由 FromSqlInterpolated 自动参数化，不存在 SQL 注入。
                // plainto_tsquery 会把自然语言输入安全地转成 tsquery，
                // 因此用户输入的 & | ! 等 tsquery 语法字符不会被当作操作符。
                // 注意：配置名必须显式 ::regconfig 转型。
                // PostgreSQL 的 plainto_tsquery 重载是 (regconfig, text) / (text)，
                // 没有 (text, text)；参数化时若只传字符串会报
                //   42883: function plainto_tsquery(text, text) does not exist
                var config = ChineseTextSearchConfig;
                var matchedIds = _context.Posts
                    .FromSqlInterpolated($@"
                        SELECT p.* FROM ""Posts"" AS p
                        WHERE p.""SearchVector"" @@ plainto_tsquery({config}::regconfig, {keyword})")
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted && p.PublishedAt != null)
                    .Select(p => p.Id);

                filtered = _context.Posts.AsNoTracking().Where(p => matchedIds.Contains(p.Id));
            }
            else
            {
                filtered = source;
            }

            source = ApplyFilters(filtered, query);

            var total = await source.CountAsync(cancellationToken);

            // 排序：按发布时间倒序。
            // TODO(相关度排序)：生成列已带 setweight 权重（标题 A > 摘要 B > 正文 C），
            // 但 EF Core 对 ts_rank 的翻译在“影子属性 + 参数化 tsquery”形态下会退回客户端求值，
            // 因此暂不做相关度排序；命中集合本身已由 GIN 索引加速。
            // 后续可用原生 SQL 或映射 ts_rank 用户函数补上（见 docs/03-后端设计.md §7.4 / T12）。
            var ordered = source
                .OrderByDescending(p => p.PublishedAt)
                .ThenByDescending(p => p.Id);

            var items = await ordered
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
                    p.CollectionLinks
                        .OrderBy(l => l.SortOrder)
                        .Select(l => new CollectionBriefDto(l.CollectionId, l.Collection!.Title, l.Collection.Slug))
                        .ToList(),
                    p.AuthorId ?? Guid.Empty,
                    p.Author != null ? p.Author.Name : null,
                    p.Author != null ? p.Author.Avatar : null,
                    p.CreatedByUserId,
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

            if (query.CollectionId.HasValue)
                source = source.Where(p => p.CollectionLinks.Any(l => l.CollectionId == query.CollectionId.Value));

            if (query.AuthorId.HasValue)
                source = source.Where(p => p.AuthorId == query.AuthorId.Value);

            // 作者工作区：只看自己创建的
            if (query.OwnedByUserId.HasValue)
                source = source.Where(p => p.CreatedByUserId == query.OwnedByUserId.Value);

            return source;
        }
    }
}
