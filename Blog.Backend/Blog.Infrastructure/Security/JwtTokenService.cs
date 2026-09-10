using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Blog.Application.Interfaces;
using Blog.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Blog.Infrastructure.Security
{
    /// <summary>
    /// JWT 签发与解析（HS256）。
    ///
    /// 本项目**只发 Access Token，不做 Refresh Token**（T7），有效期由 JwtOptions 控制。
    /// 由于 JWT 无状态、无法主动失效，采用 <see cref="User.TokenVersion"/> 方案补偿：
    /// 签发时把当前 TokenVersion 写入 claim，请求校验时与库中值比对，
    /// 改密码 / 踢下线只需把 TokenVersion +1（见 docs/tech.md §2.5 坑 1）。
    /// </summary>
    public sealed class JwtTokenService : ITokenService
    {
        /// <summary>自定义 claim：关联作者 Id 与 TokenVersion</summary>
        public const string AuthorIdClaim = "authorId";
        public const string TokenVersionClaim = "tv";

        private readonly JwtOptions _options;

        public JwtTokenService(IOptions<JwtOptions> options)
        {
            _options = options.Value;
        }

        public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
        {
            if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
            {
                throw new InvalidOperationException(
                    "Jwt:SigningKey 未配置或长度不足 32 字节。它是敏感信息，必须通过环境变量 / 用户机密注入，不能写进 appsettings。");
            }

            var now = DateTimeOffset.UtcNow;
            var expires = now.AddMinutes(_options.AccessTokenMinutes <= 0 ? 30 : _options.AccessTokenMinutes);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                // role 用标准 ClaimTypes.Role，便于 [Authorize(Roles=...)] 与策略直接使用
                new(ClaimTypes.Role, user.Role.ToString()),
                new(TokenVersionClaim, user.TokenVersion.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            };

            if (user.AuthorId.HasValue)
                claims.Add(new Claim(AuthorIdClaim, user.AuthorId.Value.ToString()));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                notBefore: now.UtcDateTime,
                expires: expires.UtcDateTime,
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return (new JwtSecurityTokenHandler().WriteToken(token), expires);
        }

        public CurrentUserInfo? Resolve(ClaimsPrincipal? principal)
        {
            if (principal?.Identity?.IsAuthenticated != true) return null;

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(sub, out var userId)) return null;

            var roleText = principal.FindFirstValue(ClaimTypes.Role);
            if (!Enum.TryParse<UserRole>(roleText, ignoreCase: true, out var role)) return null;

            Guid? authorId = null;
            if (Guid.TryParse(principal.FindFirstValue(AuthorIdClaim), out var aid))
                authorId = aid;

            var tv = 0;
            if (int.TryParse(principal.FindFirstValue(TokenVersionClaim), out var parsed)) tv = parsed;

            return new CurrentUserInfo(userId, role, authorId, tv);
        }
    }
}
