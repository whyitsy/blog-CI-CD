using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Domain.IRepository;
using AuthorEntity = Blog.Domain.Entities.Author;

namespace Blog.Application.Services.Author
{
    public class AuthorService : IAuthorService
    {
        private readonly IAuthorRepository _authors;
        private readonly IUnitOfWork _uow;
        private readonly ICacheService _cache;

        public AuthorService(IAuthorRepository authors, IUnitOfWork uow, ICacheService cache)
        {
            _authors = authors;
            _uow = uow;
            _cache = cache;
        }

        public async Task<List<AuthorDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var authors = await _authors.GetAllAsync(cancellationToken);

            return authors.Select(ToDto).ToList();
        }

        public async Task<AuthorDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var author = await _authors.GetByIdAsync(id, cancellationToken);
            return author is null ? null : ToDto(author);
        }

        public async Task<AuthorDto> UpdateAsync(Guid id, UpdateAuthorRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new BusinessException("作者名称不能为空", ErrorCodes.InvalidArgument);

            if (string.IsNullOrWhiteSpace(request.Email))
                throw new BusinessException("邮箱不能为空", ErrorCodes.InvalidArgument);

            if (request.Version < 1)
                throw new BusinessException("缺少合法的版本号，无法进行并发控制", ErrorCodes.InvalidArgument);

            var author = await _authors.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("作者不存在", ErrorCodes.NotFound);

            // 乐观锁：UPDATE ... WHERE "Version" = @expected
            _authors.ApplyOptimisticVersion(author, request.Version);

            author.Update(request.Name.Trim(), request.Email.Trim(), request.Avatar ?? string.Empty, request.Bio ?? string.Empty);
            await _uow.SaveChangesAsync(cancellationToken);

            // 文章详情（缓存中含作者名/头像）与站点统计需要重新生成
            await Task.WhenAll(
                _cache.RemoveByPrefixAsync(CacheKeys.PostsPrefix, cancellationToken),
                _cache.RemoveAsync(CacheKeys.SiteStats, cancellationToken));

            return ToDto(author);
        }

        private static AuthorDto ToDto(AuthorEntity a) =>
            new(a.Id, a.Name, a.Email, a.Avatar, a.Bio, a.CreatedAt, a.Version);
    }
}
