using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using CollectionEntity = Blog.Domain.Entities.Collection;

namespace Blog.Application.Services.Collection
{
    public sealed class CollectionService : ICollectionService
    {
        private readonly ICollectionRepository _collections;
        private readonly ICollectionQueryRepository _query;
        private readonly IPostRepository _posts;
        private readonly IUnitOfWork _uow;
        private readonly ICacheService _cache;

        public CollectionService(
            ICollectionRepository collections,
            ICollectionQueryRepository query,
            IPostRepository posts,
            IUnitOfWork uow,
            ICacheService cache)
        {
            _collections = collections;
            _query = query;
            _posts = posts;
            _uow = uow;
            _cache = cache;
        }

        public async Task<List<CollectionDto>> GetAllAsync(bool includeUnpublished = false, CancellationToken cancellationToken = default)
        {
            var items = await _cache.GetOrCreateAsync(
                includeUnpublished ? CacheKeys.CollectionsAll : CacheKeys.Collections,
                ct => _query.GetAllAsync(includeUnpublished, ct),
                TimeSpan.FromMinutes(30),
                cacheNull: true,
                cancellationToken);

            return items ?? [];
        }

        public Task<CollectionDetailDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(slug)) return Task.FromResult<CollectionDetailDto?>(null);
            return _query.GetBySlugAsync(slug, cancellationToken);
        }

        public Task<CollectionDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            _query.GetByIdAsync(id, cancellationToken);

        public async Task<CollectionDto> CreateAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default)
        {
            Validate(request.Title, request.Slug, request.Description);

            var slug = NormalizeSlug(request.Slug);
            if (await _collections.ExistsBySlugAsync(slug, cancellationToken: cancellationToken))
                throw new BusinessException($"专栏标识「{slug}」已被占用", ErrorCodes.DuplicateResource);

            var collection = new CollectionEntity(
                request.Title.Trim(),
                slug,
                (request.Description ?? string.Empty).Trim(),
                (request.CoverImage ?? string.Empty).Trim(),
                request.SortOrder,
                request.IsPublished);

            await _collections.AddAsync(collection, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidateAsync(cancellationToken);

            return ToDto(collection, 0);
        }

        public async Task<CollectionDto> UpdateAsync(Guid id, UpdateCollectionRequest request, CancellationToken cancellationToken = default)
        {
            Validate(request.Title, request.Slug, request.Description);

            var collection = await _collections.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("专栏不存在", ErrorCodes.NotFound);

            var slug = NormalizeSlug(request.Slug);
            if (!string.Equals(collection.Slug, slug, StringComparison.Ordinal) &&
                await _collections.ExistsBySlugAsync(slug, id, cancellationToken))
            {
                throw new BusinessException($"专栏标识「{slug}」已被占用", ErrorCodes.DuplicateResource);
            }

            _collections.ApplyOptimisticVersion(collection, ValidateVersion(request.Version));

            collection.Update(
                request.Title.Trim(),
                slug,
                (request.Description ?? string.Empty).Trim(),
                (request.CoverImage ?? string.Empty).Trim(),
                request.SortOrder,
                request.IsPublished);

            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidateAsync(cancellationToken);

            var detail = await _query.GetByIdAsync(collection.Id, cancellationToken);
            return ToDto(collection, detail?.PostCount ?? 0);
        }

        public async Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default)
        {
            var collection = await _collections.GetWithPostsAsync(id, cancellationToken)
                ?? throw new BusinessException("专栏不存在", ErrorCodes.NotFound);

            _collections.ApplyOptimisticVersion(collection, ValidateVersion(version));

            // 软删除专栏。PostCollection 是普通连接表，不随软删除自动清理，
            // 因此这里显式清掉关联，避免「专栏已删但文章仍挂在上面」的脏数据。
            // 注意用 GetWithPostsAsync 取实体，否则 PostLinks 为空、清理无效。
            collection.PostLinks.Clear();

            _collections.Remove(collection);
            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidateAsync(cancellationToken);
        }

        public async Task<CollectionDetailDto> SetPostsAsync(Guid id, SetCollectionPostsRequest request, CancellationToken cancellationToken = default)
        {
            // 必须用 GetWithPostsAsync：否则 PostLinks 为空，改集合会被 EF 当成 INSERT 连接行
            var collection = await _collections.GetWithPostsAsync(id, cancellationToken)
                ?? throw new BusinessException("专栏不存在", ErrorCodes.NotFound);

            _collections.ApplyOptimisticVersion(collection, ValidateVersion(request.Version));

            var ids = (request.PostIds ?? []).Distinct().ToList();

            // 校验文章都存在，否则给出明确错误（而不是静默跳过）
            if (ids.Count > 0)
            {
                var existing = await _posts.QueryByConditionAsync(p => ids.Contains(p.Id), cancellationToken);
                var existingIds = existing.Select(p => p.Id).ToHashSet();
                var missing = ids.Where(pid => !existingIds.Contains(pid)).ToList();
                if (missing.Count > 0)
                    throw new BusinessException($"存在不合法的文章 id：{string.Join(", ", missing)}", ErrorCodes.InvalidArgument);
            }

            // 整体覆盖：先移除不在新集合里的，再补充新增的，最后按请求顺序重排 SortOrder
            foreach (var link in collection.PostLinks.Where(l => !ids.Contains(l.PostId)).ToList())
                collection.PostLinks.Remove(link);

            var current = collection.PostLinks.ToDictionary(l => l.PostId);
            for (var i = 0; i < ids.Count; i++)
            {
                var postId = ids[i];
                if (current.TryGetValue(postId, out var link))
                {
                    link.SortOrder = i;
                }
                else
                {
                    collection.PostLinks.Add(new PostCollection { PostId = postId, CollectionId = collection.Id, SortOrder = i });
                }
            }

            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidateAsync(cancellationToken);

            return await _query.GetByIdAsync(collection.Id, cancellationToken)
                ?? throw new BusinessException("专栏不存在", ErrorCodes.NotFound);
        }

        // ---------------------------------------------------------------- helpers

        private Task InvalidateAsync(CancellationToken cancellationToken) =>
            Task.WhenAll(
                _cache.RemoveAsync(CacheKeys.Collections, cancellationToken),
                _cache.RemoveAsync(CacheKeys.CollectionsAll, cancellationToken),
                _cache.RemoveByPrefixAsync(CacheKeys.PostsPrefix, cancellationToken));

        private static CollectionDto ToDto(CollectionEntity c, int postCount) =>
            new(c.Id, c.Title, c.Slug, c.Description, c.CoverImage, c.SortOrder, c.IsPublished, postCount, c.Version);

        private static int ValidateVersion(int version)
        {
            if (version < 1)
                throw new BusinessException("缺少合法的版本号，无法进行并发控制", ErrorCodes.InvalidArgument);
            return version;
        }

        /// <summary>slug 统一小写，保证 URL 稳定且唯一索引生效</summary>
        private static string NormalizeSlug(string slug) => slug.Trim().ToLowerInvariant();

        /// <summary>
        /// 专栏入参校验。
        ///
        /// <para>简介（Description，varchar 500）此前没有应用层校验 ——
        /// 前端有 <c>maxlength</c>，但那只挡得住界面，<c>curl</c> 直改仍会撞到数据库约束。</para>
        /// </summary>
        private static void Validate(string title, string slug, string? description)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new BusinessException("专栏标题不能为空", ErrorCodes.InvalidArgument);
            FieldLimits.EnsureLength(title, FieldLimits.CollectionTitle, "专栏标题");

            if (string.IsNullOrWhiteSpace(slug))
                throw new BusinessException("专栏标识（slug）不能为空", ErrorCodes.InvalidArgument);
            FieldLimits.EnsureLength(slug, FieldLimits.CollectionSlug, "专栏标识");

            // slug 用于 URL，限制为安全字符集，避免出现需要转义或歧义的路径
            if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9]+(?:-[a-z0-9]+)*$"))
                throw new BusinessException("专栏标识只能包含小写字母、数字与中划线（如 my-series）", ErrorCodes.InvalidArgument);

            FieldLimits.EnsureLength(description, FieldLimits.CollectionDescription, "专栏简介");
        }
    }
}
