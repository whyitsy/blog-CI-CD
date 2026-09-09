using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using PostEntity = Blog.Domain.Entities.Post;
using TagEntity = Blog.Domain.Entities.Tag;

namespace Blog.Application.Services.Post
{
    public class PostService : IPostService
    {
        private static readonly TimeSpan ListCacheTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan DetailCacheTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ArchiveCacheTtl = TimeSpan.FromMinutes(30);

        private readonly IPostQueryRepository _postQuery;
        private readonly IPostRepository _posts;
        private readonly ITagRepository _tags;
        private readonly ICategoryRepository _categories;
        private readonly IAuthorRepository _authors;
        private readonly IUnitOfWork _uow;
        private readonly ICacheService _cache;

        public PostService(
            IPostQueryRepository postQuery,
            IPostRepository posts,
            ITagRepository tags,
            ICategoryRepository categories,
            IAuthorRepository authors,
            IUnitOfWork uow,
            ICacheService cache)
        {
            _postQuery = postQuery;
            _posts = posts;
            _tags = tags;
            _categories = categories;
            _authors = authors;
            _uow = uow;
            _cache = cache;
        }

        public Task<PagedResult<PostCardDto>> GetPagedAsync(PostQueryRequest query, CancellationToken cancellationToken = default)
        {
            var normalized = Normalize(query);
            return _cache.GetOrCreateAsync(
                CacheKeys.PostList(normalized),
                ct => _postQuery.GetPagedAsync(normalized, ct),
                ListCacheTtl,
                cacheNull: true,
                cancellationToken)!;
        }

        public async Task<PostDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var detail = await _cache.GetOrCreateAsync(
                CacheKeys.PostDetail(id),
                ct => _postQuery.GetDetailAsync(id, ct),
                DetailCacheTtl,
                cacheNull: false, // 详情不存在不写哨兵，避免新建后立即读到空
                cancellationToken);

            if (detail is null) return null;

            // 浏览量：DB 原子自增（跳过乐观锁，避免高频访问产生并发冲突），同步刷新缓存值
            await _posts.IncrementViewCountAsync(id);
            var updated = detail with { ViewCount = detail.ViewCount + 1 };
            await _cache.SetAsync(CacheKeys.PostDetail(id), updated, DetailCacheTtl, cancellationToken);

            return updated;
        }

        public async Task<List<ArchiveGroupDto>> GetArchivesAsync(CancellationToken cancellationToken = default)
        {
            var archives = await _cache.GetOrCreateAsync(
                CacheKeys.PostArchives,
                ct => _postQuery.GetArchivesAsync(ct),
                ArchiveCacheTtl,
                cacheNull: true,
                cancellationToken);

            return archives ?? [];
        }

        public async Task<PostDetailDto> CreateAsync(CreatePostRequest request, CancellationToken cancellationToken = default)
        {
            ValidateTitleAndContent(request.Title, request.Content);

            var authorId = await ResolveDefaultAuthorIdAsync(cancellationToken);

            if (request.CategoryId.HasValue &&
                await _categories.GetByIdAsync(request.CategoryId.Value, cancellationToken) is null)
            {
                throw new BusinessException("分类不存在", ErrorCodes.NotFound);
            }

            var post = new PostEntity(request.Title, request.Content, authorId, request.CategoryId,
                request.CoverImage ?? string.Empty, publishNow: request.Publish);

            await ApplyTagsAsync(post, request.TagIds, cancellationToken);

            await _posts.AddAsync(post, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidatePostCachesAsync(cancellationToken);

            return await LoadDetailOrThrowAsync(post.Id, cancellationToken);
        }

        public async Task<PostDetailDto> UpdateAsync(Guid id, UpdatePostRequest request, CancellationToken cancellationToken = default)
        {
            ValidateTitleAndContent(request.Title, request.Content);

            var post = await _posts.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("文章不存在", ErrorCodes.NotFound);

            // 乐观锁：以客户端版本号为基准，UPDATE ... WHERE "Version" = @expected
            _posts.ApplyOptimisticVersion(post, ValidateVersion(request.Version));

            post.Update(request.Title, request.Content, request.CategoryId, request.CoverImage ?? string.Empty);
            await ApplyTagsAsync(post, request.TagIds, cancellationToken);

            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidatePostCachesAsync(cancellationToken);

            return await LoadDetailOrThrowAsync(id, cancellationToken);
        }

        public async Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default)
        {
            var post = await _posts.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("文章不存在", ErrorCodes.NotFound);

            _posts.ApplyOptimisticVersion(post, ValidateVersion(version));

            _posts.Remove(post);
            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidatePostCachesAsync(cancellationToken);
        }

