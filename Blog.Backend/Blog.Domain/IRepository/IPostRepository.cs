using Blog.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Blog.Domain.IRepository
{
    public interface IPostRepository : IBaseRepository<Post>
    {
        /// <summary>
        /// 数据库原子自增浏览量。
        /// 高频并发写不经过变更追踪与乐观锁，避免详情页访问互踩产生 409
        /// </summary>
        Task<int> IncrementViewCountAsync(Guid postId);
    }
}
