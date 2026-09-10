using System.Security.Claims;
using Blog.Domain.Entities;

namespace Blog.Application.Interfaces
{
    /// <summary>JWT 签发抽象</summary>
    public interface ITokenService
    {
        /// <summary>
        /// 为账号签发 Access Token。
        /// claims 约定：sub=用户Id、role=角色、authorId=关联作者Id(可空)、tv=TokenVersion。
        /// </summary>
        /// <returns>token 字符串与过期时间</returns>
        (string Token, DateTimeOffset ExpiresAt) Issue(User user);

        /// <summary>
        /// 从当前请求的 ClaimsPrincipal 解析出用户身份。
        /// 用于校验 TokenVersion（token 有效但用户已被踢下线/改密时需拒绝）。
        /// </summary>
        CurrentUserInfo? Resolve(ClaimsPrincipal? principal);
    }

    /// <summary>从 JWT claims 解析出的当前用户信息</summary>
    public sealed record CurrentUserInfo(Guid UserId, UserRole Role, Guid? AuthorId, int TokenVersion);
}
