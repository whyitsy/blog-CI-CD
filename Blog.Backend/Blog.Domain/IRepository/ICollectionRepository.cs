using Blog.Domain.Entities;

namespace Blog.Domain.IRepository
{
    public interface ICollectionRepository : IBaseRepository<Collection>
    {
        /// <summary>按 id 集合批量查询（用于校验文章提交的专栏 id）</summary>
        Task<List<Collection>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

        /// <summary>slug 是否已被占用。excludeId 用于更新时排除自己</summary>
        Task<bool> ExistsBySlugAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default);
    }
}
