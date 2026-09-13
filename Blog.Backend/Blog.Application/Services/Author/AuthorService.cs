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
            ValidateProfile(request.Name, request.Email, request.Bio);

            var author = new AuthorEntity(
                request.Name.Trim(),
                request.Email.Trim(),
                // 头像只接受本站上传的地址：隐藏前端输入框挡不住 curl，必须在服务端拦
                MediaPath.Validate(request.Avatar, "头像"),
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
            ValidateProfile(request.Name, request.Email, request.Bio);

            if (request.Version < 1)
                throw new BusinessException("缺少合法的版本号，无法进行并发控制", ErrorCodes.InvalidArgument);

            var author = await _authors.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("作者不存在", ErrorCodes.NotFound);

            // 乐观锁：UPDATE ... WHERE "Version" = @expected
            _authors.ApplyOptimisticVersion(author, request.Version);

            author.Update(
                request.Name.Trim(),
                request.Email.Trim(),
                MediaPath.Validate(request.Avatar, "头像"),
                request.Bio ?? string.Empty);
            await _uow.SaveChangesAsync(cancellationToken);

            // 文章详情（缓存中含作者名/头像）与站点统计需要重新生成
            await Task.WhenAll(
                _cache.RemoveByPrefixAsync(CacheKeys.PostsPrefix, cancellationToken),
                _cache.RemoveAsync(CacheKeys.SiteStats, cancellationToken));

            return ToDto(author);
        }

        /// <summary>
        /// 作者资料入参校验。
        ///
        /// <para><b>个人简介（Bio，varchar 500）此前没有应用层校验</b>：前端虽然有
        /// <c>maxlength</c>，但那只挡得住界面 —— <c>curl</c> 直改依旧会撞到数据库约束，
        /// 拿到的是「服务器内部错误」而不是「简介太长了」。</para>
        /// </summary>
        private static void ValidateProfile(string name, string email, string? bio)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new BusinessException("作者名称不能为空", ErrorCodes.InvalidArgument);
            FieldLimits.EnsureLength(name, FieldLimits.AuthorName, "作者名称");

            if (string.IsNullOrWhiteSpace(email))
                throw new BusinessException("邮箱不能为空", ErrorCodes.InvalidArgument);
            FieldLimits.EnsureLength(email, FieldLimits.AuthorEmail, "邮箱");

            FieldLimits.EnsureLength(bio, FieldLimits.AuthorBio, "个人简介");
        }

        private static AuthorDto ToDto(AuthorEntity a) =>
            new(a.Id, a.Name, a.Email, a.Avatar, a.Bio, a.CreatedAt, a.Version);
    }
}
