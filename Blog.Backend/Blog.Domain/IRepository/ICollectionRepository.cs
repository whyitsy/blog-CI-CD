using Blog.Domain.Entities;

namespace Blog.Domain.IRepository
{
    public interface ICollectionRepository : IBaseRepository<Collection>
    {
        /// <summary>按 id 集合批量查询（用于校验文章提交的专栏 id）</summary>
        Task<List<Collection>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

        /// <summary>
        /// 取专栏并**显式加载**其 PostLinks（多对多连接行）。
        ///
        /// 为什么必须单独提供：BaseRepository.GetByIdAsync 不加载导航集合，
        /// 若直接改 PostLinks，EF 会认为集合是空的，从而对已存在的连接行执行 INSERT，
        /// 触发 PK_PostCollections 唯一约束冲突（实测踩到过）。
        /// 需要增删/重排专栏内文章时，必须用本方法。
        /// </summary>
        Task<Collection?> GetWithPostsAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>slug 是否已被占用。excludeId 用于更新时排除自己</summary>
        Task<bool> ExistsBySlugAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default);
    }
}
