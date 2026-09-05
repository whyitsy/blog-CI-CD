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
        Task<int> CountAsync(Expression<Func<TEntity, bool>>? whereExpression = null);
    }
}
