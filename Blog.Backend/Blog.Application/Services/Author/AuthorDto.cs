namespace Blog.Application.Services.Author
{
    public record AuthorDto(
        Guid Id,
        string Name,
        string Email,
        string Avatar,
        string Bio,
        DateTimeOffset CreatedAt
    );

    public record CreateAuthorRequest(string Name, string Email, string? Bio, string? Avatar);

    public record UpdateAuthorRequest(string Name, string Email, string Bio, string Avatar, int Version);
}
