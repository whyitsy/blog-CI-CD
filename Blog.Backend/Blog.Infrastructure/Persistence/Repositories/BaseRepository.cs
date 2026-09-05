using Blog.Domain.Entities.Base;
using Blog.Domain.IRepository;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class BaseRepository<TEntity> : IBaseRepository<TEntity> where TEntity : BaseEntity
    {
        private readonly BlogDbContext _context;
        public BaseRepository(BlogDbContext context)
        {
            _context = context;
        }

        public async Task<TEntity> AddAsync(TEntity entity)
        {
            await _context.Set<TEntity>().AddAsync(entity);
            return entity;
        }

        public async Task<int> CountAsync(Expression<Func<TEntity, bool>>? whereExpression = null)
        {
            if (whereExpression == null)
                return await _context.Set<TEntity>().CountAsync();         
            else
                return await _context.Set<TEntity>().CountAsync(whereExpression);
        }
        public async Task<IEnumerable<TEntity>> GetAllAsync()
        {
            return await _context.Set<TEntity>().AsNoTracking().ToListAsync();
        }

        public async Task<TEntity?> GetByIdAsync(Guid id)
        {
            return await _context.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id);
        }

        /// <summary>
        /// 根据条件查询实体集合，返回不被追踪的实体对象
        /// </summary>
        /// <param name="whereExpression"></param>
        /// <returns></returns>
        public async Task<IEnumerable<TEntity>> QueryByConditionAsync(Expression<Func<TEntity, bool>> whereExpression)
        {
            return await _context.Set<TEntity>().Where(whereExpression).AsNoTracking().ToListAsync();
        }
    }
}
