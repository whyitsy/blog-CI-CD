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
        /// 
        /// 这里会设置 实体 的 Version 属性进行乐观锁控制，即添加到后面的where语句中(DBContext中配置IsConcurrencyToken)，这样在保存时就会检查版本号是否匹配，如果不匹配则抛出异常。
        /// </summary>
        void ApplyOptimisticVersion(TEntity entity, int expectedVersion);
    }
}
