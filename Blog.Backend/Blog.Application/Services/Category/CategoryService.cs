using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using CategoryEntity = Blog.Domain.Entities.Category;

namespace Blog.Application.Services.Category
{
    public class CategoryService : ICategoryService
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

        private readonly ICategoryRepository _categories;
        private readonly ICategoryQueryRepository _categoryQuery;
        private readonly IUnitOfWork _uow;
        private readonly ICacheService _cache;

        public CategoryService(
            ICategoryRepository categories,
            ICategoryQueryRepository categoryQuery,
            IUnitOfWork uow,
            ICacheService cache)
        {
            _categories = categories;
            _categoryQuery = categoryQuery;
            _uow = uow;
            _cache = cache;
        }

        public async Task<List<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var items = await _cache.GetOrCreateAsync(
                CacheKeys.Categories,
                ct => _categoryQuery.GetAllWithPostCountAsync(ct),
                CacheTtl,
                cacheNull: true,
                cancellationToken);

            return items ?? [];
        }

        public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
        {
            ValidateName(request.Name);

            if (await _categories.ExistsByNameAsync(request.Name, cancellationToken: cancellationToken))
                throw new BusinessException($"分类「{request.Name}」已存在", ErrorCodes.BusinessRule);

            var category = new CategoryEntity(request.Name);
            await _categories.AddAsync(category, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidateAsync(cancellationToken);

            return new CategoryDto(category.Id, category.Name, 0, category.Version);
        }

        public async Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken = default)
        {
            ValidateName(request.Name);

            var category = await _categories.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("分类不存在", ErrorCodes.NotFound);

            // 乐观锁：UPDATE ... WHERE "Version" = @expected
            _categories.ApplyOptimisticVersion(category, ValidateVersion(request.Version));

            if (!string.Equals(category.Name, request.Name, StringComparison.Ordinal) &&
                await _categories.ExistsByNameAsync(request.Name, cancellationToken: cancellationToken))
            {
                throw new BusinessException($"分类「{request.Name}」已存在", ErrorCodes.BusinessRule);
            }

            category.Update(request.Name);
            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidateAsync(cancellationToken);

            return new CategoryDto(category.Id, category.Name,
                await CountPosts(category.Id, cancellationToken), category.Version);
        }

        public async Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default)
        {
            var category = await _categories.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("分类不存在", ErrorCodes.NotFound);

            _categories.ApplyOptimisticVersion(category, ValidateVersion(version));

            // 软删除：其下文章的 CategoryId 由 SetNull 行为置空
            _categories.Remove(category);
            await _uow.SaveChangesAsync(cancellationToken);
            await InvalidateAsync(cancellationToken);
        }

        private static int ValidateVersion(int version)
        {
            if (version < 1)
                throw new BusinessException("缺少合法的版本号，无法进行并发控制", ErrorCodes.InvalidArgument);
            return version;
        }

        private Task<int> CountPosts(Guid categoryId, CancellationToken cancellationToken) =>
            _categories.CountPostsAsync(categoryId, cancellationToken);

        private Task InvalidateAsync(CancellationToken cancellationToken) =>
            Task.WhenAll(
                _cache.RemoveAsync(CacheKeys.Categories, cancellationToken),
                _cache.RemoveAsync(CacheKeys.SiteStats, cancellationToken),
                _cache.RemoveByPrefixAsync(CacheKeys.PostsPrefix, cancellationToken));

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new BusinessException("名称不能为空", ErrorCodes.InvalidArgument);
            if (name.Length > 100)
                throw new BusinessException("名称长度不能超过 100", ErrorCodes.InvalidArgument);
        }
    }
}
