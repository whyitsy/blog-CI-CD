using Blog.Domain.Entities.Base;

namespace Blog.Domain.Entities
{
    /// <summary>
    /// 账号角色。只有两档，不引入细粒度 RBAC（见 docs/tech.md §2.4）。
    /// </summary>
    public enum UserRole
    {
        /// <summary>内容作者：只能管理自己的文章与个人资料</summary>
        Author = 0,

        /// <summary>站点管理员：管理全部内容、账号、作者与站点配置</summary>
        Admin = 1,
    }

    /// <summary>
    /// 后台/前台登录账号。
    ///
    /// 与 <see cref="Author"/> 的分工（见 docs/tech.md §2.2）：
    ///   - User 是「账号」，承载登录凭据（PasswordHash）与角色
    ///   - Author 是「内容属性」，只承载署名展示信息（姓名/头像/简介），与 Post/Tag/Category 同层
    /// 二者通过可空的 <see cref="AuthorId"/> 弱关联，允许存在没有账号的作者。
    /// </summary>
    public class User : BaseEntity
    {
        /// <summary>登录邮箱（唯一），非展示用联系方式</summary>
        public string Email { get; private set; } = string.Empty;

        /// <summary>
        /// 密码哈希。格式由 IPasswordHasher 定义，当前为
        /// pbkdf2-sha512$迭代次数$base64盐$base64哈希
        /// 绝不存明文，也绝不用 MD5/SHA 直接哈希（见 docs/tech.md §2.6）。
        /// </summary>
        public string PasswordHash { get; private set; } = string.Empty;

        /// <summary>角色</summary>
        public UserRole Role { get; private set; } = UserRole.Author;

        /// <summary>是否启用。禁用后不能登录（推荐禁用而非删除，以保留审计线索）</summary>
        public bool IsActive { get; private set; } = true;

        /// <summary>关联的署名对象（可空）。为空表示该账号没有对应的 Author 记录</summary>
        public Guid? AuthorId { get; private set; }

        /// <summary>导航属性</summary>
        public Author? Author { get; set; }

        /// <summary>最近登录时间（审计用）</summary>
        public DateTimeOffset? LastLoginAt { get; private set; }

        /// <summary>
        /// Token 版本号。签发 JWT 时写入 claim，校验时与库中值比对；
        /// 改密码或「踢下线」只需 +1，即可让该用户所有旧 token 立即失效。
        /// 相比 Redis 黑名单，它不引入新的运行时依赖，多实例下也有效（见 docs/backend.md §6.5）。
        /// </summary>
        public int TokenVersion { get; private set; } = 1;

        private User() { } // EF Core 需要无参构造函数

        public User(string email, string passwordHash, UserRole role, Guid? authorId = null)
        {
            Email = email;
            PasswordHash = passwordHash;
            Role = role;
            AuthorId = authorId;
            IsActive = true;
            CreatedAt = DateTimeOffset.UtcNow;
            IsDeleted = false;
        }

        /// <summary>更新联系方式与角色归属（不动凭据）</summary>
        public void UpdateProfile(UserRole role, Guid? authorId, bool isActive)
        {
            Role = role;
            AuthorId = authorId;
            IsActive = isActive;
        }

        /// <summary>重置密码：同时提升 TokenVersion，使旧 token 立即失效</summary>
        public void ResetPassword(string passwordHash)
        {
            PasswordHash = passwordHash;
            TokenVersion++;
        }

        /// <summary>启用/停用。停用同时提升 TokenVersion，立即踢下线</summary>
        public void SetActive(bool isActive)
        {
            if (IsActive == isActive) return;

            IsActive = isActive;
            if (!isActive)
                TokenVersion++;
        }

        /// <summary>登录成功后记录时间</summary>
        public void MarkLoggedIn()
        {
            LastLoginAt = DateTimeOffset.UtcNow;
        }

        /// <summary>主动注销：提升 TokenVersion 使当前及所有旧 token 失效</summary>
        public void LogoutAllDevices()
        {
            TokenVersion++;
        }
    }
}
