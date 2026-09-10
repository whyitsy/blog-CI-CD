namespace Blog.Application.Services.Auth
{
    /// <summary>登录与登出。作者与管理员走不同端点，但共用同一套 JWT 机制</summary>
    public interface IAuthService
    {
        /// <summary>
        /// 作者登录。角色必须是 Author（Admin 走 <see cref="AdminLoginAsync"/>）。
        /// 凭据错误统一返回「邮箱或密码错误」，不区分原因，防账号枚举。
        /// </summary>
        Task<LoginResponse> AuthorLoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

        /// <summary>管理员登录。角色必须是 Admin</summary>
        Task<LoginResponse> AdminLoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

        /// <summary>当前登录用户信息</summary>
        Task<CurrentUserDto> GetCurrentAsync(CancellationToken cancellationToken = default);

        /// <summary>注销：提升 TokenVersion，使该账号所有旧 token 立即失效</summary>
        Task LogoutAsync(CancellationToken cancellationToken = default);
    }
}
