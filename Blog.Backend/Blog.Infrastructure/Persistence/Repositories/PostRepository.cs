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
