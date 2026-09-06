using Blog.Domain.Entities.Base;
using System.Linq.Expressions;

namespace Blog.Domain.IRepository
{
    /// <summary>
    /// Base repository interface for generic CRUD operations.
    /// </summary>
    /// <typeparam name="TEntity">blog 的实体类型</typeparam>
    public interface IBaseRepository<TEntity> where TEntity : BaseEntity
    {
        Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<IEnumerable<TEntity>> QueryByConditionAsync(Expression<Func<TEntity, bool>> whereExpression, CancellationToken cancellationToken = default);
        Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);
        Task<int> CountAsync(Expression<Func<TEntity, bool>>? whereExpression = null, CancellationToken cancellationToken = default);
        void Update(TEntity entity);
        void Remove(TEntity entity);

        /// <summary>
        /// 手动应用乐观锁（整数版本号 + SQL 条件）：
        /// 以 expectedVersion 为基准，保存时生成
        /// UPDATE ... SET "Version" = @expected + 1 WHERE "Id" = @id AND "Version" = @expected。
        /// 版本不匹配时 0 行受影响，EF Core 抛出 DbUpdateConcurrencyException。
        /// </summary>
        void ApplyOptimisticVersion(TEntity entity, int expectedVersion);
    }
}
