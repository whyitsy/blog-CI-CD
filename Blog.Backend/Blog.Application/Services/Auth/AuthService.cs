using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Domain.Entities;
using Blog.Domain.IRepository;

namespace Blog.Application.Services.Auth
{
    public sealed class AuthService : IAuthService
    {
        private readonly IUserRepository _users;
        private readonly IAuthorRepository _authors;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly ICurrentUser _currentUser;
        private readonly IUnitOfWork _uow;

        public AuthService(
            IUserRepository users,
            IAuthorRepository authors,
            IPasswordHasher passwordHasher,
            ITokenService tokenService,
            ICurrentUser currentUser,
            IUnitOfWork uow)
        {
            _users = users;
            _authors = authors;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _currentUser = currentUser;
            _uow = uow;
        }

        public Task<LoginResponse> AuthorLoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            LoginAsync(request, UserRole.Author, cancellationToken);

        public Task<LoginResponse> AdminLoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            LoginAsync(request, UserRole.Admin, cancellationToken);

        private async Task<LoginResponse> LoginAsync(LoginRequest request, UserRole requiredRole, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                throw new BusinessException("邮箱与密码不能为空", ErrorCodes.InvalidArgument);

            var user = await _users.GetByEmailAsync(request.Email, cancellationToken);

            // 统一失败提示：不区分「账号不存在」「密码错误」「角色不符」，防账号枚举与角色探测。
            // 注意：即使 user 为 null 也走一次哈希校验的等价开销是可选的加固，这里为简洁直接返回。
            if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
                throw new BusinessException("邮箱或密码错误", ErrorCodes.Unauthorized);

            if (!user.IsActive)
                throw new BusinessException("账号已被停用，请联系管理员", ErrorCodes.Unauthorized);

            if (user.Role != requiredRole)
                throw new BusinessException("邮箱或密码错误", ErrorCodes.Unauthorized);

            // 迭代次数提升后，登录成功时静默升级哈希参数
            if (_passwordHasher.NeedsRehash(user.PasswordHash))
                user.ResetPassword(_passwordHasher.Hash(request.Password));

            user.MarkLoggedIn();
            await _uow.SaveChangesAsync(cancellationToken);

            var (token, expiresAt) = _tokenService.Issue(user);
            var dto = await ToCurrentUserDtoAsync(user, cancellationToken);

            return new LoginResponse(token, expiresAt, user.Role.ToString(), dto);
        }

        public async Task<CurrentUserDto> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            var user = await RequireCurrentUserAsync(cancellationToken);
            return await ToCurrentUserDtoAsync(user, cancellationToken);
        }

        public async Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            var user = await RequireCurrentUserAsync(cancellationToken);
            user.LogoutAllDevices();
            await _uow.SaveChangesAsync(cancellationToken);
        }

        private async Task<User> RequireCurrentUserAsync(CancellationToken cancellationToken)
        {
            if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
                throw new BusinessException("未登录", ErrorCodes.Unauthorized);

            return await _users.GetByIdAsync(_currentUser.UserId.Value, cancellationToken)
                   ?? throw new BusinessException("账号不存在", ErrorCodes.Unauthorized);
        }

        private async Task<CurrentUserDto> ToCurrentUserDtoAsync(User user, CancellationToken cancellationToken)
        {
            string? authorName = null;
            if (user.AuthorId.HasValue)
            {
                var author = await _authors.GetByIdAsync(user.AuthorId.Value, cancellationToken);
                authorName = author?.Name;
            }

            return new CurrentUserDto(
                user.Id, user.Email, user.Role.ToString(), user.IsActive,
                user.AuthorId, authorName, user.LastLoginAt);
        }
    }
}
