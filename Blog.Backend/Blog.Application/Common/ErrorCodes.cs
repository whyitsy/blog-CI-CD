namespace Blog.Application.Common
{
    /// <summary>统一响应码</summary>
    public static class ErrorCodes
    {
        /// <summary>成功</summary>
        public const int Ok = 0;

        /// <summary>参数校验失败</summary>
        public const int InvalidArgument = 4001;

        /// <summary>业务规则不满足（如分类名重复）</summary>
        public const int BusinessRule = 4002;

        /// <summary>资源重复（如邮箱已被占用）</summary>
        public const int DuplicateResource = 4003;

        /// <summary>未认证：缺少 token、token 无效/过期、账号被禁用</summary>
        public const int Unauthorized = 4010;

        /// <summary>无权限：已认证但角色不足，或越权访问他人资源</summary>
        public const int Forbidden = 4030;

        /// <summary>资源不存在</summary>
        public const int NotFound = 4040;

        /// <summary>乐观锁并发冲突</summary>
        public const int ConcurrencyConflict = 4090;

        /// <summary>触发限流</summary>
        public const int RateLimited = 4091;

        /// <summary>请求体过大（如上传文件超过体积限制）</summary>
        public const int PayloadTooLarge = 4130;

        /// <summary>系统异常</summary>
        public const int InternalError = 5000;
    }
}
