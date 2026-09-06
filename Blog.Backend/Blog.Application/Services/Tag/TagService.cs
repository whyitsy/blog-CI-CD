using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using TagEntity = Blog.Domain.Entities.Tag;

namespace Blog.Application.Services.Tag
{
    public class TagService : ITagService
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

        private readonly ITagRepository _tags;
        private readonly ITagQueryRepository _tagQuery;
        private readonly IUnitOfWork _uow;
        private readonly ICacheService _cache;

        public TagService(
            ITagRepository tags,
            ITagQueryRepository tagQuery,
            IUnitOfWork uow,
            ICacheService cache)
        {
            _tags = tags;
            _tagQuery = tagQuery;
            _uow = uow;
            _cache = cache;
        }

        public async Task<List<TagDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var items = await _cache.GetOrCreateAsync(
                CacheKeys.Tags,
                ct => _tagQuery.GetAllWithPostCountAsync(ct),
                CacheTtl,
                cacheNull: true,
                cancellationToken);

            return items ?? [];
        }

        public async Task<TagDto> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken = default)
        {
            ValidateName(request.Name);

            if (await _tags.ExistsByNameAsync(request.Name, cancellationToken: cancellationToken))
                throw new BusinessException($"标签「{request.Name}」已存在", ErrorCodes.BusinessRule);

            var tag = new TagEntity(request.Name);
            await _tags.AddAsync(tag, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidateAsync(cancellationToken);

            return new TagDto(tag.Id, tag.Name, 0, tag.Version);
        }

        public async Task<TagDto> UpdateAsync(Guid id, UpdateTagRequest request, CancellationToken cancellationToken = default)
        {
            ValidateName(request.Name);

            var tag = await _tags.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("标签不存在", ErrorCodes.NotFound);

            _tags.ApplyOptimisticVersion(tag, request.Version);

            if (!string.Equals(tag.Name, request.Name, StringComparison.Ordinal) &&
                await _tags.ExistsByNameAsync(request.Name, cancellationToken: cancellationToken))
            {
                throw new BusinessException($"标签「{request.Name}」已存在", ErrorCodes.BusinessRule);
            }

            tag.Update(request.Name);
            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidateAsync(cancellationToken);

            return new TagDto(tag.Id, tag.Name, await _tags.CountPostsAsync(tag.Id, cancellationToken), tag.Version);
        }

        public async Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default)
        {
            var tag = await _tags.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("标签不存在", ErrorCodes.NotFound);

            _tags.ApplyOptimisticVersion(tag, version);

            _tags.Remove(tag);
            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidateAsync(cancellationToken);
        }

        private Task InvalidateAsync(CancellationToken cancellationToken) =>
            Task.WhenAll(
                _cache.RemoveAsync(CacheKeys.Tags, cancellationToken),
                _cache.RemoveAsync(CacheKeys.SiteStats, cancellationToken),
                _cache.RemoveByPrefixAsync(CacheKeys.PostsPrefix, cancellationToken));

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new BusinessException("名称不能为空", ErrorCodes.InvalidArgument);
            if (name.Length > 50)
                throw new BusinessException("名称长度不能超过 50", ErrorCodes.InvalidArgument);
        }
    }
}
