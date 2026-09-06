namespace Blog.Application.Services.Author
{
    public interface IAuthorService
    {
        Task<List<AuthorDto>> GetAllAsync(CancellationToken cancellationToken = default);
    }
}
