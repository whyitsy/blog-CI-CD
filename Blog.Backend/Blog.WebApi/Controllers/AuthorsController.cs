using Blog.Application.Common;
using Blog.Application.Services.Author;
using Microsoft.AspNetCore.Mvc;

namespace Blog.WebApi.Controllers
{
    /// <summary>作者信息</summary>
    [ApiController]
    [Route("api/authors")]
    public class AuthorsController : ControllerBase
    {
        private readonly IAuthorService _authors;

        public AuthorsController(IAuthorService authors)
        {
            _authors = authors;
        }

        [HttpGet]
        public async Task<ApiResponse<List<AuthorDto>>> GetAll(CancellationToken cancellationToken)
        {
            var items = await _authors.GetAllAsync(cancellationToken);
            return ApiResponse<List<AuthorDto>>.Ok(items);
        }
    }
}
