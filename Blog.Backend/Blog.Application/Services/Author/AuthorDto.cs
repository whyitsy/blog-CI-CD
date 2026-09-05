using System;
using System.Collections.Generic;
using System.Text;

namespace Blog.Application.Services.Author
{
    public record AuthorDto(
        Guid Id,
        string Name,
        string Email,
        string Avatar,
        string Bio,
        DateTimeOffset PublishedAt  
    );

    public record CreateAuthorRequest(string Name, string Email, string? Bio, string? Avatar);
    public record UpdateAuthorRequest(string Name, string Email, string Bio, string Avatar);
    public record DeleteAuthorRequest(Guid Id); 
}
