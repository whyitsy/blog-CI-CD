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
        /// <summary>中文全文检索配置名（由迁移创建，见 docs/02-架构与数据模型.md §9.4 / T12）</summary>
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

            if (keyword is not null)
                return await GetPagedByRelevanceAsync(query, keyword, cancellationToken);

            source = ApplyFilters(source, query);

            var total = await source.CountAsync(cancellationToken);

            var items = await source
                .OrderByDescending(p => p.PublishedAt)
                .ThenByDescending(p => p.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(ProjectCard())
                .ToListAsync(cancellationToken);

            return PagedResult<PostCardDto>.Create(items, query.Page, query.PageSize, total);
        }

        /// <summary>
        /// 关键词检索 + **相关度排序**（T12）。
        ///
        /// 实现要点：把 <c>ts_rank</c> 作为**投影列**算出来，而不是在原生 SQL 里直接 ORDER BY。
        /// 这样做的原因是保留可组合性：
        ///   - 原生 SQL 只负责「匹配 + 打分」，外层用 <c>Contains</c> 关联回 Posts
        ///   - 分类 / 标签 / 专栏 / 作者 / 归属等过滤与最终分页仍由 LINQ 表达，
        ///     因此分页与总数是**过滤之后**的，不会出现「先分页再过滤」导致的错页
        ///   - 若在原生 SQL 里直接 ORDER BY + LIMIT，这些过滤就无法安全叠加
        ///
        /// 关于 <c>ts_rank</c> 的一个坑：PostgreSQL 里 ts_rank 属于窗口函数类，
        /// **不能直接写进 WHERE**（会报 0A000 window functions are not allowed in WHERE），
        /// 因此这里用 **CTE** 先把分数算出来，再在外层 ORDER BY。
        ///
        /// 排序规则：相关度降序 → 发布时间降序 → Id 降序（保证分页稳定）。
        /// 权重来自生成列：标题 A &gt; 摘要 B &gt; 正文 C（见 BlogDbContext 的 SearchVector 定义）。
        /// </summary>
        private async Task<PagedResult<PostCardDto>> GetPagedByRelevanceAsync(
            PostQueryRequest query, string keyword, CancellationToken cancellationToken)
        {
            // 用 Database.SqlQuery<T> 直接拿「按相关度排好序的 Id 列表」：
            //   - 它是 EF Core 7+ 的标量原生查询，**不需要映射实体类型**，
            //     因此不会引入 keyless entity 的模型/迁移负担，
            //     也不会触发「keyless entity 无法关联投影」的翻译错误。
            //   - 参数由 SqlQuery 自动参数化，不存在注入。
            //   - 配置名必须显式 ::regconfig 转型，否则报
            //     42883: function plainto_tsquery(text, text) does not exist
            var config = ChineseTextSearchConfig;
            var rankedIds = await _context.Database
                .SqlQuery<Guid>($@"
                    WITH ranked AS (
                        SELECT
                            p.""Id"" AS ""Value"",
                            ts_rank(p.""SearchVector"", plainto_tsquery({config}::regconfig, {keyword})) AS rank
                        FROM ""Posts"" AS p
                        WHERE p.""SearchVector"" @@ plainto_tsquery({config}::regconfig, {keyword})
                    )
                    SELECT r.""Value"" FROM ranked AS r
                    ORDER BY r.rank DESC, r.""Value"" DESC")
                .ToListAsync(cancellationToken);

            // 保留 EF 能关联的写法：Id IN (...)。
            // 之前试过用 join keyless 实体来带出 rank 列，但 EF 在 join 之后
            // 投影 Tags 集合时会报「cannot correlate on keyless entity type」——
            // 因为 keyless 类型无法唯一标识父行。Contains 形式不引入新实体，翻译是安全的。
            var source = _context.Posts
                .AsNoTracking()
                .Where(p => !p.IsDeleted && rankedIds.Contains(p.Id));

            if (!query.IncludeUnpublished)
                source = source.Where(p => p.PublishedAt != null);

            source = ApplyFilters(source, query);

            var total = await source.CountAsync(cancellationToken);

            var pageIds = rankedIds
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();

            if (pageIds.Count == 0)
                return PagedResult<PostCardDto>.Create([], query.Page, query.PageSize, total);

            // 取本页数据（顺序由数据库决定，下面再按相关度重排）
            var items = await source
                .Where(p => pageIds.Contains(p.Id))
                .Select(ProjectCard())
                .ToListAsync(cancellationToken);

            // 按相关度排序：真正决定顺序的是 rankedIds 的下标。
            // 在内存里排（每页至多 pageSize 条）比让 EF 去翻译 ts_rank 排序更简单可靠，
            // 代价可忽略（列表中不加载 Content 全文）。
            var order = pageIds
                .Select((id, index) => (id, index))
                .ToDictionary(x => x.id, x => x.index);
            items.Sort((a, b) => order[a.Id].CompareTo(order[b.Id]));

            return PagedResult<PostCardDto>.Create(items, query.Page, query.PageSize, total);
        }

        /// <summary>列表卡片投影（非检索路径与检索路径共用，避免两处字段不同步）</summary>
        private static System.Linq.Expressions.Expression<Func<Post, PostCardDto>> ProjectCard() =>
            p => new PostCardDto(
                p.Id,
                p.Title,
                p.Summary,
                p.CoverImage,
                p.CategoryId,
                p.Category != null ? p.Category.Name : null,
                p.Tags.Select(t => new TagBriefDto(t.Id, t.Name)).ToList(),
                p.PublishedAt,
                p.ViewCount);

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

        /// <summary>
        /// 纯文章查询的过滤条件。
        /// 检索路径的过滤写在 <see cref="GetPagedByRelevanceAsync"/> 里（作用于 Join 后的元素），
        /// 两处条件必须保持一致——新增过滤条件时记得同步。
        /// </summary>
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
