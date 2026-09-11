namespace Blog.Application.Services.Author
{
    public interface IAuthorService
    {
        Task<List<AuthorDto>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<AuthorDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 创建作者。
        /// Author 是**内容层**的署名对象（与 Post/Tag/Category 同层），
        /// 不是登录账号 —— 账号走 /api/users（见 docs/10-决策记录.md §2.1 / T1）。
        /// </summary>
        Task<AuthorDto> CreateAsync(CreateAuthorRequest request, CancellationToken cancellationToken = default);

        /// <summary>更新作者资料（乐观锁：必须携带当前 Version，冲突返回 4090）</summary>
        Task<AuthorDto> UpdateAsync(Guid id, UpdateAuthorRequest request, CancellationToken cancellationToken = default);

        /// <summary>软删除作者。其署名文章的 AuthorId 会被置空（SetNull），文章本身不受影响</summary>
        Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default);
    }
}
