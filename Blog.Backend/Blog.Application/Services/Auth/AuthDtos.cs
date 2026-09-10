namespace Blog.Application.Services.Auth
{
    /// <summary>登录请求（作者与管理员共用结构，端点不同）</summary>
    public record LoginRequest(string Email, string Password);

    /// <summary>登录成功响应。本项目只发 Access Token，无 Refresh Token（T7）</summary>
    public record LoginResponse(
        string Token,
        DateTimeOffset ExpiresAt,
        string Role,
        CurrentUserDto User);

    /// <summary>当前登录用户信息。**绝不包含 PasswordHash**</summary>
    public record CurrentUserDto(
        Guid Id,
        string Email,
        string Role,
        bool IsActive,
        Guid? AuthorId,
        string? AuthorName,
        DateTimeOffset? LastLoginAt);

    /// <summary>创建账号（仅管理员）。作者账号由此产生（T1：不开放自助注册）</summary>
    public record CreateUserRequest(
        string Email,
        string Password,
        string Role,
        Guid? AuthorId);

    /// <summary>更新账号（仅管理员）：角色、关联作者、启用状态</summary>
    public record UpdateUserRequest(
        string Role,
        Guid? AuthorId,
        bool IsActive,
        int Version);

    /// <summary>重置账号密码（仅管理员）</summary>
    public record ResetPasswordRequest(string NewPassword, int Version);

    /// <summary>账号列表项。不含凭据字段</summary>
    public record UserDto(
        Guid Id,
        string Email,
        string Role,
        bool IsActive,
        Guid? AuthorId,
        string? AuthorName,
        DateTimeOffset? LastLoginAt,
        DateTimeOffset CreatedAt,
        int Version);
}
