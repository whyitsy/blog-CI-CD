namespace Blog.Application.Services.Collection
{
    /// <summary>
    /// 专栏：把多篇文章组织成一个系列。
    ///
    /// 与分类的区别：分类是「广度」上的单值归类（一篇文章一个分类），
    /// 专栏是「深度」上的系列组织，且**一篇文章可属于多个专栏**（T2 决策，多对多）。
    /// </summary>
    public interface ICollectionService
    {
        /// <summary>专栏列表。includeUnpublished=false 时只返回已发布（前台用）</summary>
        Task<List<CollectionDto>> GetAllAsync(bool includeUnpublished = false, CancellationToken cancellationToken = default);

        /// <summary>按 slug 取专栏详情（含其文章列表）</summary>
        Task<CollectionDetailDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

        /// <summary>按 id 取专栏详情（管理端用，含未发布文章）</summary>
        Task<CollectionDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<CollectionDto> CreateAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default);

        Task<CollectionDto> UpdateAsync(Guid id, UpdateCollectionRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default);

        /// <summary>整体设置专栏内的文章与顺序（多对多关系的权威入口）</summary>
        Task<CollectionDetailDto> SetPostsAsync(Guid id, SetCollectionPostsRequest request, CancellationToken cancellationToken = default);
    }
}
