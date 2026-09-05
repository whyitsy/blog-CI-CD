using System;
using System.Collections.Generic;
using System.Text;

namespace Blog.Application.Services.Tag
{
    public record TagDto(
        Guid Id,
        string Name,
        DateTimeOffset CreatedAt
    );

    public record CreateTagRequest(string Name);
    public record UpdateTagRequest(Guid Id, string Name);
    public record DeleteTagRequest(Guid Id);
}
