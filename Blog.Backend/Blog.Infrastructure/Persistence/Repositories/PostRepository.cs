using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using Microsoft.EntityFrameworkCore;

namespace Blog.Infrastructure.Persistence.Repositories
{
    public class PostRepository : BaseRepository<Post>, IPostRepository
    {
        public PostRepository(BlogDbContext context) : base(context)
        {
        }

        /// <summary>
        /// 重写：加载文章时带上标签集合（被跟踪），保证更新时标签关系增量同步而非全量重插
        /// </summary>
        public override async Task<Post?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await _context.Posts
                .Include(p => p.Tags)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        }

        /// <summary>
        /// 原子自增浏览量（ExecuteUpdate 不走变更追踪与乐观锁，高频访问下避免并发冲突）
        /// </summary>
        public async Task<int> IncrementViewCountAsync(Guid postId, CancellationToken cancellationToken = default)
        {
            return await _context.Posts
                .Where(p => p.Id == postId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1), cancellationToken);
        }
    }
}
