using Blog.Domain.Entities;

namespace Blog.Application.Interfaces
{
    /// <summary>
    /// 当前请求的登录用户上下文。
    /// Application 层通过它拿到「谁在操作」，而不直接依赖 HttpContext（保持可测试）。
    /// 未登录时 <see cref="IsAuthenticated"/> 为 false。
    /// </summary>
    public interface ICurrentUser
    {
        bool IsAuthenticated { get; }

        Guid? UserId { get; }

        /// <summary>角色。未登录时为 null</summary>
        UserRole? Role { get; }

        /// <summary>关联的作者 Id（内容归属校验用）。未关联时为 null</summary>
        Guid? AuthorId { get; }

        bool IsAdmin => Role == UserRole.Admin;

        /// <summary>已登录、角色为 Author 或 Admin（即「能写内容的人」）</summary>
        bool CanWriteContent => IsAuthenticated && Role is UserRole.Admin or UserRole.Author;

        /// <summary>
        /// 是否可以操作指定作者的内容：
        /// 管理员可以操作全部；作者只能操作自己的（AuthorId 一致）。
        /// </summary>
        bool CanManageAuthor(Guid? targetAuthorId) =>
            IsAdmin || (Role == UserRole.Author && AuthorId.HasValue && AuthorId == targetAuthorId);

        /// <summary>是否可以操作由指定账号创建的内容</summary>
        bool CanManageOwnedBy(Guid? createdByUserId) =>
            IsAdmin || (IsAuthenticated && createdByUserId.HasValue && createdByUserId == UserId);
    }
}
