using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using PostEntity = Blog.Domain.Entities.Post;
using TagEntity = Blog.Domain.Entities.Tag;
using CollectionEntity = Blog.Domain.Entities.Collection;

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
        private readonly ICollectionRepository _collections;
        private readonly IUnitOfWork _uow;
        private readonly ICacheService _cache;
        private readonly ICurrentUser _currentUser;

        public PostService(
            IPostQueryRepository postQuery,
            IPostRepository posts,
            ITagRepository tags,
            ICategoryRepository categories,
            IAuthorRepository authors,
            ICollectionRepository collections,
            IUnitOfWork uow,
            ICacheService cache,
            ICurrentUser currentUser)
        {
            _postQuery = postQuery;
            _posts = posts;
            _tags = tags;
            _categories = categories;
            _authors = authors;
            _collections = collections;
            _uow = uow;
            _cache = cache;
            _currentUser = currentUser;
        }

        public Task<PagedResult<PostCardDto>> GetPagedAsync(PostQueryRequest query, CancellationToken cancellationToken = default)
        {
            var normalized = Normalize(query);

            // 管理端：Author 角色只能看到自己的草稿/文章；Admin 可见全部。
            // 这是「草稿权限保护」（Q7）的关键一环 —— 仅靠端点鉴权不够，必须过滤数据。
            if (normalized.IncludeUnpublished && !_currentUser.IsAdmin)
            {
                // 非管理员读「含草稿」列表时，强制限定为**自己创建的**（账号维度）
                if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
                    throw new BusinessException("无权查看未发布内容", ErrorCodes.Forbidden);

                normalized = normalized with { OwnedByUserId = _currentUser.UserId };
            }

            // 作者工作区：显式只看自己的
            if (normalized.OwnedByUserId.HasValue && !_currentUser.IsAdmin &&
                normalized.OwnedByUserId != _currentUser.UserId)
            {
                throw new BusinessException("无权查看他人的文章", ErrorCodes.Forbidden);
            }

            return _cache.GetOrCreateAsync(
                CacheKeys.PostList(normalized),
                ct => _postQuery.GetPagedAsync(normalized, ct),
                ListCacheTtl,
                cacheNull: true,
                cancellationToken)!;
        }

        /// <summary>
        /// 详情（**计数**）：每次调用浏览量 +1。用于公开文章页。
        /// </summary>
        public Task<PostDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
            GetDetailInternalAsync(id, countView: true, cancellationToken);

        /// <summary>
        /// 详情（**不计数**）：管理端/编辑页取数据用，避免后台操作污染浏览量（Q12）。
        /// </summary>
        public Task<PostDetailDto?> GetDetailReadonlyAsync(Guid id, CancellationToken cancellationToken = default) =>
            GetDetailInternalAsync(id, countView: false, cancellationToken);

        private async Task<PostDetailDto?> GetDetailInternalAsync(Guid id, bool countView, CancellationToken cancellationToken)
        {
            var detail = await _cache.GetOrCreateAsync(
                CacheKeys.PostDetail(id),
                ct => _postQuery.GetDetailAsync(id, ct),
                DetailCacheTtl,
                cacheNull: false, // 详情不存在不写哨兵，避免新建后立即读到空
                cancellationToken);

            if (detail is null) return null;

            // 未发布的文章只有作者本人与管理员可读（草稿保护）
            if (!detail.PublishedAt.HasValue && !CanReadUnpublished(detail))
                throw new BusinessException("文章不存在", ErrorCodes.NotFound);

            if (!countView) return detail;

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

            // 未登录不允许创建（无认证时角色为 null）
            if (!_currentUser.IsAuthenticated)
                throw new BusinessException("未登录", ErrorCodes.Unauthorized);

            var authorId = await ResolveAuthorIdAsync(request.AuthorId, cancellationToken);

            if (request.CategoryId.HasValue &&
                await _categories.GetByIdAsync(request.CategoryId.Value, cancellationToken) is null)
            {
                throw new BusinessException("分类不存在", ErrorCodes.NotFound);
            }

            var post = new PostEntity(
                request.Title,
                request.Content,
                request.Summary,
                authorId,
                request.CategoryId,
                request.CoverImage ?? string.Empty,
                createdByUserId: _currentUser.UserId,
                publishNow: request.Publish);

            await ApplyTagsAsync(post, request.TagIds, cancellationToken);
            await ApplyCollectionsAsync(post, request.CollectionIds, cancellationToken);

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

            EnsureCanManage(post);

            // 乐观锁：以客户端版本号为基准，UPDATE ... WHERE "Version" = @expected
            _posts.ApplyOptimisticVersion(post, ValidateVersion(request.Version));

            post.Update(request.Title, request.Content, request.Summary, request.CategoryId, request.CoverImage ?? string.Empty);
            await ApplyTagsAsync(post, request.TagIds, cancellationToken);
            await ApplyCollectionsAsync(post, request.CollectionIds, cancellationToken);

            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidatePostCachesAsync(cancellationToken);

            return await LoadDetailOrThrowAsync(id, cancellationToken);
        }

        public async Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default)
        {
            var post = await _posts.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("文章不存在", ErrorCodes.NotFound);

            EnsureCanManage(post);
            _posts.ApplyOptimisticVersion(post, ValidateVersion(version));

            _posts.Remove(post);
            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidatePostCachesAsync(cancellationToken);
        }

        public async Task<PostDetailDto> PublishAsync(Guid id, int version, bool publish, CancellationToken cancellationToken = default)
        {
            var post = await _posts.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("文章不存在", ErrorCodes.NotFound);

            EnsureCanManage(post);
            _posts.ApplyOptimisticVersion(post, ValidateVersion(version));

            if (publish) post.Publish(); else post.Unpublish();

            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidatePostCachesAsync(cancellationToken);

            return await LoadDetailOrThrowAsync(id, cancellationToken);
        }

        // ---------------------------------------------------------------- 权限

        /// <summary>
        /// 归属校验：管理员可操作全部；作者只能操作**自己创建的**文章。
        ///
        /// 归属判断只认 <c>CreatedByUserId</c>（创建这篇文章的**账号**），不能认 AuthorId。
        /// 原因：`Author` 是内容层的“署名对象”，多个作者账号可能被管理员关联到同一个 Author
        /// （例如同一人开两个账号，或管理员图省事复用了同一个 Author）。
        /// 若用 AuthorId 判断，这些账号之间就可以互相改/删对方的文章，形成越权。
        ///
        /// 必须放在服务层：要先查到实体才知道归属，光靠 [Authorize] 无法覆盖。
        /// </summary>
        private void EnsureCanManage(PostEntity post)
        {
            if (_currentUser.IsAdmin) return;

            if (!_currentUser.IsAuthenticated)
                throw new BusinessException("未登录", ErrorCodes.Unauthorized);

            var isOwner = post.CreatedByUserId.HasValue && post.CreatedByUserId == _currentUser.UserId;

            if (!isOwner)
                throw new BusinessException("无权操作他人的文章", ErrorCodes.Forbidden);
        }

        /// <summary>
        /// 能否读取未发布文章（草稿）：仅管理员与**创建者账号**。
        /// 与 <see cref="EnsureCanManage"/> 同理，只认 CreatedByUserId。
        /// 注意：不可读时对外表现为 404（而不是 403），避免通过状态码探测草稿是否存在。
        /// </summary>
        private bool CanReadUnpublished(PostDetailDto detail) =>
            _currentUser.IsAdmin ||
            (detail.CreatedByUserId.HasValue && detail.CreatedByUserId == _currentUser.UserId);

        private static int ValidateVersion(int version)
        {
            if (version < 1)
                throw new BusinessException("缺少合法的版本号，无法进行并发控制", ErrorCodes.InvalidArgument);
            return version;
        }

        // ---------------------------------------------------------------- 关联同步

        private async Task ApplyTagsAsync(PostEntity post, List<Guid>? tagIds, CancellationToken cancellationToken)
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

        /// <summary>同步文章与专栏的关联（多对多，带专栏内排序）</summary>
        private async Task ApplyCollectionsAsync(PostEntity post, List<Guid>? collectionIds, CancellationToken cancellationToken)
        {
            // null 表示「本次不改动专栏关联」；空数组表示「清空关联」
            if (collectionIds is null) return;

            var ids = collectionIds.Distinct().ToList();
            var collections = ids.Count == 0
                ? new List<CollectionEntity>()
                : await _collections.GetByIdsAsync(ids, cancellationToken);

            if (collections.Count != ids.Count)
                throw new BusinessException("存在不合法的专栏 id", ErrorCodes.InvalidArgument);

            foreach (var link in post.CollectionLinks.Where(l => !ids.Contains(l.CollectionId)).ToList())
                post.CollectionLinks.Remove(link);

            var existing = post.CollectionLinks.Select(l => l.CollectionId).ToHashSet();
            foreach (var collection in collections.Where(c => !existing.Contains(c.Id)))
            {
                post.CollectionLinks.Add(new PostCollection
                {
                    PostId = post.Id,
                    CollectionId = collection.Id,
                    SortOrder = post.CollectionLinks.Count,
                });
            }
        }

        private async Task<PostDetailDto> LoadDetailOrThrowAsync(Guid id, CancellationToken cancellationToken)
        {
            // 写入后立即失效缓存，直接读库保证拿到最新数据
            await _cache.RemoveAsync(CacheKeys.PostDetail(id), cancellationToken);
            return await _postQuery.GetDetailAsync(id, cancellationToken)
                ?? throw new BusinessException("文章不存在", ErrorCodes.NotFound);
        }

        /// <summary>
        /// 解析署名作者：
        ///   1. 管理员可显式指定 AuthorId
        ///   2. 作者身份登录时强制用自己关联的 Author（不能冒名）
        ///   3. 回退到库中首个作者，保持「无账号体系的既有数据」仍可工作
        /// </summary>
        private async Task<Guid?> ResolveAuthorIdAsync(Guid? requested, CancellationToken cancellationToken)
        {
            if (_currentUser.IsAdmin && requested.HasValue)
            {
                if (await _authors.GetByIdAsync(requested.Value, cancellationToken) is null)
                    throw new BusinessException("指定的作者不存在", ErrorCodes.InvalidArgument);
                return requested.Value;
            }

            if (!_currentUser.IsAdmin)
            {
                if (_currentUser.AuthorId.HasValue)
                    return _currentUser.AuthorId;

                // 作者账号未关联 Author：回退首个作者，避免直接失败
            }

            var authors = await _authors.QueryByConditionAsync(a => true, cancellationToken);
            return authors.FirstOrDefault()?.Id;
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
