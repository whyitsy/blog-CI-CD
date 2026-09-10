namespace Blog.Application.Services.Author
{
    /// <summary>Version 为乐观锁版本号，前端更新时必须原样回传</summary>
    public record AuthorDto(
        Guid Id,
        string Name,
        string Email,
        string Avatar,
        string Bio,
        DateTimeOffset CreatedAt,
        int Version
    );

    public record CreateAuthorRequest(string Name, string Email, string? Bio, string? Avatar);

    public record UpdateAuthorRequest(string Name, string Email, string Bio, string Avatar, int Version);
}
