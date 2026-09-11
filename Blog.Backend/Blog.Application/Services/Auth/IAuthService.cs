namespace Blog.Application.Services.Auth
{
    /// <summary>登录与登出。管理员与作者共用同一套 JWT 机制与同一个登录入口</summary>
    public interface IAuthService
    {
        /// <summary>
        /// 登录。不限定角色：管理员与作者共用此入口，调用方按返回的 Role 决定去向。
        /// 凭据错误统一返回「邮箱或密码错误」，不区分原因，防账号枚举。
        /// 真正区分权限的是授权策略（AdminOnly / ContentWriter），不是登录入口。
        /// </summary>
        Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

        /// <summary>当前登录用户信息</summary>
        Task<CurrentUserDto> GetCurrentAsync(CancellationToken cancellationToken = default);

        /// <summary>注销：提升 TokenVersion，使该账号所有旧 token 立即失效</summary>
        Task LogoutAsync(CancellationToken cancellationToken = default);
    }
}
