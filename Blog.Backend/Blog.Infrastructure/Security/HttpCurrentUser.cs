using Blog.Application.Interfaces;
using Blog.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace Blog.Infrastructure.Security
{
    /// <summary>
    /// 从当前请求读取登录用户。数据由 <see cref="CurrentUserResolutionMiddleware"/>
    /// 在管道早期解析并放入 <see cref="HttpContext.Items"/>，因此这里全部是同步读取，无阻塞风险。
    /// </summary>
    public sealed class HttpCurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpCurrentUser(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ResolvedUser? Current =>
            _httpContextAccessor.HttpContext?.Items[CurrentUserResolutionMiddleware.ItemsKey] as ResolvedUser;

        public bool IsAuthenticated => Current is not null;

        public Guid? UserId => Current?.UserId;

        public UserRole? Role => Current?.Role;

        public Guid? AuthorId => Current?.AuthorId;
    }
}
