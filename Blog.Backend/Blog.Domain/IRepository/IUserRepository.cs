using Blog.Domain.Entities;

namespace Blog.Domain.IRepository
{
    public interface IUserRepository : IBaseRepository<User>
    {
        /// <summary>按登录邮箱查账号（含禁用与软删除过滤由全局过滤器负责）</summary>
        Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

        /// <summary>邮箱是否已被占用。excludeId 用于更新时排除自己</summary>
        Task<bool> ExistsByEmailAsync(string email, Guid? excludeId = null, CancellationToken cancellationToken = default);
    }
}
