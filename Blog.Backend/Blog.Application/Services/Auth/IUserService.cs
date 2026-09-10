namespace Blog.Application.Services.Auth
{
    /// <summary>
    /// 账号管理。**仅管理员可用**，且作者账号的唯一创建入口就在这里
    /// （T1：作者不开放自助注册，由管理员在后台创建）。
    /// </summary>
    public interface IUserService
    {
        Task<List<UserDto>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<UserDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>创建账号。作者账号通过指定 role=Author 与关联的 authorId 产生</summary>
        Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

        /// <summary>更新角色 / 关联作者 / 启用状态（乐观锁）</summary>
        Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);

        /// <summary>重置密码。会提升 TokenVersion，使该账号旧 token 立即失效</summary>
        Task<UserDto> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken = default);

        /// <summary>禁用账号（软性操作，不物理删除，以保留审计线索）</summary>
        Task DisableAsync(Guid id, int version, CancellationToken cancellationToken = default);
    }
}
