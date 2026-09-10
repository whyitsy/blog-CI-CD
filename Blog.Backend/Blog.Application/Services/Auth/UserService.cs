using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Domain.Entities;
using Blog.Domain.IRepository;

namespace Blog.Application.Services.Auth
{
    public sealed class UserService : IUserService
    {
        private const int MinPasswordLength = 8;

        private readonly IUserRepository _users;
        private readonly IAuthorRepository _authors;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUnitOfWork _uow;

        public UserService(
            IUserRepository users,
            IAuthorRepository authors,
            IPasswordHasher passwordHasher,
            IUnitOfWork uow)
        {
            _users = users;
            _authors = authors;
            _passwordHasher = passwordHasher;
            _uow = uow;
        }

        public async Task<List<UserDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var users = (await _users.GetAllAsync(cancellationToken))
                .OrderBy(u => u.CreatedAt)
                .ToList();

            return await ToDtosAsync(users, cancellationToken);
        }

        public async Task<UserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var user = await _users.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("账号不存在", ErrorCodes.NotFound);

            return (await ToDtosAsync([user], cancellationToken))[0];
        }

        public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
        {
            ValidateEmail(request.Email);
            ValidatePassword(request.Password);

            var role = ParseRole(request.Role);

            if (await _users.ExistsByEmailAsync(request.Email, cancellationToken: cancellationToken))
                throw new BusinessException($"邮箱「{request.Email}」已被占用", ErrorCodes.DuplicateResource);

            if (request.AuthorId.HasValue)
                await RequireAuthorAsync(request.AuthorId.Value, cancellationToken);

            var user = new User(
                NormalizeEmail(request.Email),
                _passwordHasher.Hash(request.Password),
                role,
                request.AuthorId);

            await _users.AddAsync(user, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);

            return (await ToDtosAsync([user], cancellationToken))[0];
        }

        public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
        {
            var user = await _users.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("账号不存在", ErrorCodes.NotFound);

            var role = ParseRole(request.Role);

            if (request.AuthorId.HasValue)
                await RequireAuthorAsync(request.AuthorId.Value, cancellationToken);

            _users.ApplyOptimisticVersion(user, ValidateVersion(request.Version));

            // 通过领域方法变更：SetActive(false) 会顺带提升 TokenVersion（立即踢下线）
            user.UpdateProfile(role, request.AuthorId, request.IsActive);
            user.SetActive(request.IsActive);

            await _uow.SaveChangesAsync(cancellationToken);

            return (await ToDtosAsync([user], cancellationToken))[0];
        }

        public async Task<UserDto> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken = default)
        {
            ValidatePassword(request.NewPassword);

            var user = await _users.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("账号不存在", ErrorCodes.NotFound);

            _users.ApplyOptimisticVersion(user, ValidateVersion(request.Version));

            // ResetPassword 内部会提升 TokenVersion
            user.ResetPassword(_passwordHasher.Hash(request.NewPassword));

            await _uow.SaveChangesAsync(cancellationToken);

            return (await ToDtosAsync([user], cancellationToken))[0];
        }

        public async Task DisableAsync(Guid id, int version, CancellationToken cancellationToken = default)
        {
            var user = await _users.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("账号不存在", ErrorCodes.NotFound);

            _users.ApplyOptimisticVersion(user, ValidateVersion(version));

            user.SetActive(false);
            await _uow.SaveChangesAsync(cancellationToken);
        }

        // ---------------------------------------------------------------- helpers

        private async Task<List<UserDto>> ToDtosAsync(List<User> users, CancellationToken cancellationToken)
        {
            // 一次性把作者名查出来，避免逐个往返
            var authorIds = users.Where(u => u.AuthorId.HasValue).Select(u => u.AuthorId!.Value).Distinct().ToList();
            var authors = authorIds.Count == 0
                ? new Dictionary<Guid, string>()
                : (await _authors.QueryByConditionAsync(a => authorIds.Contains(a.Id), cancellationToken))
                    .ToDictionary(a => a.Id, a => a.Name);

            return users.Select(u => new UserDto(
                u.Id,
                u.Email,
                u.Role.ToString(),
                u.IsActive,
                u.AuthorId,
                u.AuthorId.HasValue && authors.TryGetValue(u.AuthorId.Value, out var name) ? name : null,
                u.LastLoginAt,
                u.CreatedAt,
                u.Version)).ToList();
        }

        private async Task RequireAuthorAsync(Guid authorId, CancellationToken cancellationToken)
        {
            if (await _authors.GetByIdAsync(authorId, cancellationToken) is null)
                throw new BusinessException("关联的作者不存在", ErrorCodes.InvalidArgument);
        }

        private static UserRole ParseRole(string? role)
        {
            if (!Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsed))
                throw new BusinessException("角色只能是 Admin 或 Author", ErrorCodes.InvalidArgument);
            return parsed;
        }

        private static int ValidateVersion(int version)
        {
            if (version < 1)
                throw new BusinessException("缺少合法的版本号，无法进行并发控制", ErrorCodes.InvalidArgument);
            return version;
        }

        private static void ValidateEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new BusinessException("邮箱不能为空", ErrorCodes.InvalidArgument);
            if (email.Length > 100)
                throw new BusinessException("邮箱长度不能超过 100", ErrorCodes.InvalidArgument);
            // 仅做基本形状校验，不追求完整 RFC 5322
            if (!email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
                throw new BusinessException("邮箱格式不正确", ErrorCodes.InvalidArgument);
        }

        private static void ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new BusinessException("密码不能为空", ErrorCodes.InvalidArgument);
            if (password.Length < MinPasswordLength)
                throw new BusinessException($"密码长度不能少于 {MinPasswordLength} 位", ErrorCodes.InvalidArgument);
            if (password.Length > 128)
                throw new BusinessException("密码长度不能超过 128 位", ErrorCodes.InvalidArgument);
        }

        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    }
}
