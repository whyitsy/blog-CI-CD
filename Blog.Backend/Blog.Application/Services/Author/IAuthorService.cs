namespace Blog.Application.Services.Author
{
    public interface IAuthorService
    {
        Task<List<AuthorDto>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<AuthorDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>更新作者资料（乐观锁：必须携带当前 Version，冲突返回 4090）</summary>
        Task<AuthorDto> UpdateAsync(Guid id, UpdateAuthorRequest request, CancellationToken cancellationToken = default);
    }
}
