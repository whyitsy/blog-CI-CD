using Blog.Domain.IRepository;

namespace Blog.Application.Services.Author
{
    public class AuthorService : IAuthorService
    {
        private readonly IAuthorRepository _authors;

        public AuthorService(IAuthorRepository authors)
        {
            _authors = authors;
        }

        public async Task<List<AuthorDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var authors = await _authors.GetAllAsync(cancellationToken);

            return authors
                .Select(a => new AuthorDto(a.Id, a.Name, a.Email, a.Avatar, a.Bio, a.CreatedAt))
                .ToList();
        }
    }
}
