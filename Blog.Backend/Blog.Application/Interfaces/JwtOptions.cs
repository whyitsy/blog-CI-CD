namespace Blog.Application.Interfaces
{
    /// <summary>JWT 配置（对应 appsettings 的 Jwt 节）</summary>
    public class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; set; } = "blog-api";

        public string Audience { get; set; } = "blog-frontend";

        /// <summary>
        /// 签名密钥。**必须来自环境变量 / 用户机密 / 密钥库，禁止入库**。
        /// 长度需 >= 32 字节（HS256 要求）。
        /// </summary>
        public string SigningKey { get; set; } = string.Empty;

        /// <summary>
        /// Access Token 有效期（分钟）。默认 30。
        /// 本项目**不做 Refresh Token**（T7），因此这个值直接决定用户多久需要重新登录。
        /// </summary>
        public int AccessTokenMinutes { get; set; } = 30;
    }

    /// <summary>部署拓扑配置（对应 appsettings 的 Deployment 节）</summary>
    public class DeploymentOptions
    {
        public const string SectionName = "Deployment";

        /// <summary>实例数量。> 1 时强制要求 Redis（见 docs/03-后端设计.md §11 / T5）</summary>
        public int InstanceCount { get; set; } = 1;

        /// <summary>是否强制要求 Redis（即使 InstanceCount 为 1 也可显式要求）</summary>
        public bool RequireRedis { get; set; }
    }
}
