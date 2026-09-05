using System;
using System.Collections.Generic;
using System.Text;

namespace Blog.Application.Services.Catogory
{
    public record CategotyDto(
        Guid Id,
        string Name,
        DateTimeOffset CreatedAt
    );

    public record CreateCategotyRequest(string Name);
    public record UpdateCategotyRequest(Guid Id, string Name);
    public record DeleteCategotyRequest(Guid Id);

}
