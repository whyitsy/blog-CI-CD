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

        public async Task<AuthorDto> CreateAsync(CreateAuthorRequest request, CancellationToken cancellationToken = default)
        {
            ValidateNameAndEmail(request.Name, request.Email);

            var author = new AuthorEntity(
                request.Name.Trim(),
                request.Email.Trim(),
                (request.Avatar ?? string.Empty).Trim(),
                (request.Bio ?? string.Empty).Trim());

            await _authors.AddAsync(author, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);

            return ToDto(author);
        }

        public async Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default)
        {
            if (version < 1)
                throw new BusinessException("缺少合法的版本号，无法进行并发控制", ErrorCodes.InvalidArgument);

            var author = await _authors.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("作者不存在", ErrorCodes.NotFound);

            _authors.ApplyOptimisticVersion(author, version);

            // 软删除。其署名文章的 AuthorId 由 EF 的 SetNull 行为置空，
            // 文章本身保留（作者离职不应删掉他的文章）。
            _authors.Remove(author);
            await _uow.SaveChangesAsync(cancellationToken);

            await Task.WhenAll(
                _cache.RemoveByPrefixAsync(CacheKeys.PostsPrefix, cancellationToken),
                _cache.RemoveAsync(CacheKeys.SiteStats, cancellationToken));
        }

        public async Task<AuthorDto> UpdateAsync(Guid id, UpdateAuthorRequest request, CancellationToken cancellationToken = default)
        {
            ValidateNameAndEmail(request.Name, request.Email);

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

        private static void ValidateNameAndEmail(string name, string email)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new BusinessException("作者名称不能为空", ErrorCodes.InvalidArgument);
            if (name.Length > 100)
                throw new BusinessException("作者名称长度不能超过 100", ErrorCodes.InvalidArgument);
            if (string.IsNullOrWhiteSpace(email))
                throw new BusinessException("邮箱不能为空", ErrorCodes.InvalidArgument);
            if (email.Length > 100)
                throw new BusinessException("邮箱长度不能超过 100", ErrorCodes.InvalidArgument);
        }

        private static AuthorDto ToDto(AuthorEntity a) =>
            new(a.Id, a.Name, a.Email, a.Avatar, a.Bio, a.CreatedAt, a.Version);
    }
}
