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
        Task<TEntity?> GetByIdAsync(Guid id);
        Task<IEnumerable<TEntity>> GetAllAsync();
        Task<IEnumerable<TEntity>> QueryByConditionAsync(Expression<Func<TEntity, bool>> whereExpression);
        Task<TEntity> AddAsync(TEntity entity);

        /// <summary>标记实体为已修改（配合 UoW 提交，乐观锁由 RowVersion 保证）</summary>
        void Update(TEntity entity);

        /// <summary>软删除实体（调用 entity.Delete()，配合 UoW 提交）</summary>
        void Remove(TEntity entity);

        Task<int> CountAsync(Expression<Func<TEntity, bool>>? whereExpression = null);
    }
}
