using Blog.Domain.Entities;
using Blog.Domain.Entities.Base;

namespace Blog.Domain.IRepository
{
    public interface IPostRepository : IBaseRepository<Post>
    {
        /// <summary>浏览量原子自增（不走变更追踪与乐观锁，适合高频访问场景）</summary>
        Task<int> IncrementViewCountAsync(Guid postId, CancellationToken cancellationToken = default);
    }
}