        public async Task<PostDetailDto> PublishAsync(Guid id, int version, bool publish, CancellationToken cancellationToken = default)
        {
            var post = await _posts.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("文章不存在", ErrorCodes.NotFound);

            _posts.ApplyOptimisticVersion(post, ValidateVersion(version));

            if (publish) post.Publish(); else post.Unpublish();

            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidatePostCachesAsync(cancellationToken);

            return await LoadDetailOrThrowAsync(id, cancellationToken);
        }

        private static int ValidateVersion(int version)
        {
            if (version < 1)
                throw new BusinessException("缺少合法的版本号，无法进行并发控制", ErrorCodes.InvalidArgument);
            return version;
        }

        private async Task ApplyTagsAsync(Domain.Entities.Post post, List<Guid>? tagIds, CancellationToken cancellationToken)
        {
            var ids = (tagIds ?? []).Distinct().ToList();
            var tags = ids.Count == 0 ? new List<TagEntity>() : await _tags.GetByIdsAsync(ids, cancellationToken);

            if (tags.Count != ids.Count)
                throw new BusinessException("存在不合法的标签 id", ErrorCodes.InvalidArgument);

            // 增量同步多对多关系：只移除取消关联的、只添加新增的，
            // 避免整体替换导致 PostTag 连接行重复插入（主键冲突）
            foreach (var existing in post.Tags.Where(t => !ids.Contains(t.Id)).ToList())
                post.Tags.Remove(existing);

            var existingIds = post.Tags.Select(t => t.Id).ToHashSet();
            foreach (var tag in tags.Where(t => !existingIds.Contains(t.Id)))
                post.Tags.Add(tag);
        }

        private async Task<PostDetailDto> LoadDetailOrThrowAsync(Guid id, CancellationToken cancellationToken)
        {
            // 写入后立即失效缓存，直接读库保证拿到最新数据
            await _cache.RemoveAsync(CacheKeys.PostDetail(id), cancellationToken);
            return await _postQuery.GetDetailAsync(id, cancellationToken)
                ?? throw new BusinessException("文章不存在", ErrorCodes.NotFound);
        }

        private async Task<Guid> ResolveDefaultAuthorIdAsync(CancellationToken cancellationToken)
        {
            var authors = await _authors.QueryByConditionAsync(a => true, cancellationToken);
            var author = authors.FirstOrDefault()
                ?? throw new BusinessException("尚未创建作者，无法发布文章", ErrorCodes.BusinessRule);
            return author.Id;
        }

        private Task InvalidatePostCachesAsync(CancellationToken cancellationToken)
        {
            // 文章变更影响所有列表/详情/归档与统计缓存
            return Task.WhenAll(
                _cache.RemoveByPrefixAsync(CacheKeys.PostsPrefix, cancellationToken),
                _cache.RemoveAsync(CacheKeys.SiteStats, cancellationToken));
        }

        private static PostQueryRequest Normalize(PostQueryRequest query)
        {
            var page = query.Page < 1 ? 1 : query.Page;
            // 管理端 IncludeUnpublished 时允许更大分页；公网首页限制 50
            var maxSize = query.IncludeUnpublished ? 100 : 50;
            int pageSize;
            if (query.PageSize < 1 || query.PageSize > maxSize) pageSize = 12;
            else pageSize = query.PageSize;
            var keyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();

            return query with { Page = page, PageSize = pageSize, Keyword = keyword };
        }

        private static void ValidateTitleAndContent(string title, string content)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new BusinessException("标题不能为空", ErrorCodes.InvalidArgument);
            if (title.Length > 200)
                throw new BusinessException("标题长度不能超过 200", ErrorCodes.InvalidArgument);
            if (string.IsNullOrWhiteSpace(content))
                throw new BusinessException("内容不能为空", ErrorCodes.InvalidArgument);
        }
    }
}
