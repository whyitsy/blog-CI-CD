using Blog.Domain.Entities.Base;
using Blog.Domain.IRepository;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class BaseRepository<TEntity> : IBaseRepository<TEntity> where TEntity : BaseEntity
    {
        protected readonly BlogDbContext _context;
        public BaseRepository(BlogDbContext context)
        {
            _context = context;
        }

        public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await _context.Set<TEntity>().AddAsync(entity, cancellationToken);
            return entity;
        }

        public async Task<int> CountAsync(Expression<Func<TEntity, bool>>? whereExpression = null, CancellationToken cancellationToken = default)
        {
            return whereExpression == null
                ? await _context.Set<TEntity>().CountAsync(cancellationToken)
                : await _context.Set<TEntity>().CountAsync(whereExpression, cancellationToken);
        }

        public async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Set<TEntity>().AsNoTracking().ToListAsync(cancellationToken);
        }

        public virtual async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }

        /// <summary>
        /// 根据条件查询实体集合，返回不被追踪的实体对象
        /// </summary>
        public async Task<IEnumerable<TEntity>> QueryByConditionAsync(Expression<Func<TEntity, bool>> whereExpression, CancellationToken cancellationToken = default)
        {
            return await _context.Set<TEntity>().Where(whereExpression).AsNoTracking().ToListAsync(cancellationToken);
        }

        public void Update(TEntity entity)
        {
            _context.Set<TEntity>().Update(entity);
        }

        /// <summary>软删除：仅打标记，由 UoW 统一提交</summary>
        public void Remove(TEntity entity)
        {
            entity.Delete();
            _context.Set<TEntity>().Update(entity);
        }

        /// <summary>
        /// 手动应用乐观锁（整数版本号 + SQL 条件）。
        /// 把客户端持有的版本号写入 EF 原始值并把当前值 +1，
        /// 保存时生成 UPDATE ... SET "Version" = @expected + 1 WHERE "Id" = @id AND "Version" = @expected，
        /// 版本不匹配时 0 行受影响 -> DbUpdateConcurrencyException。
        /// 整数版本号与数据库无关，可跨 PostgreSQL / MySQL / SQLite 等任意 EF 支持的库。
        /// </summary>
        public void ApplyOptimisticVersion(TEntity entity, int expectedVersion)
        {
            if (expectedVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(expectedVersion), "版本号必须 >= 1");
            var version = _context.Entry(entity).Property(nameof(BaseEntity.Version));
            version.OriginalValue = expectedVersion; // 作为 WHERE 条件
            version.CurrentValue = expectedVersion + 1;
        }
    }
}
